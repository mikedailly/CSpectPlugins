// FlameGraph — pure C# port of Brendan Gregg's flamegraph.pl /
// flamegraph.py for use by the CSpect Profiler plugin.
//
// Generates an interactive SVG flame graph from a folded-stack dictionary
// (the same format produced by ResolveAndDump). Supports:
//   - Hover tooltips
//   - Click-to-zoom
//   - Reset zoom
//   - Ctrl-F search with regex highlight
//   - Case-toggle (Ctrl-I)
//
// Original flamegraph.pl: Copyright 2016 Netflix, 2011 Joyent, 2011 Brendan Gregg.
// CDDL licensed.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Profiler
{
    internal static class FlameGraph
    {
        // ----- Tunables (defaults from flamegraph.pl) ----------------------

        const int    ImageWidth   = 1200;
        const int    FrameHeight  = 16;
        const int    FontSize     = 12;
        const double FontWidth    = 0.59;     // approx average glyph width / fontsize
        const int    FramePad     = 1;
        const int    XPad         = 10;
        const string FontType     = "Verdana";
        const string CountName    = "samples";
        const string SearchColor  = "rgb(230,0,230)";
        const string BgColor1     = "#eeeeee";
        const string BgColor2     = "#eeeeb0";  // yellow theme

        // ----- Public API --------------------------------------------------

        // Writes a flame graph SVG built from `stacks` (folded-format keys
        // "frame1;frame2;...;leaf" → sample count) to `outputPath`.
        public static void Write(Dictionary<string, long> stacks, string outputPath, string title = "Flame Graph")
        {
            if (stacks == null || stacks.Count == 0)
            {
                File.WriteAllText(outputPath, EmptyGraph("No samples"));
                return;
            }

            // Sort the stack lines so that consecutive lines share as much
            // common prefix as possible. The flow algorithm depends on this.
            var sortedKeys = new List<string>(stacks.Keys);
            sortedKeys.Sort(StringComparer.Ordinal);

            // ----- Run the flow algorithm to build node list --------------
            var nodes = new List<Frame>();
            var openFrames = new Dictionary<string, OpenFrame>();
            string[] last = new string[0];
            long timeVal = 0;

            foreach (string stackKey in sortedKeys)
            {
                long samples = stacks[stackKey];
                // Prepend an empty root so all stacks share a common base
                string[] split = stackKey.Split(';');
                string[] current = new string[split.Length + 1];
                current[0] = "";
                Array.Copy(split, 0, current, 1, split.Length);

                Flow(last, current, timeVal, openFrames, nodes);
                last = current;
                timeVal += samples;
            }
            // Close out any frames that are still open at end-of-input
            Flow(last, new string[0], timeVal, openFrames, nodes);

            if (timeVal == 0)
            {
                File.WriteAllText(outputPath, EmptyGraph("Stack count is zero"));
                return;
            }

            // ----- Determine canvas dimensions ----------------------------
            int depthMax = 0;
            foreach (Frame f in nodes)
                if (f.Depth > depthMax) depthMax = f.Depth;

            int yPad1 = FontSize * 3;       // top padding (title)
            int yPad2 = FontSize * 2 + 10;  // bottom padding (details)
            int imageHeight = ((depthMax + 1) * FrameHeight) + yPad1 + yPad2;
            double widthPerTime = (double)(ImageWidth - 2 * XPad) / timeVal;

            // ----- Emit SVG -----------------------------------------------
            var sb = new StringBuilder();
            EmitHeader(sb, ImageWidth, imageHeight);
            EmitDefsAndScript(sb);
            EmitFilledRect(sb, 0, 0, ImageWidth, imageHeight, "url(#background)", "");
            EmitText(sb, "title",      ImageWidth / 2.0,  FontSize * 2, title,        ' ',  TitleStyle());
            EmitText(sb, "details",    XPad,              imageHeight - (yPad2 / 2.0), " ", ' ',  "");
            EmitText(sb, "unzoom",     XPad,              FontSize * 2, "Reset Zoom", ' ',  "class=\"hide\"");
            EmitText(sb, "search",     ImageWidth - XPad - 100, FontSize * 2,                   "Search", ' ', "");
            EmitText(sb, "ignorecase", ImageWidth - XPad - 16,  FontSize * 2,                   "ic",     ' ', "");
            EmitText(sb, "matched",    ImageWidth - XPad - 100, imageHeight - (yPad2 / 2.0),    " ",      ' ', "");

            sb.Append("<g id=\"frames\">\n");
            foreach (Frame f in nodes)
            {
                long etime = f.EndTime;
                if (string.IsNullOrEmpty(f.Func) && f.Depth == 0)
                    etime = timeVal;

                double x1 = XPad + f.StartTime * widthPerTime;
                double x2 = XPad + etime      * widthPerTime;
                double y1 = imageHeight - yPad2 - (f.Depth + 1) * FrameHeight + FramePad;
                double y2 = imageHeight - yPad2 -  f.Depth      * FrameHeight;

                long samples = etime - f.StartTime;
                string samplesStr = AddCommas(samples.ToString(CultureInfo.InvariantCulture));
                double pct = 100.0 * samples / timeVal;

                string info;
                string func = f.Func;
                if (string.IsNullOrEmpty(func) && f.Depth == 0)
                {
                    info = "all (" + samplesStr + " " + CountName + ", 100%)";
                    func = "all";
                }
                else
                {
                    info = XmlEscape(func) + " (" + samplesStr + " " + CountName + ", " +
                           pct.ToString("F2", CultureInfo.InvariantCulture) + "%)";
                }

                sb.Append("<g>\n<title>").Append(XmlEscape(info)).Append("</title>\n");

                string color = HotColor(func);
                EmitFilledRect(sb, x1, y1, x2, y2, color, "rx=\"2\" ry=\"2\"");

                int chars = (int)((x2 - x1) / (FontSize * FontWidth));
                string text = "";
                if (chars >= 3)
                {
                    text = func.Length <= chars
                        ? func
                        : func.Substring(0, Math.Max(0, chars - 2)) + "..";
                    text = XmlEscape(text);
                }
                EmitText(sb, null, x1 + 3, 3 + (y1 + y2) / 2.0, text, ' ', "");

                sb.Append("</g>\n");
            }
            sb.Append("</g>\n");
            sb.Append("</svg>\n");

            File.WriteAllText(outputPath, sb.ToString());
        }

        // ----- Flow algorithm: stacks → frame rectangles -------------------

        private struct OpenFrame { public long StartTime; }

        private struct Frame
        {
            public string Func;
            public int    Depth;
            public long   StartTime;
            public long   EndTime;
        }

        // Compares two stacks and emits "closing" frames for the parts of
        // `last` that aren't in `current`, and "opening" frames for the parts
        // of `current` that aren't in `last`. Open frames are tracked in
        // `openFrames`; completed frames are appended to `nodes`.
        private static void Flow(string[] last, string[] current, long now,
                                 Dictionary<string, OpenFrame> openFrames,
                                 List<Frame> nodes)
        {
            int common = 0;
            int minLen = Math.Min(last.Length, current.Length);
            while (common < minLen && last[common] == current[common])
                common++;

            // Close frames from `last` that are no longer present
            for (int i = last.Length - 1; i >= common; i--)
            {
                string key = last[i] + ";" + i;
                OpenFrame open;
                if (openFrames.TryGetValue(key, out open))
                {
                    nodes.Add(new Frame
                    {
                        Func = last[i],
                        Depth = i,
                        StartTime = open.StartTime,
                        EndTime = now
                    });
                    openFrames.Remove(key);
                }
            }

            // Open new frames from `current`
            for (int i = common; i < current.Length; i++)
            {
                string key = current[i] + ";" + i;
                openFrames[key] = new OpenFrame { StartTime = now };
            }
        }

        // ----- Color generation: deterministic hot palette -----------------

        // Pseudo-random hash based on the sum of character codes; uses the
        // same .NET Random seeding so the same name always gets the same color.
        private static double RandomNameHash(string name)
        {
            int sum = 0;
            for (int i = 0; i < name.Length; i++) sum += name[i];
            Random r = new Random(sum);
            return r.NextDouble();
        }

        // "hot" palette from flamegraph.pl: red base with green/blue variation.
        private static string HotColor(string name)
        {
            double v1 = RandomNameHash(name);
            double v2 = RandomNameHash(name);
            double v3 = RandomNameHash(name);
            int r = 205 + (int)(50 * v3);
            int g = (int)(230 * v1);
            int b = (int)(55  * v2);
            return "rgb(" + r + "," + g + "," + b + ")";
        }

        // ----- SVG primitives ----------------------------------------------

        private static void EmitHeader(StringBuilder sb, int w, int h)
        {
            sb.Append("<?xml version=\"1.0\" standalone=\"no\"?>\n");
            sb.Append("<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" ");
            sb.Append("\"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">\n");
            sb.Append("<svg version=\"1.1\" width=\"").Append(w).Append("\" height=\"").Append(h);
            sb.Append("\" onload=\"init(evt)\" viewBox=\"0 0 ").Append(w).Append(" ").Append(h);
            sb.Append("\" xmlns=\"http://www.w3.org/2000/svg\" ");
            sb.Append("xmlns:xlink=\"http://www.w3.org/1999/xlink\">\n");
            sb.Append("<!-- Generated by CSpect Profiler plugin (FlameGraphSvg) -->\n");
        }

        private static void EmitFilledRect(StringBuilder sb, double x1, double y1,
                                           double x2, double y2, string fill, string extra)
        {
            sb.Append("<rect x=\"").Append(F1(x1)).Append("\" y=\"").Append(F1(y1));
            sb.Append("\" width=\"").Append(F1(x2 - x1)).Append("\" height=\"").Append(F1(y2 - y1));
            sb.Append("\" fill=\"").Append(fill).Append("\" ");
            if (!string.IsNullOrEmpty(extra)) sb.Append(extra).Append(" ");
            sb.Append("/>\n");
        }

        private static void EmitText(StringBuilder sb, string id, double x, double y,
                                     string s, char dummy, string extra)
        {
            sb.Append("<text ");
            if (!string.IsNullOrEmpty(id)) sb.Append("id=\"").Append(id).Append("\" ");
            sb.Append("x=\"").Append(F2(x)).Append("\" y=\"").Append(F0(y)).Append("\" ");
            if (!string.IsNullOrEmpty(extra)) sb.Append(extra).Append(" ");
            sb.Append(">").Append(s).Append("</text>\n");
        }

        private static string TitleStyle()
        {
            return "text-anchor=\"middle\" font-size=\"" + (FontSize + 5) + "\"";
        }

        private static string EmptyGraph(string message)
        {
            int w = ImageWidth;
            int h = FontSize * 5;
            var sb = new StringBuilder();
            EmitHeader(sb, w, h);
            EmitText(sb, null, w / 2.0, FontSize * 2, XmlEscape(message), ' ',
                     "text-anchor=\"middle\" font-family=\"" + FontType +
                     "\" font-size=\"" + FontSize + "\"");
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        // ----- Defs / styles / embedded JavaScript -------------------------

        private static void EmitDefsAndScript(StringBuilder sb)
        {
            sb.Append("<defs>\n");
            sb.Append("\t<linearGradient id=\"background\" y1=\"0\" y2=\"1\" x1=\"0\" x2=\"0\">\n");
            sb.Append("\t\t<stop stop-color=\"").Append(BgColor1).Append("\" offset=\"5%\" />\n");
            sb.Append("\t\t<stop stop-color=\"").Append(BgColor2).Append("\" offset=\"95%\" />\n");
            sb.Append("\t</linearGradient>\n");
            sb.Append("</defs>\n");

            sb.Append("<style type=\"text/css\">\n");
            sb.Append("\ttext { font-family:").Append(FontType);
            sb.Append("; font-size:").Append(FontSize).Append("px; fill:rgb(0,0,0); }\n");
            sb.Append("\t#search, #ignorecase { opacity:0.1; cursor:pointer; }\n");
            sb.Append("\t#search:hover, #search.show, #ignorecase:hover, #ignorecase.show { opacity:1; }\n");
            sb.Append("\t#title { text-anchor:middle; font-size:").Append(FontSize + 5).Append("px}\n");
            sb.Append("\t#unzoom { cursor:pointer; }\n");
            sb.Append("\t#frames > *:hover { stroke:black; stroke-width:0.5; cursor:pointer; }\n");
            sb.Append("\t.hide { display:none; }\n");
            sb.Append("\t.parent { opacity:0.5; }\n");
            sb.Append("</style>\n");

            sb.Append(JsCode);
        }

        // ----- Helpers -----------------------------------------------------

        private static string F0(double d) { return d.ToString("F0", CultureInfo.InvariantCulture); }
        private static string F1(double d) { return d.ToString("F1", CultureInfo.InvariantCulture); }
        private static string F2(double d) { return d.ToString("F2", CultureInfo.InvariantCulture); }

        private static string AddCommas(string n)
        {
            // Insert thousand separators into a numeric string
            int dot = n.IndexOf('.');
            string intPart = dot >= 0 ? n.Substring(0, dot) : n;
            string fracPart = dot >= 0 ? n.Substring(dot) : "";
            var sb = new StringBuilder();
            int len = intPart.Length;
            for (int i = 0; i < len; i++)
            {
                if (i > 0 && (len - i) % 3 == 0) sb.Append(',');
                sb.Append(intPart[i]);
            }
            sb.Append(fracPart);
            return sb.ToString();
        }

        private static string XmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }

        // ----- Embedded interactive JavaScript -----------------------------
        // Ported from flamegraph.py's js_code() with the four template
        // variables (nametype="Function:", fontsize=12, fontwidth=0.59,
        // xpad=10) substituted as constants.
        private const string JsCode = @"<script type=""text/ecmascript"">
<![CDATA[
	""use strict"";
	var details, searchbtn, unzoombtn, matchedtxt, svg, searching, currentSearchTerm, ignorecase, ignorecaseBtn;
	function init(evt) {
		details = document.getElementById(""details"").firstChild;
		searchbtn = document.getElementById(""search"");
		ignorecaseBtn = document.getElementById(""ignorecase"");
		unzoombtn = document.getElementById(""unzoom"");
		matchedtxt = document.getElementById(""matched"");
		svg = document.getElementsByTagName(""svg"")[0];
		searching = 0;
		currentSearchTerm = null;

		var params = get_params();
		if (params.x && params.y)
			zoom(find_group(document.querySelector('[x=""' + params.x + '""][y=""' + params.y + '""]')));
		if (params.s) search(params.s);
	}

	window.addEventListener(""click"", function(e) {
		var target = find_group(e.target);
		if (target) {
			if (target.nodeName == ""a"") {
				if (e.ctrlKey === false) return;
				e.preventDefault();
			}
			if (target.classList.contains(""parent"")) unzoom(true);
			zoom(target);
			if (!document.querySelector('.parent')) {
				var params = get_params();
				if (params.x) delete params.x;
				if (params.y) delete params.y;
				history.replaceState(null, null, parse_params(params));
				unzoombtn.classList.add(""hide"");
				return;
			}
			var el = target.querySelector(""rect"");
			if (el && el.attributes && el.attributes.y && el.attributes._orig_x) {
				var params = get_params()
				params.x = el.attributes._orig_x.value;
				params.y = el.attributes.y.value;
				history.replaceState(null, null, parse_params(params));
			}
		}
		else if (e.target.id == ""unzoom"") clearzoom();
		else if (e.target.id == ""search"") search_prompt();
		else if (e.target.id == ""ignorecase"") toggle_ignorecase();
	}, false)

	window.addEventListener(""mouseover"", function(e) {
		var target = find_group(e.target);
		if (target) details.nodeValue = ""Function: "" + g_to_text(target);
	}, false)

	window.addEventListener(""mouseout"", function(e) {
		var target = find_group(e.target);
		if (target) details.nodeValue = ' ';
	}, false)

	window.addEventListener(""keydown"",function (e) {
		if (e.keyCode === 114 || (e.ctrlKey && e.keyCode === 70)) {
			e.preventDefault();
			search_prompt();
		}
		else if (e.ctrlKey && e.keyCode === 73) {
			e.preventDefault();
			toggle_ignorecase();
		}
	}, false)

	function get_params() {
		var params = {};
		var paramsarr = window.location.search.substr(1).split('&');
		for (var i = 0; i < paramsarr.length; ++i) {
			var tmp = paramsarr[i].split(""="");
			if (!tmp[0] || !tmp[1]) continue;
			params[tmp[0]]  = decodeURIComponent(tmp[1]);
		}
		return params;
	}
	function parse_params(params) {
		var uri = ""?"";
		for (var key in params) {
			uri += key + '=' + encodeURIComponent(params[key]) + '&';
		}
		if (uri.slice(-1) == ""&"")
			uri = uri.substring(0, uri.length - 1);
		if (uri == '?')
			uri = window.location.href.split('?')[0];
		return uri;
	}
	function find_child(node, selector) {
		var children = node.querySelectorAll(selector);
		if (children.length) return children[0];
	}
	function find_group(node) {
		var parent = node.parentElement;
		if (!parent) return;
		if (parent.id == ""frames"") return node;
		return find_group(parent);
	}
	function orig_save(e, attr, val) {
		if (e.attributes[""_orig_"" + attr] != undefined) return;
		if (e.attributes[attr] == undefined) return;
		if (val == undefined) val = e.attributes[attr].value;
		e.setAttribute(""_orig_"" + attr, val);
	}
	function orig_load(e, attr) {
		if (e.attributes[""_orig_""+attr] == undefined) return;
		e.attributes[attr].value = e.attributes[""_orig_"" + attr].value;
		e.removeAttribute(""_orig_""+attr);
	}
	function g_to_text(e) {
		var text = find_child(e, ""title"").firstChild.nodeValue;
		return (text)
	}
	function g_to_func(e) {
		var func = g_to_text(e);
		return (func);
	}
	function update_text(e) {
		var r = find_child(e, ""rect"");
		var t = find_child(e, ""text"");
		var w = parseFloat(r.attributes.width.value) -3;
		var txt = find_child(e, ""title"").textContent.replace(/\([^(]*\)$/,"""");
		t.attributes.x.value = parseFloat(r.attributes.x.value) + 3;

		if (w < 2 * 12 * 0.59) {
			t.textContent = """";
			return;
		}

		t.textContent = txt;
		var sl = t.getSubStringLength(0, txt.length);
		if (/^ *$/.test(txt) || sl < w)
			return;

		var start = Math.floor((w/sl) * txt.length);
		for (var x = start; x > 0; x = x-2) {
			if (t.getSubStringLength(0, x + 2) <= w) {
				t.textContent = txt.substring(0, x) + "".."";
				return;
			}
		}
		t.textContent = """";
	}

	function zoom_reset(e) {
		if (e.attributes != undefined) {
			orig_load(e, ""x"");
			orig_load(e, ""width"");
		}
		if (e.childNodes == undefined) return;
		for (var i = 0, c = e.childNodes; i < c.length; i++) {
			zoom_reset(c[i]);
		}
	}
	function zoom_child(e, x, ratio) {
		if (e.attributes != undefined) {
			if (e.attributes.x != undefined) {
				orig_save(e, ""x"");
				e.attributes.x.value = (parseFloat(e.attributes.x.value) - x - 10) * ratio + 10;
				if (e.tagName == ""text"")
					e.attributes.x.value = find_child(e.parentNode, ""rect[x]"").attributes.x.value + 3;
			}
			if (e.attributes.width != undefined) {
				orig_save(e, ""width"");
				e.attributes.width.value = parseFloat(e.attributes.width.value) * ratio;
			}
		}

		if (e.childNodes == undefined) return;
		for (var i = 0, c = e.childNodes; i < c.length; i++) {
			zoom_child(c[i], x - 10, ratio);
		}
	}
	function zoom_parent(e) {
		if (e.attributes) {
			if (e.attributes.x != undefined) {
				orig_save(e, ""x"");
				e.attributes.x.value = 10;
			}
			if (e.attributes.width != undefined) {
				orig_save(e, ""width"");
				e.attributes.width.value = parseInt(svg.width.baseVal.value) - (10 * 2);
			}
		}
		if (e.childNodes == undefined) return;
		for (var i = 0, c = e.childNodes; i < c.length; i++) {
			zoom_parent(c[i]);
		}
	}
	function zoom(node) {
		var attr = find_child(node, ""rect"").attributes;
		var width = parseFloat(attr.width.value);
		var xmin = parseFloat(attr.x.value);
		var xmax = parseFloat(xmin + width);
		var ymin = parseFloat(attr.y.value);
		var ratio = (svg.width.baseVal.value - 2 * 10) / width;

		var fudge = 0.0001;

		unzoombtn.classList.remove(""hide"");

		var el = document.getElementById(""frames"").children;
		for (var i = 0; i < el.length; i++) {
			var e = el[i];
			var a = find_child(e, ""rect"").attributes;
			var ex = parseFloat(a.x.value);
			var ew = parseFloat(a.width.value);
			var upstack;
			upstack = parseFloat(a.y.value) > ymin;
			if (upstack) {
				if (ex <= xmin && (ex+ew+fudge) >= xmax) {
					e.classList.add(""parent"");
					zoom_parent(e);
					update_text(e);
				}
				else
					e.classList.add(""hide"");
			}
			else {
				if (ex < xmin || ex + fudge >= xmax) {
					e.classList.add(""hide"");
				}
				else {
					zoom_child(e, xmin, ratio);
					update_text(e);
				}
			}
		}
		search();
	}
	function unzoom(dont_update_text) {
		unzoombtn.classList.add(""hide"");
		var el = document.getElementById(""frames"").children;
		for(var i = 0; i < el.length; i++) {
			el[i].classList.remove(""parent"");
			el[i].classList.remove(""hide"");
			zoom_reset(el[i]);
			if(!dont_update_text) update_text(el[i]);
		}
		search();
	}
	function clearzoom() {
		unzoom();
		var params = get_params();
		if (params.x) delete params.x;
		if (params.y) delete params.y;
		history.replaceState(null, null, parse_params(params));
	}

	function toggle_ignorecase() {
		ignorecase = !ignorecase;
		if (ignorecase) {
			ignorecaseBtn.classList.add(""show"");
		} else {
			ignorecaseBtn.classList.remove(""show"");
		}
		reset_search();
		search();
	}
	function reset_search() {
		var el = document.querySelectorAll(""#frames rect"");
		for (var i = 0; i < el.length; i++) {
			orig_load(el[i], ""fill"")
		}
		var params = get_params();
		delete params.s;
		history.replaceState(null, null, parse_params(params));
	}
	function search_prompt() {
		if (!searching) {
			var term = prompt(""Enter a search term (regexp ""
			    + ""allowed, eg: ^_set_)""
			    + (ignorecase ? "", ignoring case"" : """")
			    + ""\nPress Ctrl-i to toggle case sensitivity"", """");
			if (term != null) search(term);
		} else {
			reset_search();
			searching = 0;
			currentSearchTerm = null;
			searchbtn.classList.remove(""show"");
			searchbtn.firstChild.nodeValue = ""Search""
			matchedtxt.classList.add(""hide"");
			matchedtxt.firstChild.nodeValue = """"
		}
	}
	function search(term) {
		if (term) currentSearchTerm = term;
		if (currentSearchTerm === null) return;

		var re = new RegExp(currentSearchTerm, ignorecase ? 'i' : '');
		var el = document.getElementById(""frames"").children;
		var matches = new Object();
		var maxwidth = 0;
		for (var i = 0; i < el.length; i++) {
			var e = el[i];
			var func = g_to_func(e);
			var rect = find_child(e, ""rect"");
			if (func == null || rect == null)
				continue;

			var w = parseFloat(rect.attributes.width.value);
			if (w > maxwidth)
				maxwidth = w;

			if (func.match(re)) {
				var x = parseFloat(rect.attributes.x.value);
				orig_save(rect, ""fill"");
				rect.attributes.fill.value = ""rgb(230,0,230)"";

				if (matches[x] == undefined) {
					matches[x] = w;
				} else {
					if (w > matches[x]) {
						matches[x] = w;
					}
				}
				searching = 1;
			}
		}
		if (!searching)
			return;
		var params = get_params();
		params.s = currentSearchTerm;
		history.replaceState(null, null, parse_params(params));

		searchbtn.classList.add(""show"");
		searchbtn.firstChild.nodeValue = ""Reset Search"";

		var count = 0;
		var lastx = -1;
		var lastw = 0;
		var keys = Array();
		for (var k in matches) {
			if (matches.hasOwnProperty(k))
				keys.push(k);
		}
		keys.sort(function(a, b){ return a - b; });
		var fudge = 0.0001;
		for (var k in keys) {
			var x = parseFloat(keys[k]);
			var w = matches[keys[k]];
			if (x >= lastx + lastw - fudge) {
				count += w;
				lastx = x;
				lastw = w;
			}
		}
		matchedtxt.classList.remove(""hide"");
		var pct = 100 * count / maxwidth;
		if (pct != 100) pct = pct.toFixed(1)
		matchedtxt.firstChild.nodeValue = ""Matched: "" + pct + ""%"";
	}
]]>
</script>
";
    }
}

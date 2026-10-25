// ********************************************************************************************************************************************
//      CSpect MCP server - the Model Context Protocol layer: JSON-RPC 2.0 over HTTP POST (Streamable HTTP, JSON replies)
//      Written by:
//                  Mike Dailly
//
//      Released under the GNU 3 license - please see license file for more details
// ********************************************************************************************************************************************
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MCP
{
    // *********************************************************************************************************
    /// <summary>
    ///     A tool's arguments, with helpers
    /// </summary>
    // *********************************************************************************************************
    public class ToolArgs
    {
        readonly Dictionary<string, object> Values;
        public ToolArgs(Dictionary<string, object> _values) { Values = _values ?? new Dictionary<string, object>(); }

        public bool Has(string _name) { return Values.TryGetValue(_name, out object v) && v != null; }
        public object Raw(string _name) { Values.TryGetValue(_name, out object v); return v; }

        public string String(string _name, string _default = null)
        {
            if (!Values.TryGetValue(_name, out object v) || v == null) return _default;
            if (v is double) return ((double)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString();
        }

        public bool Bool(string _name, bool _default = false)
        {
            if (!Values.TryGetValue(_name, out object v) || v == null) return _default;
            if (v is bool) return (bool)v;
            string s = v.ToString().Trim().ToLowerInvariant();
            return s == "true" || s == "1" || s == "yes";
        }

        /// <summary>A plain number (not an expression)</summary>
        public int Int(string _name, int _default)
        {
            if (!Values.TryGetValue(_name, out object v) || v == null) return _default;
            if (v is double) return (int)(double)v;
            if (int.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r)) return r;
            throw new ArgumentException("'" + _name + "' should be a number");
        }

        public Dictionary<string, object> Object(string _name)
        {
            Values.TryGetValue(_name, out object v);
            return v as Dictionary<string, object>;
        }
    }

    // *********************************************************************************************************
    /// <summary>
    ///     A tool's answer: MCP content items
    /// </summary>
    // *********************************************************************************************************
    public class ToolResult
    {
        public readonly List<object> Content = new List<object>();
        public bool IsError;

        public static ToolResult Text(string _text)
        {
            ToolResult r = new ToolResult();
            r.Content.Add(new JObj { { "type", "text" }, { "text", _text } });
            return r;
        }

        public static ToolResult Error(string _text)
        {
            ToolResult r = Text(_text);
            r.IsError = true;
            return r;
        }

        public ToolResult AddImage(byte[] _png)
        {
            Content.Add(new JObj { { "type", "image" }, { "data", Convert.ToBase64String(_png) }, { "mimeType", "image/png" } });
            return this;
        }
    }

    // *********************************************************************************************************
    /// <summary>
    ///     A tool
    /// </summary>
    // *********************************************************************************************************
    public class Tool
    {
        public string Name;
        public string Description;
        public JObj Properties = new JObj();
        public List<string> Required = new List<string>();
        public bool ReadOnly;
        public Func<ToolArgs, ToolResult> Handler;

        public Tool Param(string _name, string _type, string _description, bool _required = false)
        {
            Properties[_name] = new JObj { { "type", _type }, { "description", _description } };
            if (_required) Required.Add(_name);
            return this;
        }

        public Tool EnumParam(string _name, string[] _values, string _description, bool _required = false)
        {
            Properties[_name] = new JObj { { "type", "string" }, { "enum", _values }, { "description", _description } };
            if (_required) Required.Add(_name);
            return this;
        }

        public JObj Describe()
        {
            JObj schema = new JObj { { "type", "object" }, { "properties", Properties } };
            if (Required.Count > 0) schema["required"] = Required;
            JObj t = new JObj { { "name", Name }, { "description", Description }, { "inputSchema", schema } };
            t["annotations"] = new JObj { { "readOnlyHint", ReadOnly } };
            return t;
        }
    }

    // *********************************************************************************************************
    /// <summary>
    ///     MCP request handling
    /// </summary>
    // *********************************************************************************************************
    public class McpServer
    {
        static readonly string[] ProtocolVersions = { "2025-06-18", "2025-03-26", "2024-11-05" };

        readonly MCPPlugin Plugin;
        readonly List<Tool> ToolList;
        readonly Dictionary<string, Tool> ToolsByName = new Dictionary<string, Tool>();

        const string Instructions =
            "CSpect is a ZX Spectrum Next emulator. These tools inspect and control the running machine. " +
            "Addresses and values accept numbers ($8000, 0x8000, 8000h, %1010, 32768), symbols from the loaded map file, " +
            "registers, and simple maths (label+4, hl+2*b). Memory addresses are the Z80's 64K view unless physical=true " +
            "(2MB Next RAM: bank8k*$2000+offset). step, step_over and step_back need the CPU stopped: use pause first. " +
            "run_to and wait_for_stop wait for the CPU to stop. screenshot returns the Spectrum display as a PNG; " +
            "press_keys and type_text press Spectrum keys while the CPU runs.";

        public McpServer(MCPPlugin _plugin)
        {
            Plugin = _plugin;
            ToolList = Tools.Build(_plugin);
            foreach (Tool t in ToolList) ToolsByName[t.Name] = t;
        }

        // ****************************************************************************************
        /// <summary>
        ///     One HTTP request (socket thread)
        /// </summary>
        // ****************************************************************************************
        public void Handle(HttpExchange _x)
        {
            // DNS rebinding: only answer pages served from this machine (no Origin = not a browser)
            if (_x.Headers.TryGetValue("Origin", out string origin) && !IsLocalOrigin(origin))
            {
                _x.Status = 403;
                _x.ContentType = "text/plain";
                _x.ResponseBody = "Forbidden origin";
                return;
            }

            if (_x.Path != "/mcp" && _x.Path != "/")
            {
                _x.Status = 404;
                _x.ContentType = "text/plain";
                _x.ResponseBody = "CSpect MCP server: POST JSON-RPC to /mcp";
                return;
            }

            if (_x.Method != "POST")
            {
                // no server-initiated stream (GET) or sessions (DELETE)
                _x.Status = 405;
                _x.ResponseHeaders["Allow"] = "POST";
                _x.ContentType = "text/plain";
                _x.ResponseBody = "";
                return;
            }

            object msg;
            try { msg = Json.Parse(_x.Body); }
            catch (Exception ex)
            {
                _x.Status = 400;
                _x.ResponseBody = Json.Write(ErrorReply(null, -32700, "Parse error: " + ex.Message));
                return;
            }

            if (msg is List<object>)
            {
                // batch (2025-03-26)
                List<object> replies = new List<object>();
                foreach (object m in (List<object>)msg)
                {
                    object r = HandleMessage(m as Dictionary<string, object>);
                    if (r != null) replies.Add(r);
                }
                if (replies.Count == 0) { _x.Status = 202; _x.ResponseBody = ""; return; }
                _x.ResponseBody = Json.Write(replies);
                return;
            }

            object reply = HandleMessage(msg as Dictionary<string, object>);
            if (reply == null) { _x.Status = 202; _x.ResponseBody = ""; return; }
            _x.ResponseBody = Json.Write(reply);
        }

        static bool IsLocalOrigin(string _origin)
        {
            if (string.IsNullOrEmpty(_origin) || _origin == "null") return false;
            try
            {
                Uri u = new Uri(_origin);
                return u.IsLoopback || u.Host == "localhost";
            }
            catch (Exception) { return false; }
        }

        static JObj ErrorReply(object _id, int _code, string _message)
        {
            return new JObj { { "jsonrpc", "2.0" }, { "id", _id }, { "error", new JObj { { "code", _code }, { "message", _message } } } };
        }

        static JObj Reply(object _id, object _result)
        {
            return new JObj { { "jsonrpc", "2.0" }, { "id", _id }, { "result", _result } };
        }

        // ****************************************************************************************
        /// <summary>
        ///     One JSON-RPC message: a reply, or null for notifications and responses
        /// </summary>
        // ****************************************************************************************
        object HandleMessage(Dictionary<string, object> _m)
        {
            if (_m == null) return ErrorReply(null, -32600, "Invalid request");
            _m.TryGetValue("id", out object id);
            bool isRequest = _m.ContainsKey("id");
            if (!_m.TryGetValue("method", out object mo) || !(mo is string))
            {
                // a response to something we sent (we never do), or junk
                return isRequest ? ErrorReply(id, -32600, "Invalid request") : null;
            }
            string method = (string)mo;
            _m.TryGetValue("params", out object po);
            Dictionary<string, object> p = po as Dictionary<string, object> ?? new Dictionary<string, object>();

            if (!isRequest) return null;            // notifications/initialized, notifications/cancelled...

            try
            {
                switch (method)
                {
                    case "initialize":
                        {
                            string want = p.TryGetValue("protocolVersion", out object v) ? v as string : null;
                            string version = Array.IndexOf(ProtocolVersions, want) >= 0 ? want : ProtocolVersions[0];
                            return Reply(id, new JObj {
                                { "protocolVersion", version },
                                { "capabilities", new JObj { { "tools", new JObj { { "listChanged", false } } } } },
                                { "serverInfo", new JObj { { "name", "cspect" }, { "title", "CSpect ZX Spectrum Next emulator" }, { "version", MCPPlugin.Version } } },
                                { "instructions", Instructions }
                            });
                        }
                    case "ping":
                        return Reply(id, new JObj());
                    case "tools/list":
                        {
                            List<object> tools = new List<object>();
                            foreach (Tool t in ToolList) tools.Add(t.Describe());
                            return Reply(id, new JObj { { "tools", tools } });
                        }
                    case "tools/call":
                        {
                            string name = p.TryGetValue("name", out object n) ? n as string : null;
                            if (name == null || !ToolsByName.TryGetValue(name, out Tool tool))
                                return ErrorReply(id, -32602, "Unknown tool: " + name);
                            p.TryGetValue("arguments", out object a);
                            ToolResult r;
                            try { r = tool.Handler(new ToolArgs(a as Dictionary<string, object>)); }
                            catch (Exception ex) { r = ToolResult.Error(ex.Message); }
                            return Reply(id, new JObj { { "content", r.Content }, { "isError", r.IsError } });
                        }
                    case "resources/list":
                        return Reply(id, new JObj { { "resources", new List<object>() } });
                    case "prompts/list":
                        return Reply(id, new JObj { { "prompts", new List<object>() } });
                    default:
                        return ErrorReply(id, -32601, "Method not found: " + method);
                }
            }
            catch (Exception ex)
            {
                return ErrorReply(id, -32603, ex.Message);
            }
        }
    }
}

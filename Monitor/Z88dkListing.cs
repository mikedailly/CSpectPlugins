// ********************************************************************************************************************************************
//      CSpect Monitor extension - C (and asm) source level debugging from z88dk listing files
//      Written by:
//                  Mike Dailly
//
//      Released under the GNU 3 license - please see license file for more details
//
//      Build with "zcc ... --list -m": each source file gets a <file>.lis listing, and the link a .map file.
//      Add -Cc-gcline so sccz80 marks every C statement (without it, only the start of each function is marked).
//
//      A listing line is "<line> <offset> <bytes> <source>" (offset relative to the module's part of the current
//      SECTION), and a line in column 0 ending in ':' names the file the line numbers count in - for C code that's
//      the C file, from sccz80's C_LINE directives ("bomber.c::draw_flames::0::93:"), so asm and C work the same.
//      Where each module's part of a section starts comes from the map: any label the listing defines, whose
//      address the map gives, places it.
// ********************************************************************************************************************************************
using Plugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Monitor
{
    // *********************************************************************************************************
    /// <summary>
    ///     Source lines and the physical addresses of their code, from a folder of z88dk .lis files and its .map
    /// </summary>
    // *********************************************************************************************************
    public class Z88dkListing
    {
        /// <summary>Source files (full paths) - file index = list index</summary>
        public readonly List<string> Files = new List<string>();
        readonly List<string[]> Texts = new List<string[]>();
        readonly Dictionary<string, int> FileIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Code of one source line, as a run of physical addresses (sorted by Start once loaded)</summary>
        struct Run { public int Start, Length, File, Line; }
        Run[] Runs = new Run[0];
        /// <summary>(file &lt;&lt; 20 | line) -> lowest physical address of that line's code</summary>
        readonly Dictionary<long, int> LineAddress = new Dictionary<long, int>();

        /// <summary>The folder and the files it was loaded from, to see when they change</summary>
        public string Folder { get; private set; }
        /// <summary>A line of what was loaded (or why not), for the command line</summary>
        public string Summary { get; private set; }
        public bool Loaded { get { return Runs.Length > 0; } }

        // "_draw_flames = $870E ; addr, public, , bomber_c, code_compiler, bomber.c::draw_flames::0::93:931"
        static readonly Regex MapLine = new Regex(@"^(\S+)\s*=\s*\$([0-9A-Fa-f]+)\s*;\s*addr\s*,\s*(?:local|public)\s*,[^,]*,\s*([^,]*?)\s*,\s*([^,]*?)\s*,", RegexOptions.Compiled);
        static readonly Regex LabelDef = new Regex(@"^\s*(?:\.([A-Za-z_]\w*)|([A-Za-z_]\w*):)", RegexOptions.Compiled);
        static readonly Regex SectionDir = new Regex(@"^\s*SECTION\s+(\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex ModuleDir = new Regex(@"^\s*MODULE\s+(\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex PageSection = new Regex(@"(PAGE|BANK)_(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>The 8K pages a NEX starts with in slots 2-7 (slots 0-1 are ROM): code outside PAGE_n / BANK_n sections runs there</summary>
        static readonly int[] DefaultPages = { -1, -1, 10, 11, 4, 5, 0, 1 };

        // *********************************************************************************************************
        /// <summary>
        ///     Load the .lis files in a folder, with the newest .map there. (Or a .map / .lis file: its folder.)
        /// </summary>
        /// <returns>The listing, or null if there's nothing usable (Summary of the failed one says why, via _why)</returns>
        // *********************************************************************************************************
        public static Z88dkListing Load(string _path, out string _why)
        {
            _why = null;
            string folder = _path;
            try
            {
                if (File.Exists(folder)) folder = Path.GetDirectoryName(Path.GetFullPath(folder));
                folder = Path.GetFullPath(folder);
                if (!Directory.Exists(folder)) { _why = "No folder " + _path; return null; }
                string[] lis = FilesOf(folder, ".lis");
                if (lis.Length == 0) { _why = "No .lis files in " + folder + " (build with zcc --list)"; return null; }
                string map = null;
                foreach (string m in FilesOf(folder, ".map"))
                    if (map == null || File.GetLastWriteTimeUtc(m) > File.GetLastWriteTimeUtc(map)) map = m;
                if (map == null) { _why = "No .map file in " + folder + " (build with zcc -m)"; return null; }

                var l = new Z88dkListing();
                l.Folder = folder;
                l.Build(map, lis);
                if (!l.Loaded) { _why = "No code found in the .lis files in " + folder + " (do they match " + Path.GetFileName(map) + "?)"; return null; }
                return l;
            }
            catch (Exception e)
            {
                _why = "Couldn't load the listings: " + e.Message;
                return null;
            }
        }

        /// <summary>Files with exactly this extension ("*.lis" alone also finds .list files on Windows)</summary>
        static string[] FilesOf(string _folder, string _ext)
        {
            var list = new List<string>();
            foreach (string f in Directory.GetFiles(_folder, "*" + _ext))
                if (string.Equals(Path.GetExtension(f), _ext, StringComparison.OrdinalIgnoreCase)) list.Add(f);
            return list.ToArray();
        }

        /// <summary>
        ///     The .lis and .map files in a folder, and when each was written: when this changes, the listings
        ///     need loading again ("" if the folder can't be read)
        /// </summary>
        public static string Signature(string _folder)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (string ext in new[] { ".lis", ".map" })
                {
                    string[] files = FilesOf(_folder, ext);
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (string f in files) sb.Append(f).Append('|').Append(File.GetLastWriteTimeUtc(f).Ticks).Append('\n');
                }
                return sb.ToString();
            }
            catch (Exception) { return ""; }
        }

        /// <summary>The folder's signature when it was loaded</summary>
        public string LoadedSignature { get; private set; }

        // *********************************************************************************************************
        //  Loading
        // *********************************************************************************************************
        /// <summary>A label's address and section, from the map</summary>
        struct MapSym { public int Address; public string Section; }

        /// <summary>A listing line's code, before its section's address is known</summary>
        struct Piece { public string Module, Section; public int Offset, Length, File, Line; }

        void Build(string _map, string[] _lis)
        {
            LoadedSignature = Signature(Folder);

            // labels by module: local labels (i_123) repeat in every module
            var syms = new Dictionary<string, MapSym>();
            foreach (string line in File.ReadLines(_map))
            {
                Match m = MapLine.Match(line);
                if (!m.Success) continue;
                syms[m.Groups[3].Value + "\n" + m.Groups[1].Value] = new MapSym { Address = Convert.ToInt32(m.Groups[2].Value, 16), Section = m.Groups[4].Value };
            }

            var runs = new List<Run>();
            int modules = 0;
            foreach (string lis in _lis)
            {
                var pieces = new List<Piece>();
                // module + "\n" + section -> how many labels put its start at each address
                var votes = new Dictionary<string, Dictionary<int, int>>();
                ReadListing(lis, syms, pieces, votes);

                // each module's part of each section starts where most of its labels say
                var bases = new Dictionary<string, int>();
                foreach (var v in votes)
                {
                    int best = 0, bestVotes = -1;
                    foreach (var c in v.Value) if (c.Value > bestVotes) { best = c.Key; bestVotes = c.Value; }
                    bases[v.Key] = best;
                }
                if (bases.Count > 0) modules++;
                foreach (Piece p in pieces)
                {
                    int b;
                    if (!bases.TryGetValue(p.Module + "\n" + p.Section, out b)) continue;
                    int phys = ToPhysical(b + p.Offset, p.Section);
                    if (phys < 0) continue;
                    runs.Add(new Run { Start = phys, Length = p.Length, File = p.File, Line = p.Line });
                }
            }

            // sort, and join a line's pieces that follow on
            runs.Sort((a, b) => a.Start.CompareTo(b.Start));
            var joined = new List<Run>(runs.Count);
            foreach (Run r in runs)
            {
                if (joined.Count > 0)
                {
                    Run last = joined[joined.Count - 1];
                    if (last.File == r.File && last.Line == r.Line && last.Start + last.Length == r.Start)
                    {
                        last.Length += r.Length;
                        joined[joined.Count - 1] = last;
                        continue;
                    }
                }
                joined.Add(r);
            }
            Runs = joined.ToArray();
            foreach (Run r in Runs)
            {
                long key = ((long)r.File << 20) | (uint)r.Line;
                int a;
                if (!LineAddress.TryGetValue(key, out a) || r.Start < a) LineAddress[key] = r.Start;
            }
            Summary = Files.Count + " source files, " + LineAddress.Count + " lines with code, from " + modules + " listings and " + Path.GetFileName(_map);
        }

        /// <summary>One .lis file: its code pieces, and the votes for where its sections start</summary>
        void ReadListing(string _lis, Dictionary<string, MapSym> _syms, List<Piece> _pieces, Dictionary<string, Dictionary<int, int>> _votes)
        {
            string dir = Path.GetDirectoryName(_lis);
            string module = Path.GetFileNameWithoutExtension(_lis), section = "";
            string fileName = null;
            int file = -1, srcLine = 0;
            var pending = new List<string>();          // labels waiting for their address (the next line with code)
            bool havePrev = false;
            Piece prev = new Piece();

            foreach (string raw in File.ReadLines(_lis))
            {
                string line = raw.TrimEnd('\r', '\n');
                if (line.Length == 0) continue;

                // "bomber.c:" or "bomber.c::draw_flames::0::93:" - the file the line numbers are in
                if (line[0] != ' ')
                {
                    if (line.EndsWith(":"))
                    {
                        string n = line.Substring(0, line.Length - 1);
                        int i = n.IndexOf("::", StringComparison.Ordinal);
                        if (i >= 0) n = n.Substring(0, i);
                        if (n != fileName) { fileName = n; file = -2; }        // index looked up when it has code
                    }
                    continue;
                }

                // more bytes of a long DEFB: "              aaaafffaaaaafffa"
                if (line.Length > 14 && line.StartsWith("              ") && line[14] != ' ' && IsHex(line.Trim()))
                {
                    if (havePrev)
                    {
                        int n = line.Trim().Length / 2;
                        prev = new Piece { Module = prev.Module, Section = prev.Section, Offset = prev.Offset + prev.Length, Length = n, File = prev.File, Line = prev.Line };
                        _pieces.Add(prev);
                    }
                    continue;
                }

                int num;
                if (line.Length >= 6 && int.TryParse(line.Substring(0, 6).Trim(), out num)) srcLine = num;
                string src = line.Length > 32 ? line.Substring(32) : "";
                string addr = line.Length >= 12 ? line.Substring(8, 4) : "";
                bool hasAddr = IsHex(addr);

                Match m = ModuleDir.Match(src);
                if (m.Success && !hasAddr) { module = m.Groups[1].Value; pending.Clear(); havePrev = false; continue; }
                m = SectionDir.Match(src);
                if (m.Success && !hasAddr) { section = m.Groups[1].Value; pending.Clear(); havePrev = false; continue; }
                m = LabelDef.Match(src);
                if (m.Success) pending.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
                if (!hasAddr) continue;

                int offset = Convert.ToInt32(addr, 16);
                foreach (string label in pending)
                {
                    MapSym s;
                    if (!_syms.TryGetValue(module + "\n" + label, out s) || s.Section != section) continue;
                    string key = module + "\n" + section;
                    Dictionary<int, int> v;
                    if (!_votes.TryGetValue(key, out v)) _votes[key] = v = new Dictionary<int, int>();
                    int c;
                    v.TryGetValue(s.Address - offset, out c);
                    v[s.Address - offset] = c + 1;
                }
                pending.Clear();

                string bytes = line.Length > 14 ? line.Substring(14, Math.Min(18, line.Length - 14)).Trim() : "";
                if (bytes.Length < 2 || !IsHex(bytes) || fileName == null) { havePrev = false; continue; }
                if (file == -2) file = AddFile(dir, fileName);
                prev = new Piece { Module = module, Section = section, Offset = offset, Length = bytes.Length / 2, File = file, Line = srcLine };
                havePrev = true;
                _pieces.Add(prev);
            }
        }

        static bool IsHex(string _s)
        {
            if (_s.Length == 0) return false;
            foreach (char c in _s) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            return true;
        }

        /// <summary>A section address -> physical: PAGE_n is 8K page n, BANK_n 16K bank n, anything else the NEX's default pages</summary>
        static int ToPhysical(int _address, string _section)
        {
            _address &= 0xffff;
            Match m = PageSection.Match(_section);
            if (m.Success)
            {
                int n = int.Parse(m.Groups[2].Value);
                if (m.Groups[1].Value.Equals("PAGE", StringComparison.OrdinalIgnoreCase)) return n * 0x2000 + (_address & 0x1fff);
                return n * 0x4000 + (_address & 0x3fff);
            }
            int page = DefaultPages[_address >> 13];
            return page < 0 ? -1 : page * 0x2000 + (_address & 0x1fff);
        }

        int AddFile(string _dir, string _name)
        {
            string path = _name;
            try
            {
                path = Path.GetFullPath(Path.IsPathRooted(_name) ? _name : Path.Combine(_dir, _name));
            }
            catch (Exception) { }
            int i;
            if (FileIndex.TryGetValue(path, out i)) return i;
            string[] text;
            try { text = File.ReadAllLines(path); }
            catch (Exception) { text = new[] { "; source file not found: " + path }; }
            i = Files.Count;
            Files.Add(path);
            Texts.Add(text);
            FileIndex[path] = i;
            return i;
        }

        // *********************************************************************************************************
        //  Lookups (as iCSpect's GetSourceText / GetSourceLocation / GetSourceAddress)
        // *********************************************************************************************************
        public string[] GetText(int _file) { return _file >= 0 && _file < Texts.Count ? Texts[_file] : null; }

        /// <summary>The source line whose code includes this physical address (Address = where that run of its code starts), or null</summary>
        public SourceLocation GetLocation(int _physical)
        {
            int lo = 0, hi = Runs.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                Run r = Runs[mid];
                if (_physical < r.Start) hi = mid - 1;
                else if (_physical >= r.Start + r.Length) lo = mid + 1;
                else return new SourceLocation { File = r.File, Line = r.Line, Address = r.Start };
            }
            return null;
        }

        /// <summary>Where a source line's code starts (physical), or -1 if it has none</summary>
        public int GetAddress(int _file, int _line)
        {
            int a;
            return LineAddress.TryGetValue(((long)_file << 20) | (uint)_line, out a) ? a : -1;
        }
    }
}

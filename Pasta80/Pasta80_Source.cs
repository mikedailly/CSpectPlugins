// ********************************************************************************************************************************************
//      Pasta/80 source level debugging: Pascal (and asm) source lines from the sjasmplus .lst file
//
//      Pasta/80 compiles to tetris.z80, writing each Pascal line into it as a comment - "; [n] text", n counting from 0 -
//      just before that line's code. sjasmplus lists tetris.z80 with addresses, so the code under each "; [n]" belongs
//      to that Pascal line. {$I file} switches the Pascal file (its lines start again at [0]), and when it ends the
//      line numbers carry on from the file that included it. Asm the listing includes ("# file opened: system.asm")
//      is listed with its own line numbers, so the run-time library's asm source works too.
//
//      Example lines:
//          # file opened: tetris.z80
//              1 0000              ; program C:/source/ZXSpectrum/Pasta80/Projects/Tetris/tetris.pas
//           7817 9898              ; [93]   {$I Utils.pas}          //  Common Utilities
//           7818 9898              ; [0] // *************************************************
//           7844 98A0              ; [20]   gGameState:=state;
//           7845 98A0 21 97 98                     ld      hl,global382 + 0
//           3523 9835 ~            ; [114]   S[0] := Char(I);              (~ = not assembled)
//             27 8004                              include "C:/utils/rtl/system.asm"
//          # file opened: system.asm
//              5+8004 00 00        __cur_file      dw      0
// ********************************************************************************************************************************************
using Plugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Pasta80Symbols
{
    public class Pasta80_Source
    {
        iCSpect CSpect;

        // "<line>[+ ]<address>[ ~]<12 chars of bytes><source>"
        static readonly Regex ListLine = new Regex(@"^\s*(\d+)([+ ])([0-9A-F]{4})([ ~])(.*)$", RegexOptions.Compiled);
        static readonly Regex PascalLine = new Regex(@"^\s*;\s\[(\d+)\]\s?(.*)$", RegexOptions.Compiled);
        static readonly Regex PascalInclude = new Regex(@"^\s*\{\$[iI]\s+([^}]+?)\s*\}", RegexOptions.Compiled);
        static readonly Regex ProgramLine = new Regex(@"^\s*;\s*program\s+(.+?)\s*$", RegexOptions.Compiled);
        static readonly Regex AsmInclude = new Regex(@"^\s*include\s+""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex HexByte = new Regex(@"^[0-9A-F]{2}$", RegexOptions.Compiled);

        /// <summary>The 8K pages a NEX starts with in each 8K slot (-1 = ROM): Pasta/80 code is ORGed into these</summary>
        static readonly int[] DefaultPages = { -1, -1, 10, 11, 4, 5, 0, 1 };

        public Pasta80_Source(iCSpect _CSpect) { CSpect = _CSpect; }

        /// <summary>
        ///     A source file the listing refers to. A Pascal file that starts without a {$I} naming it (the run-time
        ///     library's prelude, then the program itself) is found afterwards by its text: Echo is what the listing
        ///     quoted of it.
        /// </summary>
        class SrcFile
        {
            public string Path;
            public int Next;                                // Pascal: the line its next "[n]" should be
            public Dictionary<int, string> Echo;            // unnamed Pascal file: line -> text
            public int Index = -1;                          // CSpect's file index
        }

        /// <summary>A run of code: physical address, length, file, line (1 = first)</summary>
        struct Run { public int Start, Length, Line; public SrcFile File; }

        // *********************************************************************************************************
        /// <summary>
        ///     Read the .lst file's source lines and hand them to CSpect (iCSpect.AddSourceFile / AddSourceLine)
        /// </summary>
        /// <returns>A line saying what was found (or why not)</returns>
        // *********************************************************************************************************
        public string Load(string _lstPath, string[] _lines)
        {
            try { return LoadNow(_lstPath, _lines); }
            catch (MissingMethodException) { return "this CSpect can't take source lines from plugins (iCSpect.AddSourceFile) - symbols only"; }
        }

        readonly Dictionary<string, SrcFile> Files = new Dictionary<string, SrcFile>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Folders Pascal files are looked for in: the program's, then the run-time library's (where its asm comes from)</summary>
        readonly List<string> SearchDirs = new List<string>();

        SrcFile FileFor(string _path)
        {
            SrcFile f;
            if (!Files.TryGetValue(_path, out f)) Files[_path] = f = new SrcFile { Path = _path };
            return f;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        string LoadNow(string _lstPath, string[] _lines)
        {
            string lstDir = Path.GetDirectoryName(Path.GetFullPath(_lstPath));
            SearchDirs.Add(lstDir);
            // the run-time library's folder: where the listing's absolute asm includes come from
            foreach (string l in _lines)
            {
                Match lm = ListLine.Match(l);
                if (!lm.Success) continue;
                Match im = AsmInclude.Match(lm.Groups[5].Value.Length > 12 ? lm.Groups[5].Value.Substring(12) : "");
                if (im.Success && Path.IsPathRooted(im.Groups[1].Value))
                {
                    string d = Path.GetDirectoryName(im.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar));
                    if (!SearchDirs.Exists(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase))) SearchDirs.Add(d);
                }
            }

            var runs = new List<Run>();
            var unnamed = new List<SrcFile>();          // Pascal files to find by their text
            var asmStack = new List<SrcFile>();         // asm files open: [0] = the generated .z80
            var pasStack = new List<SrcFile>();
            string lastInclude = null;                  // the asm include just seen - the next "# file opened"
            string pendingPascal = null;                // a {$I file} just seen - its "[0]" comes next
            SrcFile pasFile = null;                     // the Pascal line code belongs to (null = none yet)
            int pasLine = 0;
            string mainPascal = null;
            Run prev = new Run();
            bool prevOpen = false;                      // prev is "..." bytes: its length is up to the next address

            foreach (string raw in _lines)
            {
                string line = raw.TrimEnd('\r', '\n');
                if (line.StartsWith("# file opened: "))
                {
                    string name = lastInclude ?? line.Substring(15).Trim();
                    asmStack.Add(FileFor(ResolveAsm(name, asmStack.Count > 0 ? asmStack[asmStack.Count - 1].Path : null, lstDir)));
                    lastInclude = null;
                    continue;
                }
                if (line.StartsWith("# file closed: ")) { if (asmStack.Count > 0) asmStack.RemoveAt(asmStack.Count - 1); continue; }

                Match m = ListLine.Match(line);
                if (!m.Success) continue;
                int number = int.Parse(m.Groups[1].Value);
                int address = Convert.ToInt32(m.Groups[3].Value, 16);
                bool skipped = m.Groups[4].Value == "~";
                string rest = m.Groups[5].Value;
                string field = rest.Length > 12 ? rest.Substring(0, 12) : rest;
                string src = rest.Length > 12 ? rest.Substring(12) : "";

                if (prevOpen)
                {
                    int phys = ToPhysical(address);
                    if (phys > prev.Start && phys - prev.Start < 0x10000) { prev.Length = phys - prev.Start; runs.Add(prev); }
                    prevOpen = false;
                }

                Match inc = AsmInclude.Match(src);
                if (inc.Success) lastInclude = inc.Groups[1].Value;
                bool top = asmStack.Count <= 1;

                if (top)
                {
                    Match pm = ProgramLine.Match(src);
                    if (pm.Success && mainPascal == null) { mainPascal = pm.Groups[1].Value; continue; }
                    pm = PascalLine.Match(src);
                    if (pm.Success)
                    {
                        int n = int.Parse(pm.Groups[1].Value);
                        string text = pm.Groups[2].Value;
                        SrcFile cur = pasStack.Count > 0 ? pasStack[pasStack.Count - 1] : null;
                        if (pendingPascal != null && n == 0)
                        {
                            // {$I file}: its lines come next
                            string dir = cur != null && cur.Path != null ? Path.GetDirectoryName(cur.Path) : lstDir;
                            pasStack.Add(FileFor(ResolvePascal(pendingPascal, dir)));
                        }
                        else if (cur != null && cur.Next != n)
                        {
                            // an included file has ended: back to the one whose next line this is...
                            int k = pasStack.Count - 1;
                            while (k >= 0 && pasStack[k].Next != n) k--;
                            if (k >= 0) pasStack.RemoveRange(k + 1, pasStack.Count - k - 1);
                            // ...or a new file nothing named (the program, after the run-time library's prelude)
                            else if (n == 0) pasStack.Clear();
                        }
                        pendingPascal = null;
                        if (pasStack.Count == 0)
                        {
                            SrcFile f0 = new SrcFile { Echo = new Dictionary<int, string>() };
                            unnamed.Add(f0);
                            pasStack.Add(f0);
                        }
                        SrcFile f = pasStack[pasStack.Count - 1];
                        f.Next = n + 1;
                        if (f.Echo != null && f.Echo.Count < 400) f.Echo[n + 1] = text;
                        pasFile = f;
                        pasLine = n + 1;
                        Match pi = PascalInclude.Match(text);
                        if (pi.Success) pendingPascal = pi.Groups[1].Value.Trim().Trim('\'');
                        continue;
                    }
                }
                if (skipped) continue;

                // the bytes: up to 4 hex pairs, or "..." for more than that
                int count = 0;
                bool more = false;
                foreach (string t in field.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (HexByte.IsMatch(t)) count++;
                    else { if (t.StartsWith("...")) more = true; break; }
                }
                if (count == 0) continue;

                SrcFile file;
                int srcLine;
                if (top && pasFile != null) { file = pasFile; srcLine = pasLine; }
                else if (asmStack.Count > 0) { file = asmStack[asmStack.Count - 1]; srcLine = number; }
                else continue;

                int start = ToPhysical(address);
                if (start < 0) continue;
                prev = new Run { Start = start, Length = count, File = file, Line = srcLine };
                if (more) prevOpen = true;
                else runs.Add(prev);
            }

            // the Pascal files nothing named: whichever file's text matches what the listing quoted
            foreach (SrcFile u in unnamed) u.Path = FindByText(u.Echo, mainPascal, lstDir);

            // hand them over, lowest address first, joining a line's runs that follow on
            runs.Sort((x, y) => x.Start.CompareTo(y.Start));
            int lines = 0;
            var seen = new HashSet<long>();
            Run join = new Run();
            foreach (Run r in runs)
            {
                if (join.File == r.File && join.Line == r.Line && join.Start + join.Length == r.Start) { join.Length += r.Length; continue; }
                if (join.File != null) lines += Give(join, seen);
                join = r;
            }
            if (join.File != null) lines += Give(join, seen);
            int used = 0;
            foreach (SrcFile f in Files.Values) if (f.Index >= 0) used++;
            foreach (SrcFile f in unnamed) if (f.Index >= 0) used++;
            return used + " source files, " + lines + " lines with code";
        }

        /// <summary>Give CSpect a run of code (adding its file first); 1 if it's a line not seen before</summary>
        int Give(Run _r, HashSet<long> _seen)
        {
            if (_r.File.Index < 0) _r.File.Index = CSpect.AddSourceFile(_r.File.Path);
            if (_r.File.Index < 0) return 0;
            CSpect.AddSourceLine(_r.File.Index, _r.Line, _r.Start, _r.Length);
            return _seen.Add(((long)_r.File.Index << 32) | (uint)_r.Line) ? 1 : 0;
        }

        /// <summary>
        ///     The Pascal file whose lines are the ones quoted: the program, or a .pas file in the search folders
        ///     (the most lines the same wins)
        /// </summary>
        string FindByText(Dictionary<int, string> _echo, string _main, string _lstDir)
        {
            var candidates = new List<string>();
            if (_main != null) candidates.Add(ResolvePascal(_main, _lstDir));
            foreach (string d in SearchDirs)
            {
                try { foreach (string f in Directory.GetFiles(d, "*.pas")) candidates.Add(Path.GetFullPath(f)); }
                catch (Exception) { }
            }
            string best = null;
            int bestScore = 0;
            var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string c in candidates)
            {
                if (!tried.Add(c)) continue;
                string[] text;
                try { text = File.ReadAllLines(c); } catch (Exception) { continue; }
                int score = 0;
                foreach (var e in _echo)
                    if (e.Key >= 1 && e.Key <= text.Length && Same(text[e.Key - 1], e.Value)) score++;
                if (score > bestScore) { best = c; bestScore = score; }
            }
            // at least half the quoted lines the same - else it's some other file (shown as not found)
            if (best != null && bestScore * 2 >= _echo.Count) return best;
            return Path.Combine(_lstDir, "unknown.pas");
        }

        /// <summary>Same text, give or take spacing (the listing may change tabs)</summary>
        static bool Same(string _a, string _b)
        {
            return Regex.Replace(_a ?? "", @"\s+", " ").Trim() == Regex.Replace(_b ?? "", @"\s+", " ").Trim();
        }

        /// <summary>An asm include: absolute, or next to the file including it, or the listing's folder</summary>
        static string ResolveAsm(string _name, string _includer, string _lstDir)
        {
            string n = _name.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(n)) return Path.GetFullPath(n);
            if (_includer != null)
            {
                string near = Path.Combine(Path.GetDirectoryName(_includer), n);
                if (File.Exists(near)) return Path.GetFullPath(near);
            }
            return Path.GetFullPath(Path.Combine(_lstDir, n));
        }

        /// <summary>A Pascal include: next to the file including it, else the program's folder, else the run-time library's</summary>
        string ResolvePascal(string _name, string _dir)
        {
            string n = _name.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(n)) return Path.GetFullPath(n);
            string near = Path.Combine(_dir, n);
            if (File.Exists(near)) return Path.GetFullPath(near);
            foreach (string d in SearchDirs)
            {
                string p = Path.Combine(d, n);
                if (File.Exists(p)) return Path.GetFullPath(p);
            }
            return Path.GetFullPath(near);
        }

        static int ToPhysical(int _address)
        {
            int page = DefaultPages[(_address >> 13) & 7];
            return page < 0 ? -1 : page * 0x2000 + (_address & 0x1fff);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Plugin;

namespace Profiler
{
    class WindowWrapper : IWin32Window
    {
        private IntPtr mWindowHandle;
        public IntPtr Handle { get { return mWindowHandle; } }
        public WindowWrapper(IntPtr _handle) { mWindowHandle = _handle; }
    }

    internal class ProfilerPlugin : iPlugin
    {
        iCSpect CSpect;
        public static bool Active;
        public static ProfilerForm form;
        byte[] CopperMemory = new byte[2048];
        bool[] CopperIsWritten = new bool[1024];

        bool OpenProfiler = false;

        // Sampling state
        public bool IsSampling { get; private set; }
        public bool FilterIdle { get; set; }
        public long TotalSamples { get { return totalSamples; } }

        Dictionary<string, long> pcSamples = new Dictionary<string, long>();
        long totalSamples = 0;
        Dictionary<string, long> lastResolvedStacks = null;

        // Idle address filter (populated from exclude list)
        HashSet<int> ignoredPages = new HashSet<int>();
        string[] excludeKeywords = new string[0];

        // Symbol cache
        List<KeyValuePair<int, string>> cachedSymbols = null;
        HashSet<int> validCodeAddrs = new HashSet<int>();
        const int MAX_STACK_DEPTH = 12;

        WindowWrapper hwndWrapper;

        public List<sIO> Init(iCSpect _CSpect)
        {
            Console.WriteLine("Profiler added (FlameGraph mode)");
            Console.WriteLine("  Ctrl+Alt+P = open profiler window");

            CSpect = _CSpect;
            IntPtr handle = (IntPtr)CSpect.GetGlobal(eGlobal.window_handle);
            hwndWrapper = new WindowWrapper(handle);
            FilterIdle = true;

            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO("<ctrl><alt>p", eAccess.KeyPress, 0));
            return ports;
        }

        public bool KeyPressed(int _id)
        {
            if (_id == 0)
            {
                OpenProfiler = true;
                return true;
            }
            return false;
        }

        public void SetExcludeFilter(string commaList)
        {
            ignoredPages.Clear();
            if (string.IsNullOrWhiteSpace(commaList))
            {
                excludeKeywords = new string[0];
                return;
            }

            excludeKeywords = commaList.Split(',')
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => s.Length > 0)
                .ToArray();

            // Rebuild ignored pages from symbols matching exclude keywords
            var symbols = GetSymbols();
            foreach (var sym in symbols)
            {
                string upper = sym.Value.ToUpperInvariant();
                foreach (string kw in excludeKeywords)
                {
                    if (upper.Contains(kw))
                    {
                        for (int p = -1; p <= 4; p++)
                            ignoredPages.Add((sym.Key >> 4) + p);
                        break;
                    }
                }
            }
            Console.WriteLine("Profiler: Excluding " + excludeKeywords.Length +
                " keywords, " + ignoredPages.Count + " address ranges");
        }

        public void StartSampling()
        {
            pcSamples.Clear();
            totalSamples = 0;
            lastResolvedStacks = null;
            IsSampling = true;
            Console.WriteLine("Profiler: SAMPLING STARTED" +
                (excludeKeywords.Length > 0 ? " (excluding: " + string.Join(", ", excludeKeywords) + ")" : ""));
        }

        public void StopSampling()
        {
            IsSampling = false;
            Console.WriteLine("Profiler: SAMPLING STOPPED (" + totalSamples + " samples)");
            ResolveAndDump();
        }

        public Dictionary<string, long> GetResolvedStacks()
        {
            return lastResolvedStacks;
        }

        public string LookupSymbol(int addr)
        {
            var symbols = GetSymbols();
            return LookupFunction(symbols, addr);
        }

        public void Quit() { }

        public byte Read(eAccess _type, int _address, int _id, out bool _isvalid)
        {
            _isvalid = false;
            return 0;
        }

        public void Reset() { }

        public void Tick()
        {
            if (!IsSampling) return;

            Z80Regs regs = CSpect.GetRegs();
            int pc = regs.PC;
            int sp = regs.SP;

            if (ignoredPages.Count > 0 && ignoredPages.Contains(pc >> 4))
                return;

            totalSamples++;

            var stack = new List<int>();
            stack.Add(pc);

            for (int depth = 0; depth < MAX_STACK_DEPTH; depth++)
            {
                int addr = sp + depth * 2;
                if (addr >= 0xFFFE) break;

                byte lo = CSpect.Peek((ushort)addr);
                byte hi = CSpect.Peek((ushort)(addr + 1));
                int retAddr = lo | (hi << 8);

                if (IsCodeAddress(retAddr))
                    stack.Add(retAddr);
                else if (retAddr >= 0x4000)
                    break;
            }

            stack.Reverse();
            string key = string.Join(";", stack);

            if (pcSamples.ContainsKey(key))
                pcSamples[key]++;
            else
                pcSamples[key] = 1;
        }

        public void OSTick()
        {
            if (OpenProfiler)
            {
                OpenProfiler = false;
                if (!Active)
                {
                    Active = true;
                    form = new ProfilerForm(CSpect, this);
                    form.Show();
                }
                else if (form != null)
                {
                    form.BringToFront();
                }
            }
        }

        List<KeyValuePair<int, string>> GetSymbols()
        {
            if (cachedSymbols != null) return cachedSymbols;

            string mapFilePath = "";
            try
            {
                object fn = CSpect.GetGlobal(eGlobal.file_name);
                if (fn is string && !string.IsNullOrEmpty((string)fn))
                    mapFilePath = Path.ChangeExtension((string)fn, ".map");
            }
            catch { }

            cachedSymbols = LoadMapFile(mapFilePath);
            return cachedSymbols;
        }

        bool IsCodeAddress(int addr)
        {
            if (addr < 0x4000 || addr > 0xFFFF) return false;
            var symbols = GetSymbols();
            if (symbols.Count == 0) return true;

            int lo = 0, hi = symbols.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (symbols[mid].Key <= addr) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }

            if (best < 0) return false;
            if (addr - symbols[best].Key > 256) return false;
            return validCodeAddrs.Contains(addr >> 8);
        }

        void ResolveAndDump()
        {
            var symbols = GetSymbols();
            var resolvedStacks = new Dictionary<string, long>();
            var flatCounts = new Dictionary<string, long>();

            foreach (var kv in pcSamples)
            {
                string[] parts = kv.Key.Split(';');
                var names = new List<string>();
                string prevName = "";
                foreach (string part in parts)
                {
                    int addr;
                    if (int.TryParse(part, out addr))
                    {
                        string name = LookupFunction(symbols, addr);
                        if (name != prevName)
                        {
                            names.Add(name);
                            prevName = name;
                        }
                    }
                }
                if (names.Count == 0) continue;

                string stackKey = string.Join(";", names);
                if (resolvedStacks.ContainsKey(stackKey))
                    resolvedStacks[stackKey] += kv.Value;
                else
                    resolvedStacks[stackKey] = kv.Value;

                string leaf = names[names.Count - 1];
                if (flatCounts.ContainsKey(leaf))
                    flatCounts[leaf] += kv.Value;
                else
                    flatCounts[leaf] = kv.Value;
            }

            lastResolvedStacks = resolvedStacks;

            // Dump to files
            try
            {
                string outPath = "profile.folded";
                using (var writer = new StreamWriter(outPath))
                {
                    foreach (var kv in resolvedStacks.OrderByDescending(x => x.Value))
                        writer.WriteLine(kv.Key + " " + kv.Value);
                }

                string summaryPath = "profile_summary.txt";
                using (var writer = new StreamWriter(summaryPath))
                {
                    writer.WriteLine("=== Profile ({0} samples, {1} unique stacks) ===",
                        totalSamples, resolvedStacks.Count);
                    writer.WriteLine("{0,-50} {1,12} {2,8}", "Function (leaf)", "Samples", "%");
                    writer.WriteLine(new string('-', 72));
                    foreach (var kv in flatCounts.OrderByDescending(x => x.Value))
                    {
                        double pct = totalSamples > 0 ? (kv.Value * 100.0 / totalSamples) : 0;
                        writer.WriteLine("{0,-50} {1,12} {2,7:F1}%", kv.Key, kv.Value, pct);
                    }
                }

                Console.WriteLine("Written: " + Path.GetFullPath(outPath));
                Console.WriteLine("Written: " + Path.GetFullPath(summaryPath));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Profiler dump error: " + ex.Message);
            }

            Console.WriteLine("Top 10:");
            int i = 0;
            foreach (var kv in flatCounts.OrderByDescending(x => x.Value))
            {
                double pct = totalSamples > 0 ? (kv.Value * 100.0 / totalSamples) : 0;
                Console.WriteLine("  {0,6:F1}%  {1}", pct, kv.Key);
                if (++i >= 10) break;
            }
        }

        // ---- Map file loading ----

        List<KeyValuePair<int, string>> LoadMapFile(string path)
        {
            var symbols = new List<KeyValuePair<int, string>>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Console.WriteLine("Map file not found: " + path);
                return symbols;
            }

            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0) return symbols;

            string firstLine = "";
            foreach (string l in lines)
            {
                if (!string.IsNullOrWhiteSpace(l)) { firstLine = l.Trim(); break; }
            }

            bool isSjasmplus = firstLine.Length > 9 && firstLine[8] == ' ' &&
                               !firstLine.Contains("=");

            if (isSjasmplus)
            {
                Console.WriteLine("Detected sjasmplus map format");
                return LoadMapSjasmplus(lines);
            }
            else
            {
                Console.WriteLine("Detected z88dk map format");
                return LoadMapZ88dk(lines);
            }
        }

        List<KeyValuePair<int, string>> LoadMapSjasmplus(string[] lines)
        {
            var symbols = new List<KeyValuePair<int, string>>();

            foreach (string line in lines)
            {
                try
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    string[] parts = trimmed.Split(new char[] { ' ', '\t' },
                        StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;

                    string typeStr = parts[2];
                    if (typeStr == "02") continue;

                    int addr;
                    if (!int.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber,
                        null, out addr))
                        continue;

                    string name = parts[3];
                    if (addr < 0x100) continue;

                    validCodeAddrs.Add(addr >> 8);
                    symbols.Add(new KeyValuePair<int, string>(addr, name));
                }
                catch { }
            }

            symbols.Sort((a, b) => a.Key.CompareTo(b.Key));
            Console.WriteLine("Loaded " + symbols.Count + " symbols (sjasmplus), " +
                ignoredPages.Count + " idle addresses filtered");
            return symbols;
        }

        List<KeyValuePair<int, string>> LoadMapZ88dk(string[] lines)
        {
            var symbols = new List<KeyValuePair<int, string>>();
            int codeSymbols = 0;

            foreach (string line in lines)
            {
                try
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    int eqIdx = trimmed.IndexOf('=');
                    if (eqIdx < 0) continue;

                    string name = trimmed.Substring(0, eqIdx).Trim();
                    string rest = trimmed.Substring(eqIdx + 1).Trim();

                    int semiIdx = rest.IndexOf(';');
                    string addrStr = (semiIdx >= 0 ? rest.Substring(0, semiIdx) : rest).Trim();
                    string metaStr = (semiIdx >= 0 ? rest.Substring(semiIdx + 1) : "").Trim();
                    addrStr = addrStr.TrimStart('$').Trim();

                    int addr;
                    if (!int.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber,
                        null, out addr))
                        continue;

                    if (metaStr.StartsWith("const")) continue;
                    if (name.StartsWith("__")) continue;

                    bool isCode = metaStr.Contains("code_compiler") ||
                                  metaStr.Contains("code_crt") ||
                                  metaStr.Contains("BANK_");

                    if (isCode)
                    {
                        codeSymbols++;
                        validCodeAddrs.Add(addr >> 8);
                    }

                    symbols.Add(new KeyValuePair<int, string>(addr, name));
                }
                catch { }
            }

            symbols.Sort((a, b) => a.Key.CompareTo(b.Key));
            Console.WriteLine("Loaded " + symbols.Count + " symbols (" + codeSymbols + " code, z88dk)");
            return symbols;
        }

        string LookupFunction(List<KeyValuePair<int, string>> symbols, int addr)
        {
            if (symbols.Count == 0)
                return string.Format("0x{0:X4}", addr);

            int lo = 0, hi = symbols.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (symbols[mid].Key <= addr) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }

            if (best >= 0 && addr - symbols[best].Key < 4096)
                return symbols[best].Value;
            return string.Format("0x{0:X4}", addr);
        }

        public bool Write(eAccess _type, int _port, int _id, byte _value)
        {
            return false;
        }
    }
}

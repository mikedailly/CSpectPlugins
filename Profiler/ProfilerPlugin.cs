using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

        // Symbol cache - keys are physical addresses in the Next's 2MB space
        List<KeyValuePair<int, string>> cachedSymbols = null;
        HashSet<int> validCodeAddrs = new HashSet<int>();
        // Physical addresses of function entry points. A value on the
        // stack that exactly matches one of these is NOT a return address —
        // real return addresses are inside a function body, not at its start.
        HashSet<int> publicSymbolAddrs = new HashSet<int>();

        // Match z88dk internal labels (not real function entries):
        //   l_funcname_NNNNN  - sccz80 generated internal labels (5+ digit suffix)
        //   loop_N or loop_N_M - generated loop labels (may have sub-index)
        //   ___str_N          - string literals
        //   ___label_N        - other generated labels
        // Static C functions (e.g. _hardware_init) are marked "local" in z88dk
        // maps but are still real function entry points — they don't match these.
        static readonly Regex InternalLabelRegex = new Regex(
            @"^(l_.+_\d{5,}|loop_\d+(_\d+)?|___str_\d+|___label_\d+)$",
            RegexOptions.Compiled);

        static bool IsInternalLabel(string name)
        {
            return InternalLabelRegex.IsMatch(name);
        }
        const int MAX_STACK_DEPTH = 12;
        const int MAX_STACK_SCAN = 64;  // max bytes to scan from SP
        const int MAX_CONSECUTIVE_SKIP = 6; // stop after this many non-return entries in a row

        // MMU state for Z80-to-physical address conversion
        byte[] mmuState = new byte[8];
        bool mmuStateRead = false;

        // Initial stack pointer from CRT (z88dk REGISTER_SP). 0 = unknown.
        int stackBase = 0;

        // z88dk deferred symbol conversion
        // Symbols loaded from z88dk map before MMU state is known.
        // Banked symbols (PAGE_XX) get physical addresses immediately.
        // Non-banked symbols store Z80 addresses and are converted once MMU is read.
        struct RawZ88dkSymbol
        {
            public int Addr;        // Z80 address from map
            public string Name;
            public int BankPage;    // 8KB page number, or -1 for non-banked
            public int BankOrg;     // Z80 ORG address for this bank
        }
        List<RawZ88dkSymbol> pendingZ88dkSymbols = null;

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
            // Uses physical addresses so Tick() can compare consistently
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
            GetSymbols(); // ensure symbols + validCodeAddrs are loaded before sampling
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

        public string LookupSymbol(int z80Addr)
        {
            ReadMmuState();
            var symbols = GetSymbols();
            return LookupFunction(symbols, ToPhysical(z80Addr));
        }

        public void Quit() { }

        public byte Read(eAccess _type, int _address, int _id, out bool _isvalid)
        {
            _isvalid = false;
            return 0;
        }

        public void Reset() { }

        // ---- MMU / address conversion ----

        void ReadMmuState()
        {
            for (int i = 0; i < 8; i++)
                mmuState[i] = CSpect.GetNextRegister((byte)(0x50 + i));

            // First time: finalize any pending z88dk symbol conversions
            if (!mmuStateRead)
            {
                mmuStateRead = true;
                if (pendingZ88dkSymbols != null)
                    FinalizeZ88dkSymbols();
            }
        }

        int ToPhysical(int z80Addr)
        {
            int slot = (z80Addr >> 13) & 7;
            return mmuState[slot] * 0x2000 + (z80Addr & 0x1FFF);
        }

        // ---- Sampling ----

        public void Tick()
        {
            if (!IsSampling) return;

            ReadMmuState();

            Z80Regs regs = CSpect.GetRegs();
            int pc = regs.PC;
            int sp = regs.SP;

            // If we're at the IM2 trampoline, an interrupt has just fired and
            // the CPU pushed the interrupted PC onto the stack. Use that as
            // the real PC for sampling — it's where the game was running.
            // The vector table lives at I*256 (257 bytes); the trampoline JP
            // is either inside it or at a fixed entry derived from a uniform
            // fill byte (z88dk pattern: table filled with 0x81 → entry $8181).
            bool unwoundFromIm2 = false;
            if (regs.IM == 2)
            {
                bool atTrampoline = false;
                int vecBase = regs.I << 8;
                if (pc >= vecBase && pc < vecBase + 259)
                {
                    atTrampoline = true;
                }
                else
                {
                    byte vecByte = CSpect.Peek((ushort)vecBase);
                    int im2Entry = vecByte | (vecByte << 8);
                    if (pc == im2Entry) atTrampoline = true;
                }

                if (atTrampoline)
                {
                    // Pop the interrupted PC from the stack
                    byte lo = CSpect.Peek((ushort)sp);
                    byte hi = CSpect.Peek((ushort)(sp + 1));
                    pc = lo | (hi << 8);
                    sp = (sp + 2) & 0xFFFF;
                    unwoundFromIm2 = true;
                }
            }

            // Skip idle samples — but only if we didn't unwind from an IM2 trampoline.
            // Interrupt-driven samples are exactly what we want, even if the
            // interrupted PC was at/near a HALT (the call stack still shows
            // which function path led there).
            if (FilterIdle && !unwoundFromIm2)
            {
                byte opcode = CSpect.Peek((ushort)pc);
                if (opcode == 0x76) return; // HALT — CPU is idle
                if (opcode == 0x18) // JR e
                {
                    sbyte offset = (sbyte)CSpect.Peek((ushort)(pc + 1));
                    if (offset == -2) return; // JR $ (self-loop)
                    int target = pc + 2 + offset;
                    if (CSpect.Peek((ushort)target) == 0x76) return; // JR to HALT
                }
                if (opcode == 0xC3) // JP nn
                {
                    int target = CSpect.Peek((ushort)(pc + 1)) | (CSpect.Peek((ushort)(pc + 2)) << 8);
                    if (target == pc) return; // JP $ (self-loop)
                    if (CSpect.Peek((ushort)target) == 0x76) return; // JP to HALT
                }
            }

            int physPc = ToPhysical(pc);
            if (ignoredPages.Count > 0 && ignoredPages.Contains(physPc >> 4))
                return;

            totalSamples++;

            var stack = new List<int>();
            stack.Add(physPc);

            // Call-chain consistency: each return address found on the stack
            // must point to a CALL instruction whose target is the function
            // containing the previous frame's address (or PC for frame 0).
            // This rejects coincidental CD-byte sequences in stack data that
            // would otherwise pass IsReturnAddress validation.
            var symbols = GetSymbols();
            int expectedTarget = LookupFunctionStart(symbols, physPc);

            int framesFound = 0;
            int consecutiveSkips = 0;
            int maxScan = MAX_STACK_SCAN / 2;
            int stackLimit = (stackBase > 0) ? stackBase : 0xFFFE;
            for (int depth = 0; depth < maxScan && framesFound < MAX_STACK_DEPTH; depth++)
            {
                int addr = sp + depth * 2;
                if (addr >= stackLimit) break;

                byte lo = CSpect.Peek((ushort)addr);
                byte hi = CSpect.Peek((ushort)(addr + 1));
                int retAddr = lo | (hi << 8);

                bool accepted = false;
                if (IsCallReturn(retAddr, expectedTarget))
                {
                    int physRetAddr = ToPhysical(retAddr);
                    // Only accept if the address is in a known code region.
                    // (Don't reject function-entry addresses — they're legit
                    // when one function immediately follows another's CALL,
                    // e.g. CRT's `call _main` followed by __Exit label.)
                    if (validCodeAddrs.Count == 0 || validCodeAddrs.Contains(physRetAddr >> 8))
                    {
                        stack.Add(physRetAddr);
                        framesFound++;
                        consecutiveSkips = 0;
                        accepted = true;

                        // For the next frame, the CALL target must be the
                        // function containing this return address.
                        int nextExpected = LookupFunctionStart(symbols, physRetAddr);
                        if (nextExpected >= 0) expectedTarget = nextExpected;
                    }
                }

                if (!accepted)
                {
                    consecutiveSkips++;
                    if (consecutiveSkips >= MAX_CONSECUTIVE_SKIP)
                        break; // likely past the valid stack
                }
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

        // ---- Symbol loading ----

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

        // ---- Stack walking validation ----

        // Check if `addr` looks like a return address from a CALL whose
        // target equals `expectedTarget` (physical address). If
        // expectedTarget is -1, accepts any valid public-function CALL target.
        //
        // Note: RST instructions are NOT accepted as return-address producers.
        // RST validation has no target operand, so the check would pass on any
        // byte matching the RST bit pattern (incl. $FF which is extremely
        // common in code). z88dk uses CALL almost exclusively anyway.
        bool IsCallReturn(int addr, int expectedTarget)
        {
            if (addr < 3 || addr > 0xFFFF) return false;
            // Note: addresses below $4000 used to be filtered as "ROM", but
            // banked code (e.g. z88dk BANK_22 with ORG $0000) lives there too.
            // The validCodeAddrs check on the physical address handles ROM filtering.

            // CALL nn = CD xx xx (3 bytes), conditional CALL cc,nn = C4/CC/D4/DC/E4/EC/F4 (3 bytes)
            byte opcode = CSpect.Peek((ushort)(addr - 3));
            bool isCall = opcode == 0xCD || opcode == 0xC4 || opcode == 0xCC ||
                          opcode == 0xD4 || opcode == 0xDC || opcode == 0xE4 ||
                          opcode == 0xEC || opcode == 0xF4;
            if (!isCall) return false;

            int target = CSpect.Peek((ushort)(addr - 2)) |
                         (CSpect.Peek((ushort)(addr - 1)) << 8);
            int physTarget = ToPhysical(target);

            if (expectedTarget >= 0)
            {
                // Strict: target must match the expected function start
                return physTarget == expectedTarget;
            }
            else
            {
                // Loose: target must be a known public function entry
                return publicSymbolAddrs.Count == 0 || publicSymbolAddrs.Contains(physTarget);
            }
        }

        // ---- Resolve and dump ----

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
                        // addr is already a physical address from Tick()
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

        // sjasmplus format: "Z80ADDR  PHYSADDR TYPE NAME"
        // Column 2 is the physical address in the Next's 2MB space.
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

                    int physAddr;
                    if (!int.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber,
                        null, out physAddr))
                        continue;

                    string name = parts[3];
                    if (physAddr < 0x100) continue;

                    validCodeAddrs.Add(physAddr >> 8);

                    // sjasmplus local labels use FUNCTION@LABEL syntax
                    // (e.g. UPDATEACTORMOVER@PASS2 is a label inside
                    // UPDATEACTORMOVER). Treat these as internal — they
                    // mark code regions but don't appear in the lookup table.
                    bool isFunction = name.IndexOf('@') < 0;
                    if (isFunction)
                    {
                        publicSymbolAddrs.Add(physAddr);
                        symbols.Add(new KeyValuePair<int, string>(physAddr, name));
                    }
                }
                catch { }
            }

            symbols.Sort((a, b) => a.Key.CompareTo(b.Key));
            Console.WriteLine("Loaded " + symbols.Count + " function symbols (sjasmplus, physical addresses)");
            return symbols;
        }

        // z88dk format: "NAME = $ADDR ; metadata"
        // Metadata contains section info like "code_compiler", "PAGE_44" (8KB), "BANK_21" (16KB).
        // Banked symbols get physical addresses immediately.
        // Non-banked symbols are deferred until MMU state is known.
        List<KeyValuePair<int, string>> LoadMapZ88dk(string[] lines)
        {
            var symbols = new List<KeyValuePair<int, string>>();
            var rawSymbols = new List<RawZ88dkSymbol>();
            var pageOrgs = new Dictionary<int, int>();   // 8KB pages
            var bankOrgs = new Dictionary<int, int>();   // 16KB banks
            int codeSymbols = 0;
            bool hasNonBanked = false;

            // First pass: collect CRT constants (bank/page ORGs, initial SP)
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                int eqIdx = trimmed.IndexOf('=');
                if (eqIdx < 0) continue;

                string name = trimmed.Substring(0, eqIdx).Trim();
                string rest = trimmed.Substring(eqIdx + 1).Trim();
                int semiIdx = rest.IndexOf(';');
                string addrStr = (semiIdx >= 0 ? rest.Substring(0, semiIdx) : rest).Trim().TrimStart('$');

                if (name == "REGISTER_SP")
                {
                    int spVal;
                    if (int.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber, null, out spVal))
                        stackBase = spVal;
                    continue;
                }

                string idStr = null;
                Dictionary<int, int> targetDict = null;
                if (name.StartsWith("CRT_ORG_PAGE_"))
                {
                    idStr = name.Substring("CRT_ORG_PAGE_".Length);
                    targetDict = pageOrgs;
                }
                else if (name.StartsWith("CRT_ORG_BANK_"))
                {
                    idStr = name.Substring("CRT_ORG_BANK_".Length);
                    targetDict = bankOrgs;
                }
                else continue;

                if (idStr.Contains("_")) continue; // skip _H, _L variants

                int idNum;
                if (!int.TryParse(idStr, out idNum)) continue;

                int orgAddr;
                if (int.TryParse(addrStr, System.Globalization.NumberStyles.HexNumber, null, out orgAddr))
                    targetDict[idNum] = orgAddr;
            }

            if (pageOrgs.Count > 0 || bankOrgs.Count > 0)
                Console.WriteLine("Profiler: Found " + pageOrgs.Count + " PAGE + " +
                    bankOrgs.Count + " BANK ORG definitions");
            if (stackBase > 0)
                Console.WriteLine("Profiler: Stack base (REGISTER_SP) = $" + stackBase.ToString("X4"));

            // Second pass: load symbols
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

                    // Parse metadata: "type, scope, , module, section, source"
                    string scope = "";
                    string section = "";
                    string[] metaParts = metaStr.Split(',');
                    if (metaParts.Length >= 2) scope = metaParts[1].Trim();
                    if (metaParts.Length >= 5) section = metaParts[4].Trim();

                    // Only include symbols from code sections.
                    // This excludes bss_*, data_*, BSS_* (data/BSS) which would
                    // pollute the symbol table and cause incorrect name resolution.
                    bool isCode = section.StartsWith("code_") ||
                                  section == "CODE" ||
                                  section.StartsWith("PAGE_") ||
                                  section.StartsWith("BANK_");
                    if (!isCode) continue;

                    // Internal labels (l_funcname_NNN, loop_X, ___str_X) are
                    // INSIDE a function. They mark code regions for validCodeAddrs
                    // but are NOT added to the lookup table.
                    // Static C functions are marked "local" in z88dk but are
                    // still real function entries — IsInternalLabel correctly
                    // identifies them as functions (not labels).
                    bool isFunction = !IsInternalLabel(name);

                    // Determine if this is a banked symbol and compute its physical address.
                    // PAGE_XX = 8KB page; physical = X * 0x2000 + (z80 - org)
                    // BANK_XX = 16KB bank (= 2 consecutive 8KB pages X*2 and X*2+1);
                    //           physical = X * 0x4000 + (z80 - org)
                    int physAddrBanked = -1;
                    if (section.StartsWith("PAGE_"))
                    {
                        string pageStr = section.Substring(5);
                        int pageNum;
                        if (int.TryParse(pageStr, out pageNum))
                        {
                            int org = pageOrgs.ContainsKey(pageNum) ? pageOrgs[pageNum] : (addr & 0xE000);
                            physAddrBanked = pageNum * 0x2000 + (addr - org);
                        }
                    }
                    else if (section.StartsWith("BANK_"))
                    {
                        string bankStr = section.Substring(5);
                        int bankNum;
                        if (int.TryParse(bankStr, out bankNum))
                        {
                            int org = bankOrgs.ContainsKey(bankNum) ? bankOrgs[bankNum] : (addr & 0xC000);
                            physAddrBanked = bankNum * 0x4000 + (addr - org);
                        }
                    }

                    if (isFunction) codeSymbols++;

                    if (physAddrBanked >= 0)
                    {
                        // Banked symbol: physical address known immediately
                        validCodeAddrs.Add(physAddrBanked >> 8);
                        if (isFunction)
                        {
                            publicSymbolAddrs.Add(physAddrBanked);
                            symbols.Add(new KeyValuePair<int, string>(physAddrBanked, name));
                        }
                    }
                    else
                    {
                        // Non-banked: need MMU state to convert to physical.
                        // Internal labels are still tracked so they can populate
                        // validCodeAddrs.
                        hasNonBanked = true;
                        rawSymbols.Add(new RawZ88dkSymbol
                        {
                            Addr = addr,
                            Name = isFunction ? name : null, // null = internal label
                            BankPage = -1,
                            BankOrg = 0
                        });
                    }
                }
                catch { }
            }

            if (hasNonBanked)
            {
                if (mmuStateRead)
                {
                    // MMU already known, convert now
                    foreach (var raw in rawSymbols)
                    {
                        int physAddr = ToPhysical(raw.Addr);
                        validCodeAddrs.Add(physAddr >> 8);
                        if (raw.Name != null)
                        {
                            publicSymbolAddrs.Add(physAddr);
                            symbols.Add(new KeyValuePair<int, string>(physAddr, raw.Name));
                        }
                    }
                }
                else
                {
                    // Defer conversion until MMU state is read
                    pendingZ88dkSymbols = rawSymbols;
                    // Store banked symbols so far; non-banked will be added later
                    cachedSymbols = symbols;
                }
            }

            symbols.Sort((a, b) => a.Key.CompareTo(b.Key));
            Console.WriteLine("Loaded " + symbols.Count + " symbols (" + codeSymbols +
                " code, z88dk, " + pageOrgs.Count + " PAGEs, " + bankOrgs.Count + " BANKs)");
            return symbols;
        }

        void FinalizeZ88dkSymbols()
        {
            if (pendingZ88dkSymbols == null) return;

            foreach (var raw in pendingZ88dkSymbols)
            {
                int physAddr = ToPhysical(raw.Addr);
                validCodeAddrs.Add(physAddr >> 8);
                if (raw.Name != null)
                {
                    publicSymbolAddrs.Add(physAddr);
                    cachedSymbols.Add(new KeyValuePair<int, string>(physAddr, raw.Name));
                }
            }
            cachedSymbols.Sort((a, b) => a.Key.CompareTo(b.Key));
            pendingZ88dkSymbols = null;
            Console.WriteLine("Profiler: Finalized " + cachedSymbols.Count +
                " public symbols with MMU state");
        }

        // ---- Symbol lookup ----

        string LookupFunction(List<KeyValuePair<int, string>> symbols, int physAddr)
        {
            if (symbols.Count == 0)
                return string.Format("0x{0:X4}", physAddr);

            int lo = 0, hi = symbols.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (symbols[mid].Key <= physAddr) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }

            // No distance limit — validCodeAddrs already filtered out non-code
            // addresses, so the nearest public symbol below is the right answer.
            // (With local labels excluded from the lookup table, public functions
            // can be many KB apart, e.g. a single function with lots of internal labels.)
            if (best >= 0)
                return symbols[best].Value;
            return string.Format("0x{0:X4}", physAddr);
        }

        // Returns the physical start address of the function containing physAddr,
        // or -1 if no function found.
        int LookupFunctionStart(List<KeyValuePair<int, string>> symbols, int physAddr)
        {
            if (symbols.Count == 0) return -1;
            int lo = 0, hi = symbols.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (symbols[mid].Key <= physAddr) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return best >= 0 ? symbols[best].Key : -1;
        }

        public bool Write(eAccess _type, int _port, int _id, byte _value)
        {
            return false;
        }
    }
}

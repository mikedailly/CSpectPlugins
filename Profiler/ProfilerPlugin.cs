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

        // MMU state for Z80-to-physical address conversion.
        // Initialized once on first use (e.g. first Tick or first Memory_EXE
        // hook), then updated incrementally via NextReg_Write hooks for
        // registers $50-$57.
        byte[] mmuState = new byte[8];
        bool mmuStateRead = false;

        // Set of MMU slot indices (0-7) that contain banked code. A slot is
        // "banked" if any PAGE_X or BANK_X section has its ORG within that
        // slot. For banked slots we convert Z80→physical using the current
        // MMU page, since multiple banks can share the same Z80 address. For
        // non-banked slots we use the Z80 address directly as the key — this
        // avoids depending on the NEX loader's page assignment, which can
        // place slot 4 and slot 5 on non-consecutive physical pages and
        // break the lookup for functions that straddle the slot boundary.
        HashSet<int> bankedSlots = new HashSet<int>();


        // Set of Z80 addresses where a function entry exists (deduplicated
        // across banks). Populated during map loading. Used in Init() to
        // register Memory_EXE hooks at every function entry, so we can
        // maintain an in-plugin shadow call stack.
        HashSet<ushort> functionEntryZ80Addrs = new HashSet<ushort>();

        // In-plugin shadow call stack. Each frame holds the physical
        // address of a function entry and the SP at the moment of entry.
        // Updated by Memory_EXE hooks (push) and on the next entry that
        // arrives with an SP >= a previous entry's SP (pop the previous).
        struct ShadowFrame
        {
            public int PhysAddr;
            public int Sp;
        }
        List<ShadowFrame> shadowStack = new List<ShadowFrame>();
        const int MAX_SHADOW_DEPTH = 64;

        // Foreground shadow stack snapshot saved during ISR execution. When an
        // IM2 interrupt fires, the foreground stack is set aside and a fresh
        // stack is built for the ISR's call chain. Once SP rises back to or
        // above `interruptEntrySp` (RETI/RET pops the IRQ-pushed return), the
        // foreground stack is restored. Without this, ISR samples either show
        // foreground frames as their parents, or stale ISR frames leak into
        // subsequent samples (the `MUSICAY1;PT3PLAYER;…` problem in Monty).
        List<ShadowFrame> savedShadowStack = null;
        int interruptEntrySp = -1;

        // Cached set of every 2-byte word found in the IM2 vector table at
        // I*256. Any function whose entry address matches one of these is a
        // candidate ISR handler. Refreshed lazily when `regs.I` changes.
        // Covers both:
        //   - uniform-fill tables (z88dk pattern: byte X repeated, JP at $XXXX)
        //   - explicit pointer tables (each 2-byte entry is a handler address)
        HashSet<int> im2HandlerTargets = null;
        int cachedIm2I = -1;

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

            // Register NextReg_Write hooks for the 8 MMU slot registers
            // ($50-$57). We mirror the MMU state in `mmuState` so we can
            // convert Z80 addresses to physical without polling 8 registers
            // every Tick().
            for (byte reg = 0x50; reg <= 0x57; reg++)
                ports.Add(new sIO(reg, eAccess.NextReg_Write));

            // Load symbols early. This populates `functionEntryZ80Addrs`
            // (the Z80 addresses we want to hook for shadow-call-stack
            // tracking).
            GetSymbols();

            // Register a Memory_EXE hook at every Z80 address where a
            // function entry lives. The hook fires when execution reaches
            // that address; we use it to push a frame onto the shadow stack.
            // Many addresses may resolve to multiple banked symbols — that's
            // fine, the actual physical address is determined at hook-fire
            // time using the current MMU state.
            int hookCount = 0;
            foreach (ushort z80Addr in functionEntryZ80Addrs)
            {
                ports.Add(new sIO(z80Addr, eAccess.Memory_EXE));
                hookCount++;
            }

            // Always hook $0038 (IM1 vector) and $0066 (NMI vector) so we can
            // detect interrupt entries even when the handler isn't a named
            // function in the map. Without this, sjasmplus programs that use
            // an unnamed IM1 stub at $0038 to dispatch to e.g. PT3PLAYER look
            // identical to a normal foreground call into PT3PLAYER, and the
            // ISR's frames leak into the foreground shadow stack.
            if (functionEntryZ80Addrs.Add((ushort)0x0038))
            {
                ports.Add(new sIO((ushort)0x0038, eAccess.Memory_EXE));
                hookCount++;
            }
            if (functionEntryZ80Addrs.Add((ushort)0x0066))
            {
                ports.Add(new sIO((ushort)0x0066, eAccess.Memory_EXE));
                hookCount++;
            }
            Console.WriteLine("Profiler: Registered " + hookCount +
                " function-entry execution hooks");

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
            // Symbols and shadow stack are already running from Init() —
            // we just clear the per-sampling-session counters here. The
            // shadow stack is intentionally NOT cleared: the foreground
            // frames already in it are the program's currently-active
            // parents (e.g. `__Restart_2;_main;_Game_Update50Hz;_NES_RunMatch`),
            // and clearing would lose them since the main loop never returns
            // far enough to repush them. Stale ISR frames are kept out by
            // the IFF1-gated restore + strict CALL/RST verification in the
            // Memory_EXE hook.
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

            if (_type == eAccess.Memory_EXE)
            {
                // Execution has reached a registered function entry. The CALL
                // that brought us here just pushed the return address; sp now
                // points to it. We maintain a shadow call stack using SP as
                // the "frame depth" indicator: a function called from a
                // parent has a STRICTLY LOWER sp than the parent's entry sp,
                // because the CALL pushed at least 2 bytes.
                if (!mmuStateRead)
                {
                    for (int i = 0; i < 8; i++)
                        mmuState[i] = CSpect.GetNextRegister((byte)(0x50 + i));
                    mmuStateRead = true;
                    if (pendingZ88dkSymbols != null)
                        FinalizeZ88dkSymbols();
                }

                int physAddr = ToPhysical(_address);
                Z80Regs regs = CSpect.GetRegs();
                int sp = regs.SP;

                // If a previous ISR has finished, restore the foreground stack
                // before processing this new entry. The reliable signal is
                // IFF1 — the CPU clears it on interrupt accept and the
                // standard ISR exit sequence is `EI; RETI`, which sets it
                // back. Using SP alone is unsafe because some ISRs (e.g.
                // Monty's INTERRUPT handler) switch to a private stack at
                // a much higher address, which would otherwise look like the
                // IRQ-pushed return had been popped.
                if (savedShadowStack != null && regs.IFF1 && sp >= interruptEntrySp)
                {
                    shadowStack = savedShadowStack;
                    savedShadowStack = null;
                    interruptEntrySp = -1;
                }

                // Detect interrupt entry: this hook fire is the ISR handler's
                // first instruction. Stash the foreground stack and start
                // fresh so the ISR's call chain doesn't contaminate it.
                if (savedShadowStack == null && IsInterruptHandlerEntry(_address, regs, sp))
                {
                    savedShadowStack = shadowStack;
                    interruptEntrySp = sp;
                    shadowStack = new List<ShadowFrame>();
                }

                // Pop 1: SP-based — frames whose recorded SP is at or below
                // the current SP have already returned (because the new
                // call's sp is "deeper" than them, or equal in tail-call case).
                while (shadowStack.Count > 0 &&
                       shadowStack[shadowStack.Count - 1].Sp <= sp)
                {
                    shadowStack.RemoveAt(shadowStack.Count - 1);
                }

                // Pop 2: identity-based — if this exact function is already
                // somewhere in the shadow stack, pop everything down to and
                // including the old occurrence. This catches the case where
                // SP-based reconciliation fails (function called multiple
                // times from the same parent at different stack depths, e.g.
                // because the parent pushed locals between calls). Treats
                // direct recursion as "previous call returned" — acceptable
                // trade-off for the much more common stale-frame case.
                for (int i = shadowStack.Count - 1; i >= 0; i--)
                {
                    if (shadowStack[i].PhysAddr == physAddr)
                    {
                        while (shadowStack.Count > i)
                            shadowStack.RemoveAt(shadowStack.Count - 1);
                        break;
                    }
                }

                // Push the current function. Skip the bare interrupt vector
                // addresses ($0038, $0066) when they're not real symbols —
                // we hook them only for interrupt-entry detection, not as
                // call-chain frames; pushing would inject a noisy hex/nearby
                // symbol into every ISR chain.
                bool isVectorOnly = (_address == 0x0038 || _address == 0x0066) &&
                                    !publicSymbolAddrs.Contains(physAddr);
                if (!isVectorOnly && shadowStack.Count < MAX_SHADOW_DEPTH)
                {
                    shadowStack.Add(new ShadowFrame
                    {
                        PhysAddr = physAddr,
                        Sp = sp
                    });
                }
            }

            return 0;
        }

        public void Reset()
        {
            // On machine reset, the call stack is gone. Clear our shadow.
            shadowStack.Clear();
            savedShadowStack = null;
            interruptEntrySp = -1;
            im2HandlerTargets = null;
            cachedIm2I = -1;
        }

        // True when entering `z80Addr` is an interrupt handler's first
        // instruction. Detection covers:
        //   IM2 uniform-fill: at I*256+vecByte the code is `JP target`
        //   IM2 explicit:     I*256 holds a table of direct handler pointers
        //   IM1 / IM0:        z80Addr == 0x0038 (RST 38h vector)
        // To distinguish a real interrupt from a foreground CALL or RST to
        // the same function, we also verify the return address on stack is
        // NOT immediately preceded by a CALL or RST opcode — interrupts
        // push the interrupted PC, which is rarely positioned that way.
        bool IsInterruptHandlerEntry(int z80Addr, Z80Regs regs, int sp)
        {
            bool candidate = false;

            if (regs.IM == 2)
            {
                EnsureIm2TableCache(regs);
                if (im2HandlerTargets != null && im2HandlerTargets.Contains(z80Addr))
                    candidate = true;
            }
            else if (z80Addr == 0x0038)
            {
                // IM0/IM1 default vector. Foreground RST 38h is rare but
                // possible — the CALL/RST verification below filters it out.
                candidate = true;
            }

            if (!candidate) return false;

            // Verify: the return address on stack must NOT lie immediately
            // after a CALL or RST instruction whose target is `z80Addr`. An
            // interrupt pushes the interrupted PC; the bytes preceding it are
            // arbitrary, so the loose "any CALL opcode" filter has a 3-4%
            // false-negative rate when those bytes happen to encode a CALL by
            // coincidence — enough to lose most ISR detections when interrupts
            // tend to fire at similar foreground PCs. The strict check (does
            // the would-be CALL actually target THIS function?) eliminates
            // coincidental matches while still catching genuine foreground
            // calls to functions that are also reachable as ISRs.
            int retAddr = CSpect.Peek((ushort)sp) |
                          (CSpect.Peek((ushort)(sp + 1)) << 8);
            byte preCall = CSpect.Peek((ushort)(retAddr - 3));
            // 3-byte CALL opcodes: unconditional 0xCD, conditional 0xC4/CC/D4/DC/E4/EC/F4/FC
            if (preCall == 0xCD || preCall == 0xC4 || preCall == 0xCC ||
                preCall == 0xD4 || preCall == 0xDC || preCall == 0xE4 ||
                preCall == 0xEC || preCall == 0xF4 || preCall == 0xFC)
            {
                int callTarget = CSpect.Peek((ushort)(retAddr - 2)) |
                                 (CSpect.Peek((ushort)(retAddr - 1)) << 8);
                if (callTarget == z80Addr) return false;
            }
            // 1-byte RST opcodes: pattern 11xxx111. Target is the embedded vector
            // (RST 0/8/10/18/20/28/30/38 → 0x00/0x08/0x10/0x18/0x20/0x28/0x30/0x38).
            byte preRst = CSpect.Peek((ushort)(retAddr - 1));
            if ((preRst & 0xC7) == 0xC7)
            {
                int rstTarget = preRst & 0x38;
                if (rstTarget == z80Addr) return false;
            }

            return true;
        }

        // Build (or refresh) the set of candidate IM2 handler addresses by
        // scanning the 257-byte vector table at I*256 for every 2-byte word.
        // Both uniform-fill tables (one repeated byte → one repeated target)
        // and explicit pointer tables (256 distinct pointers) are covered by
        // a single pass. We also follow a `JP nn` at the uniform-fill landing
        // address so that the actual handler is in the set, not the trampoline.
        void EnsureIm2TableCache(Z80Regs regs)
        {
            if (regs.IM != 2) { im2HandlerTargets = null; cachedIm2I = -1; return; }
            if (im2HandlerTargets != null && cachedIm2I == regs.I) return;

            var targets = new HashSet<int>();
            int vecBase = regs.I << 8;

            for (int offset = 0; offset < 256; offset++)
            {
                int word = CSpect.Peek((ushort)(vecBase + offset)) |
                           (CSpect.Peek((ushort)(vecBase + offset + 1)) << 8);
                targets.Add(word);
                // If the landing address contains a JP, also include the JP target.
                if (CSpect.Peek((ushort)word) == 0xC3)
                {
                    int jpTarget = CSpect.Peek((ushort)(word + 1)) |
                                   (CSpect.Peek((ushort)(word + 2)) << 8);
                    targets.Add(jpTarget);
                }
            }

            im2HandlerTargets = targets;
            cachedIm2I = regs.I;
        }

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

        // Convert a Z80 address to a "lookup key":
        //   - Banked slot: page * 0x2000 + offset (the actual physical address)
        //   - Non-banked slot: the Z80 address itself
        // Storage in cachedSymbols / publicSymbolAddrs uses the same encoding,
        // so the lookup is consistent. Non-banked code doesn't depend on the
        // current MMU state, which is important because slot 4 and slot 5
        // may not be on consecutive physical pages.
        int ToPhysical(int z80Addr)
        {
            int slot = (z80Addr >> 13) & 7;
            if (bankedSlots.Contains(slot))
                return mmuState[slot] * 0x2000 + (z80Addr & 0x1FFF);
            return z80Addr;
        }

        // ---- Sampling ----

        public void Tick()
        {
            if (!IsSampling) return;

            // First-time MMU read: shadow may be unset if no NextReg writes
            // have happened yet between Init() and the first Tick.
            if (!mmuStateRead) ReadMmuState();

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

            // Restore the foreground shadow stack if a prior ISR has finished.
            // IFF1=1 means the ISR's `EI; RETI` has run; using SP alone is
            // unsafe when an ISR switches to its own stack (which would push
            // SP higher than `interruptEntrySp` mid-ISR and falsely look like
            // a return).
            if (savedShadowStack != null && regs.IFF1 && sp >= interruptEntrySp)
            {
                shadowStack = savedShadowStack;
                savedShadowStack = null;
                interruptEntrySp = -1;
            }

            // Reconcile the shadow stack against the live SP. Any frames
            // recorded with SP < current SP have already returned (we don't
            // hook RETs). Pop them now.
            while (shadowStack.Count > 0 &&
                   shadowStack[shadowStack.Count - 1].Sp < sp)
            {
                shadowStack.RemoveAt(shadowStack.Count - 1);
            }

            totalSamples++;

            // Build the sample frame chain: shadow stack frames from oldest
            // to newest, then the current PC as the leaf.
            var stack = new List<int>();
            for (int i = 0; i < shadowStack.Count; i++)
                stack.Add(shadowStack[i].PhysAddr);
            stack.Add(physPc);

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

            // Live sample-count update on the status strip while sampling.
            // OSTick runs on the OS thread, so it's safe to touch UI here.
            if (Active && form != null && IsSampling)
                form.UpdateSampleCount(totalSamples);
        }

        // ---- Symbol loading ----

        List<KeyValuePair<int, string>> GetSymbols()
        {
            if (cachedSymbols != null) return cachedSymbols;

            // Use CSpect's own LoadFile() to find the map file. CSpect knows
            // where the .nex was loaded from (SD card or MMC root) and will
            // resolve the path the same way. We just give it the same name
            // with the extension swapped to .map.
            string fileName = "";
            try
            {
                object fn = CSpect.GetGlobal(eGlobal.file_name);
                if (fn is string) fileName = (string)fn;
            }
            catch { }

            cachedSymbols = new List<KeyValuePair<int, string>>();
            if (string.IsNullOrEmpty(fileName))
            {
                Console.WriteLine("Profiler: No file_name from CSpect — no symbols loaded");
                return cachedSymbols;
            }

            // Try the map next to the .nex via CSpect's loader
            string mapName = Path.ChangeExtension(fileName, ".map");
            byte[] mapBytes = null;
            try { mapBytes = CSpect.LoadFile(mapName); } catch { }

            // Some CSpect versions strip leading "./" — try the bare name too
            if (mapBytes == null)
            {
                string trimmed = mapName.TrimStart('.', '\\', '/');
                if (trimmed != mapName)
                {
                    try { mapBytes = CSpect.LoadFile(trimmed); } catch { }
                }
            }

            if (mapBytes == null)
            {
                Console.WriteLine("Profiler: Map file not found via CSpect LoadFile: " + mapName);
                return cachedSymbols;
            }

            Console.WriteLine("Profiler: Loaded map file via CSpect (" + mapBytes.Length + " bytes): " + mapName);
            string[] lines = System.Text.Encoding.ASCII.GetString(mapBytes)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            cachedSymbols = LoadMapLines(lines);
            return cachedSymbols;
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

                // Generate the interactive flame graph SVG from the same data
                string svgPath = "flamegraph.svg";
                FlameGraph.Write(resolvedStacks, svgPath,
                    "CSpect Profile (" + totalSamples + " samples)");

                Console.WriteLine("Written: " + Path.GetFullPath(outPath));
                Console.WriteLine("Written: " + Path.GetFullPath(summaryPath));
                Console.WriteLine("Written: " + Path.GetFullPath(svgPath));
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

        // Detects the map format (sjasmplus vs z88dk) from the first line and
        // dispatches to the appropriate loader. Used by both the disk-based
        // LoadMapFile and the CSpect-LoadFile-based path in GetSymbols.
        List<KeyValuePair<int, string>> LoadMapLines(string[] lines)
        {
            var symbols = new List<KeyValuePair<int, string>>();
            if (lines == null || lines.Length == 0) return symbols;

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

        List<KeyValuePair<int, string>> LoadMapFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Console.WriteLine("Profiler: Map file not found: " + path);
                return new List<KeyValuePair<int, string>>();
            }
            Console.WriteLine("Profiler: Loading map file: " + path);
            return LoadMapLines(File.ReadAllLines(path));
        }

        // sjasmplus format: "Z80ADDR  PHYSADDR TYPE NAME"
        // Column 1 is the Z80 address; column 2 is the physical address
        // in the Next's 2MB space.
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

                    // sjasmplus type codes:
                    //   00 = code label / function
                    //   01 = I/O port or data (e.g. MEMORY_PAGING_CONTROL_PORT = $7FFD)
                    //   02 = constant
                    // Only type 00 is real executable code.
                    string typeStr = parts[2];
                    if (typeStr != "00") continue;

                    int z80Addr;
                    if (!int.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber,
                        null, out z80Addr))
                        continue;

                    int physAddr;
                    if (!int.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber,
                        null, out physAddr))
                        continue;

                    string name = parts[3];
                    if (physAddr < 0x100) continue;

                    validCodeAddrs.Add(physAddr >> 8);

                    // sjasmplus stores physical addresses for all symbols, so
                    // EVERY slot must use MMU translation at lookup time. Mark
                    // the symbol's slot as banked.
                    bankedSlots.Add((z80Addr >> 13) & 7);

                    // sjasmplus local labels use FUNCTION@LABEL syntax
                    // (e.g. UPDATEACTORMOVER@PASS2 is a label inside
                    // UPDATEACTORMOVER). Treat these as internal — they
                    // mark code regions but don't appear in the lookup table.
                    bool isFunction = name.IndexOf('@') < 0;
                    if (isFunction)
                    {
                        publicSymbolAddrs.Add(physAddr);
                        symbols.Add(new KeyValuePair<int, string>(physAddr, name));
                        if (z80Addr >= 0 && z80Addr <= 0xFFFF)
                            functionEntryZ80Addrs.Add((ushort)z80Addr);
                    }
                }
                catch { }
            }

            symbols.Sort((a, b) => a.Key.CompareTo(b.Key));
            Console.WriteLine("Loaded " + symbols.Count + " function symbols (sjasmplus, physical addresses)");
            if (bankedSlots.Count > 0)
            {
                var sorted = new List<int>(bankedSlots);
                sorted.Sort();
                Console.WriteLine("Profiler: Banked MMU slots: " + string.Join(",", sorted));
            }
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

            // bankedSlots is populated lazily in the second pass below, only
            // for sections that ACTUALLY contain symbols. We do NOT add slots
            // based on CRT_ORG_PAGE_X / CRT_ORG_BANK_X constants alone — z88dk
            // defines defaults for every possible page (0-223), and adding all
            // of them would mark code_compiler slots as banked and break lookup.

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
                    // Mark the slot(s) this symbol occupies as banked, so
                    // ToPhysical() knows to use MMU translation for them.
                    int physAddrBanked = -1;
                    if (section.StartsWith("PAGE_"))
                    {
                        string pageStr = section.Substring(5);
                        int pageNum;
                        if (int.TryParse(pageStr, out pageNum))
                        {
                            int org = pageOrgs.ContainsKey(pageNum) ? pageOrgs[pageNum] : (addr & 0xE000);
                            physAddrBanked = pageNum * 0x2000 + (addr - org);
                            bankedSlots.Add((addr >> 13) & 7);
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
                            bankedSlots.Add((addr >> 13) & 7);
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
                            if (addr >= 0 && addr <= 0xFFFF)
                                functionEntryZ80Addrs.Add((ushort)addr);
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
                        if (isFunction && addr >= 0 && addr <= 0xFFFF)
                            functionEntryZ80Addrs.Add((ushort)addr);
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
            Console.WriteLine("Loaded " + symbols.Count + " z88dk symbols (" + codeSymbols +
                " code; " + pageOrgs.Count + " PAGE defs / " + bankOrgs.Count + " BANK defs)");
            if (bankedSlots.Count > 0)
            {
                var sorted = new List<int>(bankedSlots);
                sorted.Sort();
                Console.WriteLine("Profiler: Banked MMU slots actually used: " +
                    string.Join(",", sorted));
            }
            else
            {
                Console.WriteLine("Profiler: No banked code (all symbols use Z80 addresses)");
            }
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

        public bool Write(eAccess _type, int _port, int _id, byte _value)
        {
            if (_type == eAccess.NextReg_Write && _port >= 0x50 && _port <= 0x57)
            {
                // Mirror the MMU register write into our shadow state. We
                // don't claim the write — we let CSpect process it normally.
                if (!mmuStateRead)
                {
                    // First-time fill: read the other 7 slots so the
                    // shadow is fully populated, then mark as read.
                    for (int i = 0; i < 8; i++)
                        mmuState[i] = CSpect.GetNextRegister((byte)(0x50 + i));
                    mmuStateRead = true;
                    if (pendingZ88dkSymbols != null)
                        FinalizeZ88dkSymbols();
                }
                mmuState[_port - 0x50] = _value;
            }
            return false;
        }
    }
}

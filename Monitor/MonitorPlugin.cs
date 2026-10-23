// ********************************************************************************************************************************************
//      CSpect Monitor extension - a debugger front end: registers, disassembly, stack and memory in one window
//      Written by:
//                  Mike Dailly
//
//      Released under the GNU 3 license - please see license file for more details
//
//      <ctrl><alt>m opens the monitor in its own OS window (iCSpect.OpenWindow) drawn with Dear ImGui.
//
//      Threading: CSpect's CPU must only be touched on the emulator thread (GetRegs() even swaps the register
//      banks to read the shadow set), so the UI never calls iCSpect directly. It queues commands, and Tick()
//      - which CSpect calls every frame, running or stopped in the debugger - executes them, then captures a
//      MonitorSnapshot that the UI draws from.
//
// ********************************************************************************************************************************************
using Plugin;
using System;
using System.Collections.Generic;

namespace Monitor
{
    // *********************************************************************************************************
    /// <summary>
    ///     The Monitor plugin
    /// </summary>
    // *********************************************************************************************************
    public class MonitorPlugin : iPlugin
    {
        internal const string Title = "Monitor - Ctrl+Alt+M";

        /// <summary>CSpect emulator interface</summary>
        iCSpect CSpect;
        /// <summary>Is the window open?</summary>
        volatile bool Active;
        volatile bool OpenRequested;

        iWindow Window;
        MonitorView View;

        // ---- UI -> emulator thread ----
        readonly object Gate = new object();
        readonly Queue<Action> Commands = new Queue<Action>();
        /// <summary>Disassembly: follow PC, or show from DisasmStart</summary>
        bool DisasmFollow = true;
        int DisasmStart = 0;
        /// <summary>Disassembly shows physical memory (DisasmStart is physical)</summary>
        bool DisasmPhysical = false;
        /// <summary>Goto boxes</summary>
        public const int TargetDisasm = 0, TargetMemory = 1;
        readonly GotoResult[] GotoResults = new GotoResult[2];
        int GotoSequence;
        /// <summary>Symbol auto-complete: what each Goto box has typed (null = closed), and the cached answer</summary>
        readonly string[] SymbolQuery = new string[2];
        readonly string[] CachedQuery = new string[2];
        readonly SymbolMatch[][] CachedMatches = new SymbolMatch[2][];
        const int MAX_MATCHES = 12;
        /// <summary>Physical memory the memory pane wants (count 0 = none)</summary>
        int PhysWantStart = 0, PhysWantCount = 0;
        /// <summary>Lines to scroll the disassembly by (+/-), applied on the emulator thread</summary>
        int DisasmScroll = 0;
        int DisasmLines = 40;
        /// <summary>Instructions the UI actually fitted on screen last frame (labels take rows too), 0 = unknown</summary>
        int DisasmVisible = 0;

        // ---- emulator thread state ----
        volatile MonitorSnapshot Latest;
        int StopCount;
        bool WasStopped;
        /// <summary>"Run to here" breakpoint to remove when we next stop (-1 = none)</summary>
        int TempBreakpoint = -1;
        bool RemoteSet;
        int LastStart = -1, LastCount = 0;

        /// <summary>The newest snapshot (null until the first emulator frame after opening)</summary>
        public MonitorSnapshot Snapshot { get { return Latest; } }

        // *********************************************************************************************************
        /// <summary>
        ///     Init the plugin
        /// </summary>
        // *********************************************************************************************************
        public List<sIO> Init(iCSpect _CSpect)
        {
            Console.WriteLine(" Monitor added");
            CSpect = _CSpect;
            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO("<ctrl><shift>m", eAccess.KeyPress, 0));
            return ports;
        }

        public bool KeyPressed(int _id)
        {
            if (_id != 0) return false;
            OpenRequested = true;
            return true;                // handled - CSpect clears the keyboard so the Next doesn't see it
        }

        public byte Read(eAccess _type, int _address, int _id, out bool _isvalid) { _isvalid = false; return 0; }
        public bool Write(eAccess _type, int _port, int _id, byte _value) { return false; }
        public void Reset() { }

        public void Quit()
        {
            Active = false;
            if (Window != null) { iWindow w = Window; Window = null; w.Close(); }
            if (RemoteSet) { CSpect.Debugger(eDebugCommand.SetRemote, 0); RemoteSet = false; }
        }

        // ******************************************************************************************
        /// <summary>
        ///     OS frame: open the window
        /// </summary>
        // ******************************************************************************************
        public void OSTick()
        {
            if (!OpenRequested) return;
            OpenRequested = false;
            if (Window != null) return;

            iWindow w = CSpect.OpenWindow(Title, 1100, 760);
            if (w == null) return;
            if (View == null) View = new MonitorView(this);
            if (!w.SetImGuiDraw(View.Draw))
            {
                Console.WriteLine(" Monitor: Dear ImGui not available on this system");
                w.Close();
                return;
            }
            w.OnClosed += Window_OnClosed;
            Window = w;
            lock (Gate) { Commands.Clear(); DisasmFollow = true; DisasmScroll = 0; }
            Latest = null;
            LastStart = -1;
            Active = true;
        }

        private void Window_OnClosed(object sender, EventArgs e)
        {
            Active = false;
            Window = null;
            lock (Gate) Commands.Clear();
            // don't leave CSpect's own debugger hidden
            if (RemoteSet) { CSpect.Debugger(eDebugCommand.SetRemote, 0); RemoteSet = false; }
        }

        // ******************************************************************************************
        //  UI -> emulator thread commands. Safe to call from any thread.
        // ******************************************************************************************
        void Queue(Action _a) { lock (Gate) Commands.Enqueue(_a); }

        public void Break() { Queue(delegate { if (CSpect.Debugger(eDebugCommand.GetState) == 0) CSpect.Debugger(eDebugCommand.Enter); }); }
        public void Run() { Queue(delegate { CSpect.Debugger(eDebugCommand.Run); }); }
        public void Step() { Queue(delegate { CSpect.Debugger(eDebugCommand.Step); Stepped(); }); }
        public void StepOver() { Queue(delegate { CSpect.Debugger(eDebugCommand.StepOver); Stepped(); }); }
        public void StepBack() { Queue(delegate { CSpect.Debugger(eDebugCommand.UnStep); Stepped(); }); }

        /// <summary>The CPU moved: highlight what changed, and make the disassembly show PC again</summary>
        void Stepped()
        {
            StopCount++;
            lock (Gate) { DisasmFollow = true; DisasmPhysical = false; DisasmScroll = 0; }
            CachedQuery[0] = CachedQuery[1] = null;     // banks may have changed: re-resolve symbol suggestions
        }
        public void ClearAllBreakpoints() { Queue(delegate { CSpect.Debugger(eDebugCommand.ClearAllBreakpoints); TempBreakpoint = -1; }); }

        public void ToggleBreakpoint(int _address) { ToggleBreakpoint(_address, false); }

        /// <summary>Toggle a breakpoint on a 64k address, or on a physical address (CSpect's physical breakpoints)</summary>
        public void ToggleBreakpoint(int _address, bool _physical)
        {
            if (_physical)
            {
                _address = Wrap(_address, true);
                Queue(delegate
                {
                    bool set = CSpect.Debugger(eDebugCommand.GetPhysicalBreakpoint, _address) != 0;
                    CSpect.Debugger(set ? eDebugCommand.ClearPhysicalBreakpoint : eDebugCommand.SetPhysicalBreakpoint, _address);
                });
                return;
            }
            _address &= 0xffff;
            Queue(delegate
            {
                bool set = CSpect.Debugger(eDebugCommand.GetBreakpoint, _address) != 0;
                CSpect.Debugger(set ? eDebugCommand.ClearBreakpoint : eDebugCommand.SetBreakpoint, _address);
                if (TempBreakpoint == _address) TempBreakpoint = -1;       // now the user's
            });
        }

        /// <summary>
        ///     Toggle the breakpoint for a disassembly line (double click, F9, the gutter). Lines in RAM get a physical
        ///     (bank:offset) breakpoint - in the 64K view, on whatever bank is paged in there right now - so it only
        ///     stops in that bank. ROM isn't in physical memory, so a ROM line gets a 64K one. If the line already has a
        ///     64K breakpoint, that's cleared instead.
        /// </summary>
        public void ToggleLineBreakpoint(int _address, bool _physical)
        {
            if (_physical) { ToggleBreakpoint(_address, true); return; }
            _address &= 0xffff;
            Queue(delegate
            {
                if (CSpect.Debugger(eDebugCommand.GetBreakpoint, _address) != 0)
                {
                    CSpect.Debugger(eDebugCommand.ClearBreakpoint, _address);
                    if (TempBreakpoint == _address) TempBreakpoint = -1;
                    return;
                }
                int phys = LogicalToPhysical(_address);
                if (phys < 0) { CSpect.Debugger(eDebugCommand.SetBreakpoint, _address); return; }
                bool set = CSpect.Debugger(eDebugCommand.GetPhysicalBreakpoint, phys) != 0;
                CSpect.Debugger(set ? eDebugCommand.ClearPhysicalBreakpoint : eDebugCommand.SetPhysicalBreakpoint, phys);
            });
        }

        /// <summary>64k address -> physical address through the MMU right now, or -1 for ROM (emulator thread only)</summary>
        int LogicalToPhysical(int _address)
        {
            int bank = CSpect.GetNextRegister((byte)(0x50 + ((_address >> 13) & 7)));
            return bank == 0xff ? -1 : (bank << 13) | (_address & 0x1fff);
        }

        /// <summary>Set a temporary breakpoint and run - removed again when we stop</summary>
        public void RunTo(int _address)
        {
            _address &= 0xffff;
            Queue(delegate
            {
                if (CSpect.Debugger(eDebugCommand.GetBreakpoint, _address) == 0)
                {
                    CSpect.Debugger(eDebugCommand.SetBreakpoint, _address);
                    TempBreakpoint = _address;
                }
                CSpect.Debugger(eDebugCommand.Run);
                WasStopped = false;
            });
        }

        /// <summary>Change registers (only while stopped): _change edits a fresh copy of the live registers</summary>
        public void SetRegisters(Action<Z80Regs> _change)
        {
            Queue(delegate
            {
                if (CSpect.Debugger(eDebugCommand.GetState) == 0) return;
                Z80Regs r = CSpect.GetRegs();
                _change(r);
                CSpect.SetRegs(r);
            });
        }

        public void Poke(int _address, byte _value) { Poke(_address, false, _value); }
        public void Poke(int _address, bool _physical, byte _value)
        {
            if (_physical) { int a = Wrap(_address, true); Queue(delegate { CSpect.PokePhysical(a, _value); }); }
            else Queue(delegate { CSpect.Poke((ushort)(_address & 0xffff), _value); });
        }

        // ******************************************************************************************
        /// <summary>
        ///     Goto a symbol (name, name+offset, name-offset) in the disassembly or memory pane.
        ///     Resolved on the emulator thread; the answer appears in LastGoto(_target).
        /// </summary>
        // ******************************************************************************************
        public void GotoSymbol(string _text, int _target)
        {
            Queue(delegate
            {
                var r = new GotoResult { Sequence = ++GotoSequence };
                string name; int offset, a; bool phys;
                if (!MonitorAddress.TrySplitSymbol(_text, out name, out offset)) r.Error = "Not an address or symbol";
                else if (!ResolveSymbol(name, out a, out phys)) r.Error = "Unknown symbol '" + name + "'";
                else
                {
                    r.Physical = phys;
                    r.Address = Wrap(a + offset, phys);
                    if (_target == TargetDisasm) lock (Gate) { DisasmFollow = false; DisasmPhysical = phys; DisasmStart = r.Address; DisasmScroll = 0; }
                }
                GotoResults[_target] = r;
            });
        }

        /// <summary>The last symbol Goto for a box (null if none yet)</summary>
        public GotoResult LastGoto(int _target) { return GotoResults[_target]; }

        /// <summary>What a Goto box has typed so far, for auto-complete (null/empty to stop)</summary>
        public void SetSymbolQuery(int _target, string _text) { lock (Gate) SymbolQuery[_target] = _text; }

        // ******************************************************************************************
        /// <summary>
        ///     Where a symbol is right now (emulator thread): its 64k address if that's what the CPU
        ///     sees there (bank paged in, ROM, or an equate), else its physical address
        /// </summary>
        // ******************************************************************************************
        bool ResolveSymbol(string _name, out int _address, out bool _physical)
        {
            _address = 0; _physical = false;
            Symbol sym;
            try { sym = CSpect.FindSymbol(_name); }
            catch (Exception) { return false; }                 // older CSpect without symbol lookup
            if (sym == null) return false;
            Resolve(sym, out _address, out _physical);
            return true;
        }

        void Resolve(Symbol _sym, out int _address, out bool _physical)
        {
            int phys = _sym.PhysicalAddress;
            if (_sym.type == eLabelType.Address && phys >= 0 && phys < MonitorAddress.PhysicalSize && _sym.value >= 0 && _sym.value <= 0xffff)
            {
                // what's at that 64k address now? (CSpect's own mapping: ROM is banks 0/1)
                int slot = (_sym.value >> 13) & 7;
                int bank = CSpect.GetNextRegister((byte)(0x50 + slot));
                if (bank == 0xff) bank = (_sym.value >> 13) & 1;
                int now = (bank << 13) | (_sym.value & 0x1fff);
                if (now != phys) { _address = phys; _physical = true; return; }
            }
            _physical = _sym.value > 0xffff;
            _address = Wrap(_sym.value, _physical);
        }

        /// <summary>Auto-complete for the Goto boxes (emulator thread, only when the text changes)</summary>
        void UpdateSymbolMatches(MonitorSnapshot _s)
        {
            for (int t = 0; t < 2; t++)
            {
                string q;
                lock (Gate) q = SymbolQuery[t];
                string name; int offset;
                if (string.IsNullOrEmpty(q) || !MonitorAddress.TrySplitSymbol(q, out name, out offset)) { CachedQuery[t] = null; CachedMatches[t] = null; continue; }
                if (q != CachedQuery[t])
                {
                    var list = new List<SymbolMatch>();
                    try
                    {
                        foreach (Symbol sym in CSpect.FindSymbols(name, MAX_MATCHES))
                        {
                            int a; bool phys;
                            Resolve(sym, out a, out phys);
                            list.Add(new SymbolMatch { Name = sym.name, Address = a, Physical = phys });
                        }
                    }
                    catch (Exception) { }                       // older CSpect without symbol lookup
                    CachedQuery[t] = q;
                    CachedMatches[t] = list.ToArray();
                }
                _s.SymbolQuery[t] = CachedQuery[t];
                _s.SymbolMatches[t] = CachedMatches[t];
            }
        }

        /// <summary>The memory pane is showing physical memory from _start: read _count bytes there each frame (0 = stop)</summary>
        public void SetPhysicalWindow(int _start, int _count)
        {
            lock (Gate) { PhysWantStart = Wrap(_start, true); PhysWantCount = Math.Max(0, Math.Min(_count, 64 * 1024)); }
        }

        static int Wrap(int _address, bool _physical)
        {
            if (!_physical) return _address & 0xffff;
            int m = _address % MonitorAddress.PhysicalSize;
            return m < 0 ? m + MonitorAddress.PhysicalSize : m;
        }

        public void SetRemote(bool _hideCSpectDebugger)
        {
            Queue(delegate { CSpect.Debugger(eDebugCommand.SetRemote, _hideCSpectDebugger ? 1 : 0); RemoteSet = _hideCSpectDebugger; });
        }

        /// <summary>Disassembly view: follow PC</summary>
        public void DisasmFollowPC() { lock (Gate) { DisasmFollow = true; DisasmPhysical = false; DisasmScroll = 0; } }
        /// <summary>Disassembly view: show from a 64k address</summary>
        public void DisasmGoto(int _address) { DisasmGoto(_address, false); }
        /// <summary>Disassembly view: show from a 64k or physical address</summary>
        public void DisasmGoto(int _address, bool _physical)
        {
            lock (Gate) { DisasmFollow = false; DisasmPhysical = _physical; DisasmStart = Wrap(_address, _physical); DisasmScroll = 0; }
        }
        /// <summary>Disassembly view: scroll by instructions (+ down, - up)</summary>
        public void DisasmScrollBy(int _lines)
        {
            lock (Gate)
            {
                if (DisasmFollow && Latest != null && Latest.Disasm.Length > 0) { DisasmStart = Latest.Disasm[0].Address; DisasmPhysical = false; }
                DisasmFollow = false;
                DisasmScroll += _lines;
            }
        }
        public void SetDisasmLines(int _lines) { lock (Gate) DisasmLines = Math.Max(1, Math.Min(200, _lines)); }
        /// <summary>How many instructions the UI managed to show in full</summary>
        public void SetDisasmVisible(int _count) { lock (Gate) DisasmVisible = Math.Max(0, _count); }
        public bool IsFollowingPC { get { lock (Gate) return DisasmFollow; } }


        // ******************************************************************************************
        /// <summary>
        ///     Emulator frame (running, or stopped in the debugger): run queued commands, then capture
        /// </summary>
        // ******************************************************************************************
        public void Tick()
        {
            if (!Active) return;

            Action[] todo;
            lock (Gate) { todo = Commands.ToArray(); Commands.Clear(); }
            foreach (Action a in todo)
            {
                try { a(); }
                catch (Exception ex) { Console.WriteLine(" Monitor: " + ex.Message); }
            }

            bool stopped = CSpect.Debugger(eDebugCommand.GetState) != 0;
            if (stopped && !WasStopped)
            {
                Stepped();
                if (TempBreakpoint >= 0) { CSpect.Debugger(eDebugCommand.ClearBreakpoint, TempBreakpoint); TempBreakpoint = -1; }
            }
            WasStopped = stopped;

            Latest = Capture(stopped);
        }

        // ******************************************************************************************
        /// <summary>
        ///     Read everything the window shows (emulator thread)
        /// </summary>
        // ******************************************************************************************
        MonitorSnapshot Capture(bool _stopped)
        {
            var s = new MonitorSnapshot();
            s.Stopped = _stopped;
            s.StopCount = StopCount;
            s.Remote = RemoteSet;
            s.Regs = CSpect.GetRegs();
            s.Memory = CSpect.Peek(0, 65536);
            s.Mmu = new byte[8];
            for (int i = 0; i < 8; i++) s.Mmu[i] = CSpect.GetNextRegister((byte)(0x50 + i));

            // ---- disassembly ----
            bool follow, phys; int start, scroll, lines, visible, physStart, physCount;
            lock (Gate)
            {
                follow = DisasmFollow; phys = DisasmPhysical && !DisasmFollow; start = DisasmStart; scroll = DisasmScroll;
                lines = DisasmLines; visible = DisasmVisible; DisasmScroll = 0;
                physStart = PhysWantStart; physCount = PhysWantCount;
            }
            int pc = s.Regs.PC;
            if (follow) start = FollowPC(pc, lines, visible);
            if (scroll > 0) for (int i = 0; i < scroll; i++) start = Wrap(start + Length(start, phys), phys);
            else if (scroll < 0) start = BackLines(start, -scroll, phys);
            if (!follow) lock (Gate) { if (!DisasmFollow) DisasmStart = start; }

            var dis = new DisasmLine[lines];
            int a = start;
            for (int i = 0; i < lines; i++)
            {
                DissassemblyLine d = CSpect.DissasembleMemory(a, phys);
                int len = Math.Max(1, Math.Min(6, d.bytes));
                var line = new DisasmLine { Address = a, Physical = phys, Length = len, Text = d.line ?? "", TStates1 = d.TStates1, TStates2 = d.TStates2 };
                if (phys)
                {
                    line.Bytes = CSpect.PeekPhysical(a, len);
                    line.Breakpoint = line.PhysicalBreakpoint = CSpect.Debugger(eDebugCommand.GetPhysicalBreakpoint, a) != 0;
                    int logical = s.ToLogical(a);                       // labels only for banks paged in right now
                    line.Label = logical >= 0 ? Label(logical) : null;
                }
                else
                {
                    line.Bytes = new byte[len];
                    for (int b = 0; b < len; b++) line.Bytes[b] = s.Memory[(a + b) & 0xffff];
                    // a 64K breakpoint, or a physical one on the bank paged in here
                    int pa = s.ToPhysical(a);
                    bool bp64 = CSpect.Debugger(eDebugCommand.GetBreakpoint, a) != 0;
                    line.PhysicalBreakpoint = !bp64 && pa >= 0 && CSpect.Debugger(eDebugCommand.GetPhysicalBreakpoint, pa) != 0;
                    line.Breakpoint = bp64 || line.PhysicalBreakpoint;
                    line.Label = Label(a);
                }
                line.Text = MarkLocalLabels(line.Text, phys ? s.ToLogical(a) : a, line.Bytes);
                dis[i] = line;
                a = Wrap(a + len, phys);
            }
            s.Disasm = dis;
            // FollowPC works in the 64k view
            if (phys) LastStart = -1; else { LastStart = start; LastCount = lines; }

            UpdateSymbolMatches(s);

            if (physCount > 0)
            {
                s.PhysStart = physStart;
                s.PhysMemory = CSpect.PeekPhysical(physStart, physCount);
            }

            // ---- stack labels (return addresses) ----
            s.StackLabels = new string[MonitorSnapshot.StackBelow + MonitorSnapshot.StackWords];
            int sp = s.Regs.SP - MonitorSnapshot.StackBelow * 2;
            for (int i = 0; i < s.StackLabels.Length; i++)
            {
                int at = (sp + i * 2) & 0xffff;
                int word = s.Memory[at] | (s.Memory[(at + 1) & 0xffff] << 8);
                s.StackLabels[i] = Label(word);
            }
            return s;
        }

        // ******************************************************************************************
        /// <summary>
        ///     CSpect writes a local label operand (symbol "MAINLOOP@SKIPPRINT") as just "SKIPPRINT".
        ///     Put the '@' back - "@SKIPPRINT" - by looking up the full symbol at the instruction's target
        ///     (a 16-bit operand, or a relative jump's destination).
        /// </summary>
        /// <param name="_text">Instruction text from CSpect</param>
        /// <param name="_address">64k address of the instruction (-1 if not paged in)</param>
        /// <param name="_bytes">The instruction bytes</param>
        // ******************************************************************************************
        string MarkLocalLabels(string _text, int _address, byte[] _bytes)
        {
            int len = _bytes.Length;
            var targets = new List<int>();
            if (len >= 3)
            {
                targets.Add(_bytes[len - 2] | (_bytes[len - 1] << 8));          // nn
                targets.Add((_bytes[len - 2] << 8) | _bytes[len - 1]);          // PUSH nn (big endian)
            }
            if (len == 2 && _address >= 0) targets.Add((_address + 2 + (sbyte)_bytes[1]) & 0xffff);   // JR/DJNZ e
            foreach (int t in targets)
            {
                string full = Label(t);
                if (full == null) continue;
                int at = full.IndexOf('@');
                if (at < 0 || at == full.Length - 1) continue;
                string local = full.Substring(at + 1);
                string marked = System.Text.RegularExpressions.Regex.Replace(_text,
                    @"(?<![\w@.!])" + System.Text.RegularExpressions.Regex.Escape(local) + @"(?![\w@.!])", "@" + local);
                if (marked != _text) return marked;
            }
            return _text;
        }

        string Label(int _address)
        {
            string l = CSpect.LookUpSymbol(_address & 0xffff);
            return string.IsNullOrEmpty(l) ? null : l;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Where the disassembly should start so PC is on screen:
        ///     - PC still on screen (clear of the bottom few lines): don't move
        ///     - PC just past the bottom: scroll down only as far as needed
        ///     - anywhere else (a jump, call, return, or above the top): jump so PC is a few lines down
        /// </summary>
        // ******************************************************************************************
        internal int FollowPC(int _pc, int _lines, int _visible)
        {
            int shown = _visible > 0 ? Math.Min(_visible, _lines) : Math.Max(1, _lines - 4);
            int margin = Math.Max(0, Math.Min(3, (shown - 1) / 3));
            if (LastStart < 0) return BackLines(_pc, margin);

            // where is PC in the instructions from the current start? (look a little past the bottom too)
            int index = -1, a = LastStart;
            int[] starts = new int[shown + shown / 2 + 1];
            for (int i = 0; i < starts.Length; i++)
            {
                starts[i] = a;
                if (a == _pc) { index = i; break; }
                a = (a + Length(a)) & 0xffff;
            }
            if (index < 0) return BackLines(_pc, margin);                      // gone somewhere else: jump
            int lastOk = shown - 1 - margin;                                    // lowest row PC may sit on
            if (index <= lastOk) return LastStart;                              // still on screen
            return starts[index - lastOk];                                      // scroll so PC sits on that row
        }

        int Length(int _address) { return Length(_address, false); }
        int Length(int _address, bool _physical)
        {
            DissassemblyLine d = CSpect.DissasembleMemory(Wrap(_address, _physical), _physical);
            return Math.Max(1, Math.Min(6, d.bytes));
        }

        // ******************************************************************************************
        /// <summary>
        ///     Walk back _lines instructions from _address. Z80 code can't be disassembled backwards
        ///     reliably, so try every start point up to 24 bytes back, follow each forward, and take the
        ///     instruction before _address that most of the paths that land exactly on it agree on.
        /// </summary>
        // ******************************************************************************************
        internal int BackLines(int _address, int _lines) { return BackLines(_address, _lines, false); }
        internal int BackLines(int _address, int _lines, bool _physical)
        {
            int a = Wrap(_address, _physical);
            for (int n = 0; n < _lines; n++) a = PreviousInstruction(a, _physical);
            return a;
        }

        int PreviousInstruction(int _address, bool _physical)
        {
            const int RANGE = 24;
            int[] len = new int[RANGE];
            for (int i = 0; i < RANGE; i++) len[i] = Length(_address - RANGE + i, _physical);
            int[] votes = new int[RANGE];
            for (int s = 0; s < RANGE; s++)
            {
                int p = s, prev = -1;
                while (p < RANGE) { prev = p; p += len[p]; }
                if (p == RANGE && prev >= 0) votes[prev]++;
            }
            int best = RANGE - 1;            // nothing lands exactly: one byte back
            for (int i = 0; i < RANGE; i++) if (votes[i] > 0 && votes[i] >= votes[best]) best = i;
            return Wrap(_address - RANGE + best, _physical);
        }
    }
}

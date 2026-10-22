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
            ports.Add(new sIO("<ctrl><alt>m", eAccess.KeyPress, 0));
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
            lock (Gate) { DisasmFollow = true; DisasmScroll = 0; }
        }
        public void ClearAllBreakpoints() { Queue(delegate { CSpect.Debugger(eDebugCommand.ClearAllBreakpoints); TempBreakpoint = -1; }); }

        public void ToggleBreakpoint(int _address)
        {
            _address &= 0xffff;
            Queue(delegate
            {
                bool set = CSpect.Debugger(eDebugCommand.GetBreakpoint, _address) != 0;
                CSpect.Debugger(set ? eDebugCommand.ClearBreakpoint : eDebugCommand.SetBreakpoint, _address);
                if (TempBreakpoint == _address) TempBreakpoint = -1;       // now the user's
            });
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

        public void Poke(int _address, byte _value) { Queue(delegate { CSpect.Poke((ushort)(_address & 0xffff), _value); }); }

        public void SetRemote(bool _hideCSpectDebugger)
        {
            Queue(delegate { CSpect.Debugger(eDebugCommand.SetRemote, _hideCSpectDebugger ? 1 : 0); RemoteSet = _hideCSpectDebugger; });
        }

        /// <summary>Disassembly view: follow PC</summary>
        public void DisasmFollowPC() { lock (Gate) { DisasmFollow = true; DisasmScroll = 0; } }
        /// <summary>Disassembly view: show from an address</summary>
        public void DisasmGoto(int _address) { lock (Gate) { DisasmFollow = false; DisasmStart = _address & 0xffff; DisasmScroll = 0; } }
        /// <summary>Disassembly view: scroll by instructions (+ down, - up)</summary>
        public void DisasmScrollBy(int _lines)
        {
            lock (Gate)
            {
                if (DisasmFollow && Latest != null && Latest.Disasm.Length > 0) DisasmStart = Latest.Disasm[0].Address;
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
            bool follow; int start, scroll, lines, visible;
            lock (Gate) { follow = DisasmFollow; start = DisasmStart; scroll = DisasmScroll; lines = DisasmLines; visible = DisasmVisible; DisasmScroll = 0; }
            int pc = s.Regs.PC;
            if (follow) start = FollowPC(pc, lines, visible);
            if (scroll > 0) for (int i = 0; i < scroll; i++) start = (start + Length(start)) & 0xffff;
            else if (scroll < 0) start = BackLines(start, -scroll);
            if (!follow) lock (Gate) { if (!DisasmFollow) DisasmStart = start; }

            var dis = new DisasmLine[lines];
            int a = start;
            for (int i = 0; i < lines; i++)
            {
                DissassemblyLine d = CSpect.DissasembleMemory(a, false);
                int len = Math.Max(1, Math.Min(6, d.bytes));
                var line = new DisasmLine { Address = a, Length = len, Text = d.line ?? "", TStates1 = d.TStates1, TStates2 = d.TStates2 };
                line.Bytes = new byte[len];
                for (int b = 0; b < len; b++) line.Bytes[b] = s.Memory[(a + b) & 0xffff];
                line.Breakpoint = CSpect.Debugger(eDebugCommand.GetBreakpoint, a) != 0;
                line.Label = Label(a);
                dis[i] = line;
                a = (a + len) & 0xffff;
            }
            s.Disasm = dis;
            LastStart = start; LastCount = lines;

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

        int Length(int _address)
        {
            DissassemblyLine d = CSpect.DissasembleMemory(_address & 0xffff, false);
            return Math.Max(1, Math.Min(6, d.bytes));
        }

        // ******************************************************************************************
        /// <summary>
        ///     Walk back _lines instructions from _address. Z80 code can't be disassembled backwards
        ///     reliably, so try every start point up to 24 bytes back, follow each forward, and take the
        ///     instruction before _address that most of the paths that land exactly on it agree on.
        /// </summary>
        // ******************************************************************************************
        internal int BackLines(int _address, int _lines)
        {
            int a = _address & 0xffff;
            for (int n = 0; n < _lines; n++) a = PreviousInstruction(a);
            return a;
        }

        int PreviousInstruction(int _address)
        {
            const int RANGE = 24;
            int[] len = new int[RANGE];
            for (int i = 0; i < RANGE; i++) len[i] = Length(_address - RANGE + i);
            int[] votes = new int[RANGE];
            for (int s = 0; s < RANGE; s++)
            {
                int p = s, prev = -1;
                while (p < RANGE) { prev = p; p += len[p]; }
                if (p == RANGE && prev >= 0) votes[prev]++;
            }
            int best = RANGE - 1;            // nothing lands exactly: one byte back
            for (int i = 0; i < RANGE; i++) if (votes[i] > 0 && votes[i] >= votes[best]) best = i;
            return (_address - RANGE + best) & 0xffff;
        }
    }
}

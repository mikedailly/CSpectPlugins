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
        /// <summary>Symbol completion for the command line (Tab) - no Goto result</summary>
        public const int TargetCommand = 2;
        readonly GotoResult[] GotoResults = new GotoResult[2];
        int GotoSequence;
        /// <summary>Symbol auto-complete: what each Goto box has typed (null = closed), and the cached answer</summary>
        readonly string[] SymbolQuery = new string[3];
        readonly string[] CachedQuery = new string[3];
        readonly SymbolMatch[][] CachedMatches = new SymbolMatch[3][];
        const int MAX_MATCHES = 12;
        /// <summary>Tab cycles through these many at most</summary>
        const int MAX_COMPLETIONS = 64;
        /// <summary>Physical memory the memory pane wants (count 0 = none)</summary>
        int PhysWantStart = 0, PhysWantCount = 0;
        /// <summary>Lines to scroll the disassembly by (+/-), applied on the emulator thread</summary>
        int DisasmScroll = 0;
        int DisasmLines = 40;
        /// <summary>Instructions the UI actually fitted on screen last frame (labels take rows too), 0 = unknown</summary>
        int DisasmVisible = 0;

        // ---- source level debugging ----
        /// <summary>Source view instead of disassembly</summary>
        bool SourceMode;
        /// <summary>Source view: follow PC, or show SrcFile from line SrcTop</summary>
        bool SrcFollow = true;
        int SrcFile = -1, SrcTop = 1, SrcScroll = 0, SrcLines = 40, SrcVisible = 0;
        /// <summary>Bumped by every UI request that moves the source view, so the emulator thread doesn't overwrite a newer one</summary>
        int SrcSeq;
        /// <summary>Show the source line of this physical address next frame (-1 = none)</summary>
        int SrcGoto = -1;
        /// <summary>Most instructions a source step runs before giving up (a line that loops forever, code that never returns)</summary>
        const int MAX_SOURCE_STEP = 2000000;
        /// <summary>Source file names, read when the window opens and whenever the CPU stops</summary>
        string[] SourceFileNames = new string[0];

        // ---- emulator thread state ----
        volatile MonitorSnapshot Latest;
        int StopCount;
        bool WasStopped;
        /// <summary>"Run to here" breakpoint to remove when we next stop (-1 = none)</summary>
        int TempBreakpoint = -1;
        bool RemoteSet;
        /// <summary>The window was closed: run, if we're stopped</summary>
        volatile bool RunOnClose;
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

        // *********************************************************************************************************
        /// <summary>
        ///     Open the Monitor (CSpect calls this whenever it stops, when run with -monitor). Any thread.
        /// </summary>
        /// <returns>true - so CSpect knows the Monitor is here</returns>
        // *********************************************************************************************************
        [Function("monitor_open", "none", "Open the Monitor window (CSpect -monitor uses this as its debugger)")]
        public bool OpenMonitor()
        {
            OpenRequested = true;
            return true;
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
            lock (Gate) { Commands.Clear(); DisasmFollow = true; DisasmScroll = 0; SrcFollow = true; SrcScroll = 0; }
            RunOnClose = false;
            Queue(ReadSourceFiles);
            // the Monitor is the debugger now: hide CSpect's own while it's open (the tick box turns it back on)
            View.HideCSpectDebugger = true;
            SetRemote(true);
            Latest = null;
            LastStart = -1;
            Active = true;
        }

        private void Window_OnClosed(object sender, EventArgs e)
        {
            Active = false;
            Window = null;
            lock (Gate) Commands.Clear();
            RunOnClose = true;                  // closing the Monitor carries on running (on the emulator thread, next Tick)
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
            lock (Gate) { DisasmFollow = true; DisasmPhysical = false; DisasmScroll = 0; SrcFollow = true; SrcScroll = 0; SrcGoto = -1; SrcSeq++; }
            CachedQuery[0] = CachedQuery[1] = CachedQuery[2] = null;     // banks may have changed: re-resolve symbol suggestions
            ReadSourceFiles();
        }

        // ******************************************************************************************
        //  Source level debugging - the map file's [FILES] and [LINES] sections
        // ******************************************************************************************
        /// <summary>Show source instead of disassembly (only does anything if the map file had source lines)</summary>
        public void SetSourceMode(bool _on) { lock (Gate) { SourceMode = _on; SrcSeq++; SrcFollow = true; SrcScroll = 0; SrcGoto = -1; } }
        public bool IsSourceMode { get { lock (Gate) return SourceMode; } }
        public bool IsFollowingPCSource { get { lock (Gate) return SrcFollow; } }
        /// <summary>Source view: follow PC again</summary>
        public void SourceFollowPC() { lock (Gate) { SrcSeq++; SrcFollow = true; SrcScroll = 0; SrcGoto = -1; } }
        /// <summary>Source view: show a file from a line</summary>
        public void SourceShow(int _file, int _line) { lock (Gate) { SrcSeq++; SrcFollow = false; SrcFile = _file; SrcTop = Math.Max(1, _line); SrcScroll = 0; SrcGoto = -1; } }
        /// <summary>Source view: scroll by lines (+ down, - up)</summary>
        public void SourceScrollBy(int _lines)
        {
            lock (Gate)
            {
                if (SrcFollow && Latest != null && Latest.SourceRows.Length > 0) { SrcFile = Latest.SourceFile; SrcTop = Latest.SourceRows[0].Line; }
                SrcFollow = false;
                SrcScroll += _lines;
            }
        }
        public void SetSourceLines(int _lines) { lock (Gate) SrcLines = Math.Max(1, Math.Min(200, _lines)); }
        public void SetSourceVisible(int _count) { lock (Gate) SrcVisible = Math.Max(0, _count); }

        // ---- tabs: CSpect keeps the source's tabs; they're expanded here to the TAB setting ----
        /// <summary>Tab widths: each tab in turn, the last one repeating ("TAB 4,4,4,8")</summary>
        int[] Tabs = { 4 };

        /// <summary>Set the tab widths (any thread)</summary>
        public void SetTabs(int[] _tabs) { lock (Gate) Tabs = (int[])_tabs.Clone(); }
        public int[] GetTabs() { lock (Gate) return (int[])Tabs.Clone(); }

        /// <summary>A source line (1 = first) with its tabs expanded to the tab setting</summary>
        string SourceLine(string[] _text, int _line)
        {
            int[] tabs;
            lock (Gate) tabs = Tabs;
            return ExpandTabs(_text[_line - 1] ?? "", tabs);
        }

        /// <summary>Tabs to spaces: the n'th tab stop is the sum of the first n widths (the last width repeating)</summary>
        public static string ExpandTabs(string _line, int[] _tabs)
        {
            if (_line.IndexOf('\t') < 0) return _line;
            var sb = new System.Text.StringBuilder(_line.Length + 16);
            int stop = 0, n = 0;                        // the next tab stop, and which width made it
            foreach (char c in _line)
            {
                if (c != '\t') { sb.Append(c); continue; }
                // find the first tab stop past the column we're in
                while (stop <= sb.Length) { stop += _tabs[Math.Min(n, _tabs.Length - 1)]; n++; }
                sb.Append(' ', stop - sb.Length);
            }
            return sb.ToString();
        }

        /// <summary>Is this word a label? (case insensitive, remembered - cleared when the CPU stops, in case symbols reload)</summary>
        readonly Dictionary<string, bool> IsLabelCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        bool IsLabel(string _word)
        {
            bool label;
            if (!IsLabelCache.TryGetValue(_word, out label))
            {
                try { label = CSpect.FindSymbol(_word) != null; } catch (Exception) { label = false; }
                IsLabelCache[_word] = label;
            }
            return label;
        }

        /// <summary>
        ///     The words in a source line's code (not its comment) that are labels, as (start, length) pairs.
        ///     Local labels too: @name or .name - or a plain name that isn't a label itself - is SCOPE@NAME, where SCOPE
        ///     is the global label the line comes under.
        /// </summary>
        int[] LabelWords(string _text, string _scope)
        {
            int end = SourceText.CommentStart(_text);
            List<int> words = SourceText.Words(_text, end < 0 ? _text.Length : end);
            var found = new List<int>();
            for (int w = 0; w < words.Count; w += 2)
            {
                string word = _text.Substring(words[w], words[w + 1]);
                bool local = word.Length > 1 && (word[0] == '@' || word[0] == '.');
                string name = local ? word.Substring(1) : word;
                bool label = (!local && IsLabel(word)) ||
                             (_scope != null && (IsLabel(_scope + "@" + name) || IsLabel(_scope + "." + name)));
                if (label) { found.Add(words[w]); found.Add(words[w + 1]); }
            }
            return found.ToArray();
        }

        /// <summary>Each file's scope (the global label in force) at each line - worked out once per file</summary>
        readonly Dictionary<int, string[]> FileScopes = new Dictionary<int, string[]>();

        /// <summary>
        ///     The global label a line comes under: the last label defined at the very start of a line (not a local -
        ///     no @ or . first), up to and including this one. null if there isn't one yet.
        /// </summary>
        string ScopeAt(int _file, string[] _text, int _line)
        {
            string[] scopes;
            if (!FileScopes.TryGetValue(_file, out scopes) || scopes.Length != _text.Length + 1)
            {
                scopes = new string[_text.Length + 1];
                string current = null;
                for (int i = 0; i < _text.Length; i++)
                {
                    string t = _text[i] ?? "";
                    if (t.Length > 0 && SourceText.IsWordChar(t[0]) && t[0] != '@' && t[0] != '.' && !char.IsDigit(t[0]))
                    {
                        int n = 1;
                        while (n < t.Length && SourceText.IsWordChar(t[n])) n++;
                        string word = t.Substring(0, n);
                        if (IsLabel(word)) current = word;
                    }
                    scopes[i + 1] = current;
                }
                FileScopes[_file] = scopes;
            }
            return _line >= 0 && _line < scopes.Length ? scopes[_line] : null;
        }

        void ReadSourceFiles()
        {
            IsLabelCache.Clear();
            FileScopes.Clear();
            try { SourceFileNames = CSpect.GetSourceFiles().ToArray(); }
            catch (Exception) { SourceFileNames = new string[0]; }         // older CSpect
        }

        /// <summary>The source line at a physical address (emulator thread), or null</summary>
        SourceLocation SourceAt(int _physical)
        {
            if (_physical < 0 || SourceFileNames.Length == 0) return null;
            try { return CSpect.GetSourceLocation(_physical); }
            catch (Exception) { return null; }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Step by source line: run instructions until PC reaches the start of a different source line.
        ///     _over: CALLs and RSTs run to their return first (so it never stops inside them).
        ///     Code with no source line (ROM, say) is run through. Stops early at a breakpoint.
        /// </summary>
        // ******************************************************************************************
        public void SourceStep(bool _over)
        {
            Queue(delegate
            {
                if (CSpect.Debugger(eDebugCommand.GetState) == 0) return;
                Z80Regs r = CSpect.GetRegs();
                SourceLocation from = SourceAt(LogicalToPhysical(r.PC));
                if (from == null)
                {
                    // PC isn't in the source: an ordinary instruction step
                    CSpect.Debugger(_over ? eDebugCommand.StepOver : eDebugCommand.Step);
                    Stepped();
                    return;
                }
                int count = 0;
                while (count < MAX_SOURCE_STEP)
                {
                    int pc = r.PC;
                    byte[] op = CSpect.Peek((ushort)pc, 3);
                    if (_over && IsCall(op[0]))
                    {
                        // run the CALL/RST until it comes back to the next instruction, with the stack as it was
                        int ret = (pc + CallLength(op[0])) & 0xffff, sp = r.SP;
                        CSpect.Debugger(eDebugCommand.Step); count++;
                        r = CSpect.GetRegs();
                        while (r.PC != ret || r.SP != sp)
                        {
                            if (count >= MAX_SOURCE_STEP || AtBreakpoint(r.PC)) { Stepped(); return; }
                            CSpect.Debugger(eDebugCommand.Step); count++;
                            r = CSpect.GetRegs();
                        }
                    }
                    else if (_over && IsBackwardBranch(pc, op))
                    {
                        // a loop (DJNZ / JR cc / JP cc back): run it until it falls through, as CSpect's Step Over does
                        int next = (pc + BranchLength(op[0])) & 0xffff;
                        CSpect.Debugger(eDebugCommand.Step); count++;
                        r = CSpect.GetRegs();
                        while (r.PC != next)
                        {
                            if (count >= MAX_SOURCE_STEP || AtBreakpoint(r.PC)) { Stepped(); return; }
                            CSpect.Debugger(eDebugCommand.Step); count++;
                            r = CSpect.GetRegs();
                        }
                    }
                    else
                    {
                        CSpect.Debugger(eDebugCommand.Step); count++;
                        r = CSpect.GetRegs();
                    }
                    if (AtBreakpoint(r.PC)) break;
                    int phys = LogicalToPhysical(r.PC);
                    SourceLocation at = SourceAt(phys);
                    if (at != null && at.Address == phys && (at.File != from.File || at.Line != from.Line)) break;
                }
                Stepped();
            });
        }

        /// <summary>
        ///     A conditional branch backwards - the end of a loop: DJNZ e, JR cc,e or JP cc,nn to before itself.
        ///     (Unconditional JR/JP never fall through, so they aren't "stepped over".)
        /// </summary>
        static bool IsBackwardBranch(int _pc, byte[] _op)
        {
            byte o = _op[0];
            if (o == 0x10 || o == 0x20 || o == 0x28 || o == 0x30 || o == 0x38) return (sbyte)_op[1] < 0;
            if ((o & 0xC7) == 0xC2) return (_op[1] | (_op[2] << 8)) <= _pc;
            return false;
        }
        static int BranchLength(byte _op) { return (_op & 0xC7) == 0xC2 ? 3 : 2; }

        /// <summary>CALL nn, CALL cc,nn, RST n</summary>
        static bool IsCall(byte _op) { return _op == 0xCD || (_op & 0xC7) == 0xC4 || (_op & 0xC7) == 0xC7; }
        static int CallLength(byte _op) { return (_op & 0xC7) == 0xC7 ? 1 : 3; }

        /// <summary>A breakpoint (64K, or physical on the bank paged in) at a 64K address?</summary>
        bool AtBreakpoint(int _pc)
        {
            if (CSpect.Debugger(eDebugCommand.GetBreakpoint, _pc & 0xffff) != 0) return true;
            int phys = LogicalToPhysical(_pc);
            return phys >= 0 && CSpect.Debugger(eDebugCommand.GetPhysicalBreakpoint, phys) != 0;
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
                    if (_target == TargetDisasm) lock (Gate) { DisasmFollow = false; DisasmPhysical = phys; DisasmStart = r.Address; DisasmScroll = 0; SrcSeq++; SrcGoto = phys ? r.Address : LogicalToPhysical(r.Address); }
                }
                GotoResults[_target] = r;
            });
        }

        // ******************************************************************************************
        /// <summary>
        ///     Run a command line through CSpect's own debugger command parser (BR, POKE, registers, SAVE...).
        ///     The answer appears in LastCommand.
        /// </summary>
        // ******************************************************************************************
        public void Command(string _text)
        {
            Queue(delegate
            {
                var r = new CommandResult { Sequence = ++CommandSequence, Command = _text };
                try { r.Message = CSpect.DebugCommand(_text); }
                catch (Exception ex) { r.Message = "Error: " + ex.Message; r.Error = true; }
                if (r.Message != null && r.Message.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) r.Error = true;
                RereadRegisters = true;                     // it may have changed them (shown even while running)
                LastCommandResult = r;
            });
        }
        int CommandSequence;
        volatile CommandResult LastCommandResult;
        bool RereadRegisters;
        /// <summary>The last command line's result (null if none yet)</summary>
        public CommandResult LastCommand { get { return LastCommandResult; } }

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
            for (int t = 0; t < 3; t++)
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
                        foreach (Symbol sym in CSpect.FindSymbols(name, t == TargetCommand ? MAX_COMPLETIONS : MAX_MATCHES))
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
            lock (Gate) { DisasmFollow = false; DisasmPhysical = _physical; DisasmStart = Wrap(_address, _physical); DisasmScroll = 0; SrcSeq++; SrcGoto = _physical ? Wrap(_address, true) : -2 - (_address & 0xffff); }
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
            if (!Active)
            {
                if (RunOnClose)
                {
                    RunOnClose = false;
                    if (CSpect.Debugger(eDebugCommand.GetState) != 0) CSpect.Debugger(eDebugCommand.Run);
                    WasStopped = false;
                }
                return;
            }

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
            // registers (and flags) are read while stopped; running, they stay as they were (the first frame reads them)
            MonitorSnapshot last = Latest;
            s.Regs = (_stopped || last == null || last.Regs == null || RereadRegisters) ? CSpect.GetRegs() : last.Regs;
            RereadRegisters = false;
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
            // following PC: only while stopped - running, the view stays where it was (the first frame shows PC)
            if (follow) start = (_stopped || LastStart < 0) ? FollowPC(pc, lines, visible) : LastStart;
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
            CaptureSource(s);
            CaptureWatches(s, _stopped);

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
        //  Watches: "where[,size[,count]]"
        //      where   $1234, 4660, bank:offset, a label (+/- offset), or a register pair (HL, IX+2, HL'...) -
        //              the memory it points to
        //      size    b byte (default - "where,count" is bytes too), w 16 bit, l 32 bit, t text
        //      count   how many (default 1); for text, characters - 0 (the default) = up to a 0 byte
        // ******************************************************************************************
        /// <summary>Each watch tab's watches</summary>
        readonly string[][] WatchExprs = { new string[0], new string[0], new string[0], new string[0] };
        const int MAX_WATCH = 256;

        /// <summary>The watches to work out every frame (any thread)</summary>
        public void SetWatches(int _set, string[] _watches) { lock (Gate) WatchExprs[_set] = (string[])_watches.Clone(); }

        void CaptureWatches(MonitorSnapshot s, bool _stopped)
        {
            Z80Regs live = _stopped ? s.Regs : null;         // running: registers read live, once, only if a watch uses them
            for (int set = 0; set < WatchExprs.Length; set++)
            {
                string[] w;
                lock (Gate) w = WatchExprs[set];
                var results = new WatchResult[w.Length];
                for (int i = 0; i < w.Length; i++)
                {
                    try { results[i] = EvalWatch(w[i], ref live); }
                    catch (Exception ex) { results[i] = new WatchResult { Expr = w[i], Value = ex.Message, Error = true }; }
                }
                s.WatchSets[set] = results;
            }
        }

        WatchResult EvalWatch(string _expr, ref Z80Regs _regs)
        {
            var r = new WatchResult { Expr = _expr, Error = true };
            string[] parts = (_expr ?? "").Split(',');
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            if (parts[0].Length == 0) { r.Value = "Expected an address, label or register"; return r; }
            if (parts.Length > 3) { r.Value = "Too many parts - where,size,count"; return r; }
            char size = 'b';
            // "where,count" - no size given: bytes
            int n2;
            if (parts.Length == 2 && MonitorAddress.TryNumber(parts[1], out n2)) parts = new[] { parts[0], "b", parts[1] };
            if (parts.Length > 1 && parts[1].Length > 0)
            {
                string z = parts[1].ToLowerInvariant();
                if (z != "b" && z != "w" && z != "l" && z != "t") { r.Value = "Size '" + parts[1] + "' - use b, w, l or t"; return r; }
                size = z[0];
            }
            int count = size == 't' ? 0 : 1;
            if (parts.Length > 2 && parts[2].Length > 0)
            {
                if (!MonitorAddress.TryNumber(parts[2], out count) || count < 0) { r.Value = "Count '" + parts[2] + "' isn't a number"; return r; }
                if (count == 0 && size != 't') { r.Value = "Count must be 1 or more (0 is for null terminated text)"; return r; }
            }
            count = Math.Min(count, MAX_WATCH);

            int a; bool phys; string reg, err;
            if (!WatchAddress(parts[0], ref _regs, out a, out phys, out reg, out err)) { r.Value = err; return r; }
            r.Address = a; r.Physical = phys;
            r.Where = MonitorAddress.Format(a, phys);
            int unit = size == 'w' ? 2 : size == 'l' ? 4 : 1;
            byte[] m = ReadMemory(a, phys, size == 't' ? (count == 0 ? MAX_WATCH : count) : count * unit);
            var text = new System.Text.StringBuilder();
            var dec = new System.Text.StringBuilder();
            if (size == 't')
            {
                int n = m.Length;
                if (count == 0) { n = Array.IndexOf(m, (byte)0); if (n < 0) n = m.Length; }
                text.Append('"');
                for (int i = 0; i < n; i++) text.Append(m[i] >= 32 && m[i] < 127 ? (char)m[i] : '.');
                text.Append('"');
                if (count == 0 && n == m.Length) text.Append("...");
                dec.Append(n + " characters");
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    long v = 0;
                    for (int b = unit - 1; b >= 0; b--) v = (v << 8) | m[i * unit + b];          // little endian
                    if (i > 0) { text.Append(' '); dec.Append(' '); }
                    text.Append('$').Append(v.ToString(unit == 1 ? "X2" : unit == 2 ? "X4" : "X8"));
                    dec.Append(v);
                }
                if (count == 1) text.Append("  ").Append(dec);      // one value: decimal alongside
            }
            r.Value = text.ToString();
            r.Tip = (reg != null ? reg + " -> " : "") + r.Where + "\n" + dec;
            r.Error = false;
            return r;
        }

        /// <summary>
        ///     Where a watch looks: a number, bank:offset, a register pair (the memory it points to), a label - or a
        ///     calculation of any of those ($1234+hl+(a*4), ix+label), with bank:offset parts calculated too
        /// </summary>
        bool WatchAddress(string _text, ref Z80Regs _regs, out int _address, out bool _physical, out string _reg, out string _error)
        {
            _address = 0; _physical = false; _reg = null; _error = null;
            if (MonitorAddress.TryParse(_text, out _address, out _physical)) return true;

            // the simple forms keep their meaning: a label is where it is now (its physical address if its bank
            // isn't paged in), a register pair (+/- n) is what it points to
            string name; int offset;
            if (MonitorAddress.TrySplitSymbol(_text, out name, out offset) && IsName(name))
            {
                string up = name.ToUpperInvariant();
                if (Array.IndexOf(WatchRegisters, up) >= 0)
                {
                    if (_regs == null) _regs = CSpect.GetRegs();
                    int v = RegisterPair(_regs, up);
                    _address = (v + offset) & 0xffff;
                    _reg = up + "=$" + v.ToString("X4") + (offset != 0 ? (offset > 0 ? "+" : "") + offset : "");
                    return true;
                }
                if (ResolveSymbol(name, out _address, out _physical)) { _address = Wrap(_address + offset, _physical); return true; }
            }

            // a calculation; "bank:offset" with either side calculated is physical memory
            Z80Regs regs = _regs;
            Func<string, long?> lookup = n =>
            {
                string up = n.ToUpperInvariant();
                if (IsRegister(up)) { if (regs == null) regs = CSpect.GetRegs(); return Register(regs, up); }
                Symbol sym;
                try { sym = CSpect.FindSymbol(n); } catch (Exception) { sym = null; }
                return sym != null ? sym.value : (long?)null;
            };
            int colon = TopLevelColon(_text);
            long value;
            if (colon >= 0)
            {
                long bank, off;
                if (!WatchExpression.TryEval(_text.Substring(0, colon), lookup, out bank, out _error)) { _regs = regs; return false; }
                if (!WatchExpression.TryEval(_text.Substring(colon + 1), lookup, out off, out _error)) { _regs = regs; return false; }
                value = bank * MonitorAddress.BankSize + off;
                _physical = true;
            }
            else
            {
                if (!WatchExpression.TryEval(_text, lookup, out value, out _error)) { _regs = regs; return false; }
                _physical = value > 0xffff;
            }
            _regs = regs;
            if (value < 0 && !_physical) value &= 0xffff;               // e.g. hl-2 with hl = 0
            _address = Wrap((int)(value % MonitorAddress.PhysicalSize), _physical);
            return true;
        }

        static bool IsName(string _s)
        {
            if (_s.Length == 0 || !(char.IsLetter(_s[0]) || _s[0] == '_' || _s[0] == '@' || _s[0] == '.')) return false;
            for (int i = 1; i < _s.Length; i++)
                if (!(char.IsLetterOrDigit(_s[i]) || _s[i] == '_' || _s[i] == '@' || _s[i] == '.' || _s[i] == '!' || (_s[i] == '\'' && i == _s.Length - 1))) return false;
            return true;
        }

        /// <summary>The ':' of "bank:offset", outside any brackets (-1 if none)</summary>
        static int TopLevelColon(string _s)
        {
            int depth = 0;
            for (int i = 0; i < _s.Length; i++)
            {
                if (_s[i] == '(') depth++;
                else if (_s[i] == ')') depth--;
                else if (_s[i] == ':' && depth == 0) return i;
            }
            return -1;
        }

        static readonly string[] Registers8 = { "A", "F", "B", "C", "D", "E", "H", "L", "I", "R", "IXH", "IXL", "IYH", "IYL",
                                                "A'", "F'", "B'", "C'", "D'", "E'", "H'", "L'" };
        static bool IsRegister(string _up) { return Array.IndexOf(WatchRegisters, _up) >= 0 || Array.IndexOf(Registers8, _up) >= 0; }

        /// <summary>A register's value, 8 or 16 bit (name in upper case)</summary>
        static long Register(Z80Regs r, string _up)
        {
            switch (_up)
            {
                case "A": return r.AF >> 8; case "F": return r.AF & 0xff;
                case "B": return r.BC >> 8; case "C": return r.BC & 0xff;
                case "D": return r.DE >> 8; case "E": return r.DE & 0xff;
                case "H": return r.HL >> 8; case "L": return r.HL & 0xff;
                case "I": return r.I; case "R": return r.R;
                case "IXH": return r.IX >> 8; case "IXL": return r.IX & 0xff;
                case "IYH": return r.IY >> 8; case "IYL": return r.IY & 0xff;
                case "A'": return r._AF >> 8; case "F'": return r._AF & 0xff;
                case "B'": return r._BC >> 8; case "C'": return r._BC & 0xff;
                case "D'": return r._DE >> 8; case "E'": return r._DE & 0xff;
                case "H'": return r._HL >> 8; case "L'": return r._HL & 0xff;
            }
            return RegisterPair(r, _up);
        }

        static readonly string[] WatchRegisters = { "AF", "BC", "DE", "HL", "IX", "IY", "SP", "PC", "AF'", "BC'", "DE'", "HL'" };

        static int RegisterPair(Z80Regs r, string _name)
        {
            switch (_name)
            {
                case "AF": return r.AF; case "BC": return r.BC; case "DE": return r.DE; case "HL": return r.HL;
                case "IX": return r.IX; case "IY": return r.IY; case "SP": return r.SP; case "PC": return r.PC;
                case "AF'": return r._AF; case "BC'": return r._BC; case "DE'": return r._DE; case "HL'": return r._HL;
            }
            return 0;
        }

        /// <summary>_count bytes from a 64K or physical address, wrapping at the end</summary>
        byte[] ReadMemory(int _address, bool _physical, int _count)
        {
            var r = new byte[_count];
            int limit = _physical ? MonitorAddress.PhysicalSize : 0x10000, done = 0;
            while (done < _count)
            {
                int at = (_address + done) % limit;
                int chunk = Math.Min(_count - done, limit - at);
                byte[] b = _physical ? CSpect.PeekPhysical(at, chunk) : CSpect.Peek((ushort)at, chunk);
                Array.Copy(b, 0, r, done, Math.Min(chunk, b.Length));
                done += chunk;
            }
            return r;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Source view (emulator thread): which file and lines to show, following PC unless scrolled away
        /// </summary>
        // ******************************************************************************************
        void CaptureSource(MonitorSnapshot s)
        {
            s.SourceFiles = SourceFileNames;
            if (SourceFileNames.Length == 0) return;
            s.PCSource = SourceAt(s.ToPhysical(s.Regs.PC));

            bool on, follow; int file, top, scroll, lines, visible, go, seq;
            lock (Gate)
            {
                seq = SrcSeq;
                on = SourceMode; follow = SrcFollow; file = SrcFile; top = SrcTop; scroll = SrcScroll; lines = SrcLines;
                visible = SrcVisible > 0 ? SrcVisible : lines; go = SrcGoto;
                if (!on) SrcGoto = -1;
            }
            s.SourceMode = on;
            if (!on) return;

            // a Goto: the source line of that address, if it has one (-2 - addr = a 64K address, mapped now)
            if (go != -1)
            {
                SourceLocation g = SourceAt(go >= 0 ? go : s.ToPhysical(-2 - go));
                if (g != null) { follow = false; file = g.File; top = Math.Max(1, g.Line - 3); }
            }
            else if (follow && s.PCSource != null && (s.Stopped || file < 0))     // running: stay put
            {
                int line = s.PCSource.Line;
                if (s.PCSource.File != file || line < top || line > top + visible - 1) { file = s.PCSource.File; top = Math.Max(1, line - 3); }
                else if (line > top + visible - 4) top = Math.Max(1, line - visible + 4);      // near the bottom: a line at a time
            }
            if (file < 0 || file >= SourceFileNames.Length) file = 0;
            string[] text;
            try { text = CSpect.GetSourceText(file) ?? new string[0]; } catch (Exception) { text = new string[0]; }
            top = Math.Max(1, Math.Min(top + scroll, Math.Max(1, text.Length - 2)));
            // remember where we are - unless the UI asked for something else meanwhile (that wins next frame)
            lock (Gate)
            {
                if (SrcSeq == seq) { SrcFollow = follow && SrcFollow; SrcFile = file; SrcTop = top; SrcScroll -= scroll; SrcGoto = -1; }
            }

            var rows = new List<SourceRow>(lines);
            for (int i = 0; i < lines && top + i <= text.Length; i++)
            {
                var row = new SourceRow { Line = top + i, Text = SourceLine(text, top + i) };
                row.Labels = LabelWords(row.Text, ScopeAt(file, text, row.Line));
                try { row.Address = CSpect.GetSourceAddress(file, row.Line); } catch (Exception) { row.Address = -1; }
                if (row.Address >= 0)
                {
                    int logical = s.ToLogical(row.Address);
                    bool bp64 = logical >= 0 && CSpect.Debugger(eDebugCommand.GetBreakpoint, logical) != 0;
                    row.PhysicalBreakpoint = !bp64 && CSpect.Debugger(eDebugCommand.GetPhysicalBreakpoint, row.Address) != 0;
                    row.Breakpoint = bp64 || row.PhysicalBreakpoint;
                }
                rows.Add(row);
            }
            s.SourceFile = file;
            s.SourceRows = rows.ToArray();
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

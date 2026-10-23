// ********************************************************************************************************************************************
//      CSpect Monitor extension - a debugger front end: registers, disassembly, stack and memory in one window
//      Released under the GNU 3 license - please see license file for more details
//
//      The UI: a toolbar, then four panes - disassembly and memory on the left, registers and stack on the
//      right - separated by splitter bars that can be dragged to resize them. Drawn with Dear ImGui into the
//      plugin's own OS window; all emulator access goes through MonitorPlugin's command queue.
//
// ********************************************************************************************************************************************
using ImGuiNET;
using Plugin;
using System;
using System.Numerics;
using System.Text;

namespace Monitor
{
    public class MonitorView
    {
        /// <summary>An ABGR colour (ImGui's own packing, 0xAABBGGRR) as a Vector4</summary>
        static Vector4 Abgr(uint _c) { return new Vector4((_c & 0xff) / 255f, ((_c >> 8) & 0xff) / 255f, ((_c >> 16) & 0xff) / 255f, (_c >> 24) / 255f); }
        static readonly Vector4 ColPCRow = Abgr(0xff0000B6);           // red
        static readonly Vector4 ColBreakpoint = new Vector4(0.90f, 0.20f, 0.20f, 1.0f);
        static readonly Vector4 ColBreakpointRow = new Vector4(0.62f, 0.10f, 0.10f, 1.0f);
        static readonly Vector4 ColPhysBreakpointRow = new Vector4(0.36f, 0.05f, 0.05f, 1.0f);   // darker: physical (bank:offset)
        static readonly Vector4 ColPhysBreakpoint = new Vector4(0.55f, 0.10f, 0.10f, 1.0f);
        static readonly Vector4 ColChanged = new Vector4(1.00f, 0.45f, 0.35f, 1.0f);
        static readonly Vector4 ColLabel = new Vector4(0.45f, 0.80f, 1.00f, 1.0f);
        static readonly Vector4 ColDim = new Vector4(0.55f, 0.58f, 0.65f, 1.0f);
        static readonly Vector4 ColSP = new Vector4(0.25f, 0.35f, 0.55f, 1.0f);
        static readonly Vector4 ColRunning = new Vector4(0.45f, 0.85f, 0.45f, 1.0f);
        static readonly Vector4 ColFlagOn = new Vector4(0.20f, 0.45f, 0.80f, 1.0f);
        static readonly Vector4 ColFlagOff = new Vector4(0.13f, 0.15f, 0.19f, 1.0f);
        static readonly Vector4 ColNormal = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        static readonly Vector4 ColHex = new Vector4(1.00f, 0.88f, 0.35f, 1.0f);
        static readonly Vector4 ColUserBar = Abgr(0xffB7B700);         // teal
        static readonly Vector4 ColTStates = new Vector4(0.50f, 0.50f, 0.50f, 1.0f);      // medium grey
        static readonly Vector4 ColStopped = new Vector4(1.00f, 0.75f, 0.30f, 1.0f);

        const float SPLIT = 6.0f;               // splitter bar thickness
        const float MIN_PANE = 60.0f;

        static readonly string[] SpaceNames = { "64K", "Physical" };
        static readonly string[] FollowNames = { "None", "PC", "SP", "HL", "DE", "BC", "IX", "IY" };

        MonitorPlugin Plugin;

        // pane sizes, as fractions so they scale with the window
        internal float SplitX = 0.64f;          // left column width
        internal float SplitLeftY = 0.60f;      // disassembly / memory
        internal float SplitRightY = 0.42f;     // registers / stack
        /// <summary>Registers pane content size (measured as it's drawn), and whether the splits have been fitted to it yet</summary>
        Vector2 RegsNeed;
        internal bool LayoutFitted;

        // previous stop, for "changed" highlighting
        MonitorSnapshot Shown;
        Z80Regs PrevRegs;
        byte[] PrevMemory;
        int ShownStopCount = -1;

        /// <summary>The disassembly's Goto box (each memory window has its own)</summary>
        readonly GotoBox DisasmBox = new GotoBox();
        /// <summary>Last symbol Goto result seen, per target (disassembly, memory)</summary>
        readonly int[] SeenGoto = new int[2];
        static readonly Vector4 ColError = new Vector4(1.0f, 0.35f, 0.35f, 1.0f);

        sealed class GotoBox
        {
            public string Text = "";
            /// <summary>Highlighted suggestion (arrow keys), -1 = none</summary>
            public int Selected = -1;
            /// <summary>Mouse over the suggestion list last frame (keeps it open while clicking)</summary>
            public bool ListHovered;
            /// <summary>Where the suggestion list was last frame (screen) - hover is tested against this rather than with
            /// IsWindowHovered, which an active text box can block</summary>
            public Vector2 ListMin, ListMax;
            public string Error;
            public double ErrorTime;
        }
        // ---- memory windows: up to 4, shown as tabs in the memory pane ----
        sealed class MemView
        {
            public bool Enabled;
            public int Follow = 0;
            public int FollowLast = -1;
            public int ScrollTo = -1;
            /// <summary>Shows physical memory (addresses bank:offset) rather than the CPU's 64K</summary>
            public bool Physical;
            public int TopAddress;
            public readonly GotoBox Box = new GotoBox();
        }
        internal const int MEM_VIEWS = 4;
        readonly MemView[] Mems = { new MemView { Enabled = true }, new MemView(), new MemView(), new MemView() };
        /// <summary>The memory tab on show</summary>
        int ActiveMem = 0;
        /// <summary>Bring this memory tab to the front next frame (-1 = none)</summary>
        int SelectMem = -1;
        bool MemEditPhysical;
        byte[] PrevPhys;
        int PrevPhysStart;
        int MemEditAddress = -1;
        string MemEditText = "";
        string RegEditName;
        string RegEditText = "";
        /// <summary>"Hide CSpect debugger" - on whenever the Monitor opens (MonitorPlugin.OSTick)</summary>
        internal bool HideCSpectDebugger = true;

        public MonitorView(MonitorPlugin _plugin) { Plugin = _plugin; }

        // ****************************************************************************************
        //  iWindow ImGui draw callback
        // ****************************************************************************************
        /// <summary>Font size (Ctrl+mouse wheel), 1 = normal</summary>
        internal float FontScale = 1.0f;
        const float MIN_SCALE = 0.6f, MAX_SCALE = 3.0f;
        static float CurrentScale = 1.0f;
        /// <summary>A fixed pixel size, scaled with the font</summary>
        static float S(float _pixels) { return _pixels * CurrentScale; }
        /// <summary>Child window flags: no mouse wheel scrolling while Ctrl is held (Ctrl+wheel zooms)</summary>
        static ImGuiWindowFlags WheelFlags() { return ImGui.GetIO().KeyCtrl ? ImGuiWindowFlags.NoScrollWithMouse : ImGuiWindowFlags.None; }

        public void Draw()
        {
            // Ctrl+mouse wheel: font size (this window's own ImGui context, so only the Monitor changes)
            ImGuiIOPtr io = ImGui.GetIO();
            if (io.KeyCtrl && io.MouseWheel != 0)
            {
                FontScale = (float)Math.Round(Clamp(FontScale + (io.MouseWheel > 0 ? 0.1f : -0.1f), MIN_SCALE, MAX_SCALE), 1);
            }
            io.FontGlobalScale = FontScale;
            CurrentScale = FontScale;

            MonitorSnapshot s = Plugin.Snapshot;
            if (s != null && s.StopCount != ShownStopCount)
            {
                // the CPU moved on: remember what it looked like before, for highlighting changes
                if (Shown != null) { PrevRegs = Shown.Regs; PrevMemory = Shown.Memory; PrevPhys = Shown.PhysMemory; PrevPhysStart = Shown.PhysStart; }
                ShownStopCount = s.StopCount;
            }
            Shown = s;

            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                                     ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoScrollbar |
                                     ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.MenuBar;
            bool open = true;
            if (ImGui.Begin("##Monitor", ref open, flags))
            {
                DrawMenu(s);
                FunctionKeys(s);
                DrawToolbar(s);
                ImGui.Separator();
                if (s == null) TextDim("Waiting for the emulator...");
                else DrawPanes(s);
            }
            ImGui.End();
        }

        // ****************************************************************************************
        //  Menu bar: Debug and Memory
        // ****************************************************************************************
        void DrawMenu(MonitorSnapshot s)
        {
            if (!ImGui.BeginMenuBar()) return;
            bool stopped = s != null && s.Stopped;
            if (ImGui.BeginMenu("Debug"))
            {
                if (ImGui.MenuItem("Run", "F1", false, s != null && stopped)) Plugin.Run();
                if (ImGui.MenuItem("Break", "F1", false, s != null && !stopped)) Plugin.Break();
                ImGui.Separator();
                if (ImGui.MenuItem("Step", "F7", false, stopped)) Plugin.Step();
                if (ImGui.MenuItem("Step Over", "F8", false, stopped)) Plugin.StepOver();
                if (ImGui.MenuItem("Step Back", null, false, stopped)) Plugin.StepBack();
                ImGui.Separator();
                if (ImGui.MenuItem("Goto PC")) Plugin.DisasmFollowPC();
                if (ImGui.MenuItem("Toggle Breakpoint", "F9", false, s != null)) ToggleBarBreakpoint(s);
                if (ImGui.MenuItem("Clear Breakpoints")) Plugin.ClearAllBreakpoints();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Memory"))
            {
                for (int i = 0; i < MEM_VIEWS; i++)
                {
                    bool on = Mems[i].Enabled;
                    if (ImGui.MenuItem("Memory " + (i + 1), null, ref on))
                    {
                        Mems[i].Enabled = on;
                        if (on) SelectMem = i;
                    }
                }
                ImGui.EndMenu();
            }
            ImGui.EndMenuBar();
        }

        /// <summary>F9 / Debug menu: breakpoint on the user bar, or on PC if there's no bar</summary>
        void ToggleBarBreakpoint(MonitorSnapshot s)
        {
            int bi = BarIndex(s);
            if (bi >= 0) Plugin.ToggleLineBreakpoint(s.Disasm[bi].Address, s.Disasm[bi].Physical);
            else Plugin.ToggleLineBreakpoint(s.Regs.PC, false);
        }

        // ****************************************************************************************
        //  F1 break/run, F7 step, F8 step over, F9 breakpoint on the user bar (or on PC if there's no bar)
        //  - anywhere in the Monitor window, unless typing into a box
        // ****************************************************************************************
        void FunctionKeys(MonitorSnapshot s)
        {
            // Keys typed into the Monitor never reach the emulator - CSpect would act on them too (its own debugger
            // steps on F7/F8, and F8 toggles VSync while running). This only covers the Monitor's own OS window.
            ImGui.SetNextFrameWantCaptureKeyboard(true);
            if (s == null || ImGui.GetIO().WantTextInput) return;
            if (ImGui.IsKeyPressed(ImGuiKey.F1, false)) { if (s.Stopped) Plugin.Run(); else Plugin.Break(); return; }
            bool f7 = ImGui.IsKeyPressed(ImGuiKey.F7), f8 = ImGui.IsKeyPressed(ImGuiKey.F8), f9 = ImGui.IsKeyPressed(ImGuiKey.F9, false);
            if (f7 && s.Stopped) Plugin.Step();
            else if (f8 && s.Stopped) Plugin.StepOver();
            else if (f9) ToggleBarBreakpoint(s);
        }

        // ****************************************************************************************
        //  Run/Break, Step, Step Over, Back, breakpoints, status
        // ****************************************************************************************
        void DrawToolbar(MonitorSnapshot s)
        {
            bool stopped = s != null && s.Stopped;
            // Run/Break swap places, so give them the same width
            float runWidth = ImGui.CalcTextSize("Break (F1)").X + ImGui.GetStyle().FramePadding.X * 2;
            if (stopped) { if (ImGui.Button("Run (F1)", new Vector2(runWidth, 0))) Plugin.Run(); }
            else { if (ImGui.Button("Break (F1)", new Vector2(runWidth, 0))) Plugin.Break(); }
            ImGui.SameLine();
            ImGui.BeginDisabled(!stopped);
            if (ImGui.Button("Step (F7)")) Plugin.Step();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) Tip("Single step");
            ImGui.SameLine();
            if (ImGui.Button("Step Over (F8)")) Plugin.StepOver();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) Tip("Step over CALLs, RSTs and repeated instructions");
            ImGui.SameLine();
            if (ImGui.Button("Back")) Plugin.StepBack();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) Tip("Rewind the CPU one instruction");
            ImGui.EndDisabled();
            ImGui.SameLine(0, S(20));
            if (ImGui.Button("Clear Breakpoints")) Plugin.ClearAllBreakpoints();
            ImGui.SameLine(0, S(20));
            if (ImGui.Checkbox("Hide CSpect debugger", ref HideCSpectDebugger)) Plugin.SetRemote(HideCSpectDebugger);
            if (ImGui.IsItemHovered()) Tip("While stopped, show the game screen in the main window instead of CSpect's own debugger");

            if (s != null)
            {
                ImGui.SameLine(0, S(20));
                if (s.Stopped)
                {
                    string label = LabelAt(s, s.Regs.PC);
                    TextCol(ColStopped, "Stopped at $" + s.Regs.PC.ToString("X4") + (label != null ? "  " + label : ""));
                }
                else TextCol(ColRunning, "Running");
            }
        }

        static string LabelAt(MonitorSnapshot s, int _address)
        {
            foreach (DisasmLine l in s.Disasm) if (!l.Physical && l.Address == _address) return l.Label;
            return null;
        }

        // ****************************************************************************************
        //  The four panes and the three splitter bars
        // ****************************************************************************************
        void DrawPanes(MonitorSnapshot s)
        {
            Vector2 origin = ImGui.GetCursorPos();
            Vector2 avail = ImGui.GetContentRegionAvail();
            float W = Math.Max(avail.X, MIN_PANE * 2 + SPLIT), H = Math.Max(avail.Y, MIN_PANE * 2 + SPLIT);
            if (!LayoutFitted && RegsNeed.X > 0) FitRightColumn(W, H);

            float leftW = Clamp(W * SplitX, MIN_PANE, W - SPLIT - MIN_PANE);
            float rightW = W - leftW - SPLIT;
            float leftTop = Clamp(H * SplitLeftY, MIN_PANE, H - SPLIT - MIN_PANE);
            float rightTop = Clamp(H * SplitRightY, MIN_PANE, H - SPLIT - MIN_PANE);
            float rightX = origin.X + leftW + SPLIT;

            ImGui.SetCursorPos(origin);
            Pane("Disassembly", "##disasm", new Vector2(leftW, leftTop), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse, () => DrawDisassembly(s));
            Splitter("##splitLeft", new Vector2(origin.X, origin.Y + leftTop), new Vector2(leftW, SPLIT), false, ref SplitLeftY, H);
            ImGui.SetCursorPos(new Vector2(origin.X, origin.Y + leftTop + SPLIT));
            Pane(null, "##memory", new Vector2(leftW, H - leftTop - SPLIT), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse, () => DrawMemory(s));

            Splitter("##splitX", new Vector2(origin.X + leftW, origin.Y), new Vector2(SPLIT, H), true, ref SplitX, W);

            ImGui.SetCursorPos(new Vector2(rightX, origin.Y));
            Pane("Registers", "##regs", new Vector2(rightW, rightTop), WheelFlags(), () =>
            {
                ImGui.BeginGroup();
                DrawRegisters(s);
                ImGui.EndGroup();
                // how big the pane needs to be: everything drawn, plus the window padding
                if (ImGui.GetScrollY() == 0 && ImGui.GetScrollX() == 0)
                    RegsNeed = ImGui.GetItemRectMax() - ImGui.GetWindowPos() + ImGui.GetStyle().WindowPadding;
            });
            Splitter("##splitRight", new Vector2(rightX, origin.Y + rightTop), new Vector2(rightW, SPLIT), false, ref SplitRightY, H);
            ImGui.SetCursorPos(new Vector2(rightX, origin.Y + rightTop + SPLIT));
            Pane("Stack", "##stack", new Vector2(rightW, H - rightTop - SPLIT), WheelFlags(), () => DrawStack(s));
        }

        /// <summary>
        ///     First layout: make the registers/stack column just wide enough for the registers (and the stack's
        ///     offset/address/value with room for a label), and the registers pane just tall enough for its contents.
        ///     Everything else goes to the disassembly and memory. The splitters can still move them after.
        /// </summary>
        void FitRightColumn(float W, float H)
        {
            LayoutFitted = true;
            ImGuiStylePtr st = ImGui.GetStyle();
            float stackW = ImGui.CalcTextSize("-08 $FEF8 $0000 ").X + ImGui.CalcTextSize(new string('M', 12)).X +
                           st.CellPadding.X * 6 + st.WindowPadding.X * 2 + st.ScrollbarSize;
            float needW = Math.Max(RegsNeed.X, stackW) + 2;
            float needH = RegsNeed.Y + 2;
            if (needW < W - SPLIT - MIN_PANE) SplitX = (W - needW - SPLIT) / W;
            if (needH < H - SPLIT - MIN_PANE) SplitRightY = needH / H;
        }

        // ****************************************************************************************
        //  Text that is never a printf format. ImGui.NET passes the string straight through as cimgui's "fmt",
        //  so a '%' in it (a memory byte of $25 in the ASCII column, a label, a tooltip) reads varargs that
        //  aren't there and crashes. Everything with variable text goes through these.
        // ****************************************************************************************
        static void TextCol(Vector4 _col, string _text)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, _col);
            ImGui.TextUnformatted(_text);
            ImGui.PopStyleColor();
        }
        static void TextDim(string _text) { TextCol(ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], _text); }
        static void TextWrap(string _text)
        {
            ImGui.PushTextWrapPos(0.0f);
            ImGui.TextUnformatted(_text);
            ImGui.PopTextWrapPos();
        }
        static void Tip(string _text)
        {
            if (!ImGui.BeginTooltip()) return;
            ImGui.TextUnformatted(_text);
            ImGui.EndTooltip();
        }

        static float Clamp(float v, float lo, float hi) { return Math.Max(lo, Math.Min(hi, v)); }

        static void Pane(string _title, string _id, Vector2 _size, ImGuiWindowFlags _flags, Action _body)
        {
            if (ImGui.BeginChild(_id, _size, ImGuiChildFlags.Borders, _flags))
            {
                if (_title != null) TextCol(ColLabel, _title);
                _body();
            }
            ImGui.EndChild();
        }

        /// <summary>A draggable bar: moves _fraction (of _total) with the mouse</summary>
        static void Splitter(string _id, Vector2 _pos, Vector2 _size, bool _vertical, ref float _fraction, float _total)
        {
            ImGui.SetCursorPos(_pos);
            ImGui.InvisibleButton(_id, _size);
            bool hot = ImGui.IsItemHovered() || ImGui.IsItemActive();
            if (hot) ImGui.SetMouseCursor(_vertical ? ImGuiMouseCursor.ResizeEW : ImGuiMouseCursor.ResizeNS);
            if (ImGui.IsItemActive())
            {
                Vector2 d = ImGui.GetIO().MouseDelta;
                _fraction = Clamp(_fraction + (_vertical ? d.X : d.Y) / _total, 0.1f, 0.9f);
            }
            if (hot)
            {
                Vector2 min = ImGui.GetItemRectMin(), max = ImGui.GetItemRectMax();
                ImGui.GetWindowDrawList().AddRectFilled(min, max, ImGui.GetColorU32(ImGui.IsItemActive() ? ImGuiCol.SeparatorActive : ImGuiCol.SeparatorHovered));
            }
        }

        static bool HexInput(string _id, ref string _text, float _width)
        {
            ImGui.SetNextItemWidth(_width);
            return ImGui.InputText(_id, ref _text, 8, ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        }

        static bool ParseHex(string _text, out int _value)
        {
            _value = 0;
            _text = (_text ?? "").Trim().TrimStart('$');
            if (_text.StartsWith("0x") || _text.StartsWith("0X")) _text = _text.Substring(2);
            return _text.Length > 0 && int.TryParse(_text, System.Globalization.NumberStyles.HexNumber, null, out _value);
        }

        // ****************************************************************************************
        //  Disassembly: gutter (breakpoints), address, bytes, instruction. PC highlighted.
        //  Click the gutter to toggle a breakpoint; right click a line for more.
        // ****************************************************************************************
        /// <summary>Width of the Symbols column, in characters - longer labels are cropped</summary>
        const int SYMBOL_CHARS = 32;

        static string Crop(string _text, int _chars) { return _text.Length > _chars ? _text.Substring(0, _chars) : _text; }

        // ---- user bar: a pale green line moved with the cursor keys (or by clicking), scrolling the view at the edges ----
        int BarAddress = -1;
        bool BarPhysical;
        /// <summary>After asking the plugin to scroll: put the bar on this row of the next snapshot (-1 = nothing pending)</summary>
        int BarWantRow = -1;
        MonitorSnapshot BarRequestSnapshot;
        /// <summary>Instructions shown in full last frame</summary>
        int LastFitted = 1;

        int BarIndex(MonitorSnapshot s)
        {
            for (int i = 0; i < s.Disasm.Length && i < LastFitted; i++)
                if (s.Disasm[i].Address == BarAddress && s.Disasm[i].Physical == BarPhysical) return i;
            return -1;
        }

        void PutBar(DisasmLine _l) { BarAddress = _l.Address; BarPhysical = _l.Physical; }

        /// <summary>Scroll by _lines and put the bar on _row once the scrolled view arrives</summary>
        void ScrollWithBar(MonitorSnapshot s, int _lines, int _row)
        {
            Plugin.DisasmScrollBy(_lines);
            BarWantRow = Math.Max(0, _row);
            BarRequestSnapshot = s;
        }

        // ****************************************************************************************
        /// <summary>
        ///     Cursor keys move the user bar while the disassembly has focus: Up/Down a line, Page Up/Down a screen;
        ///     past the top or bottom the view scrolls. Shift+Enter sets PC to the bar.
        ///     The keys are kept from the emulator meanwhile.
        /// </summary>
        // ****************************************************************************************
        void UserBarKeys(MonitorSnapshot s)
        {
            // the scroll we asked for has arrived: place the bar
            if (BarWantRow >= 0 && s != BarRequestSnapshot && s.Disasm.Length > 0)
            {
                PutBar(s.Disasm[Math.Min(BarWantRow, s.Disasm.Length - 1)]);
                BarWantRow = -1;
            }
            if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows) || ImGui.IsAnyItemActive() || s.Disasm.Length == 0) return;
            ImGui.SetNextFrameWantCaptureKeyboard(true);            // don't pass the keys on to the emulator

            // Shift+Enter: set PC to the user bar (stopped only; a physical line needs its bank paged in)
            if (ImGui.GetIO().KeyShift && (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false)))
            {
                int bi = BarIndex(s);
                if (bi >= 0 && s.Stopped)
                {
                    DisasmLine l = s.Disasm[bi];
                    int pc = l.Physical ? s.ToLogical(l.Address) : l.Address;
                    if (pc >= 0) Plugin.SetRegisters(r => r.PC = (ushort)pc);
                }
                return;
            }

            bool up = ImGui.IsKeyPressed(ImGuiKey.UpArrow), down = ImGui.IsKeyPressed(ImGuiKey.DownArrow);
            bool pgUp = ImGui.IsKeyPressed(ImGuiKey.PageUp), pgDown = ImGui.IsKeyPressed(ImGuiKey.PageDown);
            if (!(up || down || pgUp || pgDown) || BarWantRow >= 0) return;

            int shown = Math.Max(1, Math.Min(LastFitted, s.Disasm.Length));
            int i = BarIndex(s);
            if (i < 0)
            {
                // no bar on screen yet: start on PC if it's showing, else the top line
                int start = 0;
                for (int k = 0; k < shown; k++) if (!s.Disasm[k].Physical && s.Disasm[k].Address == s.Regs.PC) start = k;
                PutBar(s.Disasm[start]);
                return;
            }
            int page = Math.Max(1, shown - 1);
            if (up) { if (i > 0) PutBar(s.Disasm[i - 1]); else ScrollWithBar(s, -1, 0); }
            else if (down) { if (i < shown - 1) PutBar(s.Disasm[i + 1]); else ScrollWithBar(s, 1, shown - 1); }
            else if (pgUp) ScrollWithBar(s, -page, i);
            else if (pgDown) ScrollWithBar(s, page, i);
        }

        /// <summary>Show labels in place of addresses and bytes (the "Symbols" tick box)</summary>
        internal bool ShowSymbols = true;

        /// <summary>"LABEL:" - a local label ("MAINLOOP@SKIPPRINT") as "@SKIPPRINT:", as in the operands</summary>
        static string LabelText(string _label)
        {
            int at = _label.IndexOf('@');
            return (at > 0 ? _label.Substring(at) : _label) + ":";
        }

        void DrawDisassembly(MonitorSnapshot s)
        {
            ImGui.SameLine(0, S(16));
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Goto");
            ImGui.SameLine();
            DrawGotoBox(s, MonitorPlugin.TargetDisasm, DisasmBox);
            ImGui.SameLine();
            bool follow = Plugin.IsFollowingPC;
            ImGui.BeginDisabled(follow);
            if (ImGui.Button("PC")) Plugin.DisasmFollowPC();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) Tip(follow ? "Following PC" : "Follow PC again");
            ImGui.SameLine(0, S(16));
            ImGui.Checkbox("Symbols", ref ShowSymbols);
            if (ImGui.IsItemHovered()) Tip("Show each line's label instead of its address and bytes\n(hover a line for its address)");

            float line = ImGui.GetTextLineHeightWithSpacing();
            Plugin.SetDisasmLines((int)(ImGui.GetContentRegionAvail().Y / line) + 1);

            // mouse wheel scrolls by instructions
            if (ImGui.IsWindowHovered())
            {
                float wheel = ImGui.GetIO().MouseWheel;
                if (wheel != 0 && !ImGui.GetIO().KeyCtrl) Plugin.DisasmScrollBy(wheel > 0 ? -3 : 3);
            }

            // Symbols: [gutter][label][T-states][instruction] - the label on the instruction's own line
            // else:    [gutter][address][bytes][T-states][instruction], with labels on a line of their own
            bool sym = ShowSymbols;
            int textCol = sym ? 3 : 4;
            ImGuiTableFlags tf = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX;
            if (!ImGui.BeginTable(sym ? "##dissym" : "##dis", sym ? 4 : 5, tf)) return;
            ImGui.TableSetupColumn("bp", ImGuiTableColumnFlags.WidthFixed, S(14));
            if (sym)
            {
                // a fixed SYMBOL_CHARS wide column (longer labels are cropped), so the instructions always line up
                ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(new string('M', SYMBOL_CHARS)).X + 8);
            }
            else
            {
                ImGui.TableSetupColumn("addr", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("bytes", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("00 00 00 00 ").X);
            }
            // T-states, right aligned against the instruction ("17/10" when a branch taken/not taken differ)
            float tsWidth = ImGui.CalcTextSize("00/00").X;
            ImGui.TableSetupColumn("ts", ImGuiTableColumnFlags.WidthFixed, tsWidth);
            ImGui.TableSetupColumn("text", ImGuiTableColumnFlags.WidthStretch);

            UserBarKeys(s);

            float bottom = ImGui.GetWindowPos().Y + ImGui.GetWindowHeight() - ImGui.GetStyle().WindowPadding.Y;
            int fitted = 0;                 // instructions shown in full - tells the plugin where the bottom is
            foreach (DisasmLine l in s.Disasm)
            {
                if (ImGui.GetCursorScreenPos().Y > bottom) break;
                if (l.Label != null && !sym)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(textCol);
                    ImGui.PushStyleColor(ImGuiCol.Text, ColLabel);
                    ImGui.TextUnformatted(LabelText(l.Label));
                    ImGui.PopStyleColor();
                }
                // the 64K address of this line (physical lines only have one while their bank is paged in)
                int logical = l.Physical ? s.ToLogical(l.Address) : l.Address;
                string id = (l.Physical ? "p" : "l") + l.Address;
                ImGui.TableNextRow();
                // breakpoints are a red bar; PC sitting on one shows the PC bar with a red dot in the gutter
                bool atPC = s.Stopped && logical == s.Regs.PC;
                if (atPC) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ColPCRow));
                else if (l.Breakpoint) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(l.PhysicalBreakpoint ? ColPhysBreakpointRow : ColBreakpointRow));
                if (l.Address == BarAddress && l.Physical == BarPhysical) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.GetColorU32(ColUserBar));

                ImGui.TableSetColumnIndex(1);
                string where = MonitorAddress.Format(l.Address, l.Physical);
                if (sym) ImGui.PushStyleColor(ImGuiCol.Text, ColLabel);
                if (ImGui.Selectable((sym ? (l.Label != null ? Crop(LabelText(l.Label), SYMBOL_CHARS) : "") : where) + "##dl" + id, false,
                                 ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap | ImGuiSelectableFlags.AllowDoubleClick))
                    PutBar(l);                      // click a line: the user bar goes there
                if (sym) ImGui.PopStyleColor();
                // double click a line to set/clear a breakpoint on it
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) Plugin.ToggleLineBreakpoint(l.Address, l.Physical);
                if (ImGui.BeginPopupContextItem("##dctx" + id))
                {
                    TextDim(where + OtherForm(s, l.Address, l.Physical));
                    int bpPhys = l.Physical ? l.Address : s.ToPhysical(l.Address);
                    string bpWhere = bpPhys >= 0 ? " (" + MonitorAddress.Format(bpPhys, true) + ")" : "";
                    if (ImGui.MenuItem((l.Breakpoint ? "Clear breakpoint" : "Set breakpoint") + bpWhere)) Plugin.ToggleLineBreakpoint(l.Address, l.Physical);
                    if (logical >= 0 && ImGui.MenuItem("Set/clear 64K breakpoint ($" + logical.ToString("X4") + ", any bank)")) Plugin.ToggleBreakpoint(logical, false);
                    if (ImGui.MenuItem("Run to here", null, false, s.Stopped && logical >= 0)) Plugin.RunTo(logical);
                    if (ImGui.MenuItem("Set PC here", null, false, s.Stopped && logical >= 0)) { int pcTo = logical; Plugin.SetRegisters(r => r.PC = (ushort)pcTo); }
                    if (ImGui.MenuItem("Show in memory")) ShowInMemory(l.Address, l.Physical);
                    ImGui.EndPopup();
                }
                if (ImGui.IsItemHovered() && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId))
                {
                    string t = l.TStates2 > 0 ? string.Format("{0}/{1} T-states", l.TStates1, l.TStates2) : string.Format("{0} T-states", l.TStates1);
                    Tip((l.Label != null ? l.Label + "\n" : "") + where + OtherForm(s, l.Address, l.Physical) + "\n" + t);
                }

                // gutter: breakpoint marker, click to toggle (after the row selectable, so it gets the click)
                ImGui.TableSetColumnIndex(0);
                Vector2 p = ImGui.GetCursorScreenPos();
                if (ImGui.InvisibleButton("##bp" + id, new Vector2(S(14), ImGui.GetTextLineHeight()))) Plugin.ToggleLineBreakpoint(l.Address, l.Physical);
                bool onBar = l.Address == BarAddress && l.Physical == BarPhysical;
                if (l.Breakpoint && (atPC || onBar))            // the PC bar or user bar hides the breakpoint's red
                    ImGui.GetWindowDrawList().AddCircleFilled(p + new Vector2(S(7), ImGui.GetTextLineHeight() * 0.5f), S(5), ImGui.GetColorU32(l.PhysicalBreakpoint ? ColPhysBreakpoint : ColBreakpoint));
                else if (!l.Breakpoint && ImGui.IsItemHovered())
                    ImGui.GetWindowDrawList().AddCircle(p + new Vector2(S(7), ImGui.GetTextLineHeight() * 0.5f), S(5), ImGui.GetColorU32(ColBreakpoint));

                if (!sym)
                {
                    ImGui.TableSetColumnIndex(2);
                    var sb = new StringBuilder();
                    for (int b = 0; b < l.Bytes.Length && b < 4; b++) sb.Append(l.Bytes[b].ToString("X2")).Append(' ');
                    TextCol(ColDim, sb.ToString());
                }

                ImGui.TableSetColumnIndex(textCol - 1);
                if (l.TStates1 > 0)
                {
                    string ts = l.TStates2 > 0 ? l.TStates1 + "/" + l.TStates2 : l.TStates1.ToString();
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + tsWidth - ImGui.CalcTextSize(ts).X);
                    TextCol(ColTStates, ts);
                }

                ImGui.TableSetColumnIndex(textCol);
                DrawInstruction(l.Text);
                if (ImGui.GetItemRectMax().Y <= bottom) fitted++;
            }
            ImGui.EndTable();
            Plugin.SetDisasmVisible(fitted);
            LastFitted = Math.Max(1, fitted);
        }

        /// <summary>Z80 register and condition names - everything else in an operand that's a word is a label</summary>
        static readonly System.Collections.Generic.HashSet<string> RegisterNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "A", "B", "C", "D", "E", "H", "L", "I", "R", "F", "AF", "AF'", "BC", "DE", "HL", "SP", "PC", "IX", "IY",
            "IXH", "IXL", "IYH", "IYL", "XH", "XL", "YH", "YL", "HX", "LX", "HY", "LY",
            "NZ", "Z", "NC", "PO", "PE", "P", "M"
        };

        // ****************************************************************************************
        /// <summary>
        ///     Draw an instruction: mnemonic and registers white, labels light blue, $hex values yellow
        /// </summary>
        // ****************************************************************************************
        static void DrawInstruction(string _text)
        {
            var parts = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Vector4>>();
            Vector4 white = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
            int i = 0, n = _text.Length;
            bool mnemonic = true;
            while (i < n)
            {
                char c = _text[i];
                int start = i;
                Vector4 col = white;
                if (c == '$' && i + 1 < n && Uri.IsHexDigit(_text[i + 1]))
                {
                    i++;
                    while (i < n && Uri.IsHexDigit(_text[i])) i++;
                    col = ColHex;
                }
                else if (char.IsLetter(c) || c == '_' || c == '.' || c == '@' || c == '!')
                {
                    while (i < n && (char.IsLetterOrDigit(_text[i]) || _text[i] == '_' || _text[i] == '.' || _text[i] == '@' || _text[i] == '!')) i++;
                    if (i < n && _text[i] == '\'' && _text.Substring(start, i - start).Equals("AF", StringComparison.OrdinalIgnoreCase)) i++;      // AF'
                    string word = _text.Substring(start, i - start);
                    if (mnemonic) mnemonic = false;                                     // first word: the instruction
                    else if (!RegisterNames.Contains(word)) col = ColLabel;             // an operand that isn't a register: a label
                }
                else
                {
                    while (i < n && !(char.IsLetter(_text[i]) || _text[i] == '_' || _text[i] == '.' || _text[i] == '@' || _text[i] == '!' ||
                                      (_text[i] == '$' && i + 1 < n && Uri.IsHexDigit(_text[i + 1])))) i++;
                }
                // merge runs of the same colour
                if (parts.Count > 0 && parts[parts.Count - 1].Value == col)
                    parts[parts.Count - 1] = new System.Collections.Generic.KeyValuePair<string, Vector4>(parts[parts.Count - 1].Key + _text.Substring(start, i - start), col);
                else parts.Add(new System.Collections.Generic.KeyValuePair<string, Vector4>(_text.Substring(start, i - start), col));
            }
            if (parts.Count == 0) { ImGui.TextUnformatted(""); return; }
            ImGui.BeginGroup();
            for (int p = 0; p < parts.Count; p++)
            {
                if (p > 0) ImGui.SameLine(0, S(0));
                ImGui.PushStyleColor(ImGuiCol.Text, parts[p].Value);
                ImGui.TextUnformatted(parts[p].Key);
                ImGui.PopStyleColor();
            }
            ImGui.EndGroup();
        }

        // ****************************************************************************************
        //  Registers: pairs and shadows, specials, flags, MMU. Double click a value to edit it
        //  (while stopped); click a flag to toggle it. Changes since the last step are highlighted.
        // ****************************************************************************************
        void DrawRegisters(MonitorSnapshot s)
        {
            Z80Regs r = s.Regs, p = PrevRegs ?? s.Regs;
            if (!ImGui.BeginTable("##regs", 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX)) return;
            ImGui.TableSetupColumn("n1", ImGuiTableColumnFlags.WidthFixed, S(34));
            ImGui.TableSetupColumn("v1", ImGuiTableColumnFlags.WidthFixed, S(70));
            ImGui.TableSetupColumn("n2", ImGuiTableColumnFlags.WidthFixed, S(34));
            ImGui.TableSetupColumn("v2", ImGuiTableColumnFlags.WidthFixed, S(70));

            RegRow(s, "AF", r.AF, p.AF, "AF'", r._AF, p._AF);
            RegRow(s, "BC", r.BC, p.BC, "BC'", r._BC, p._BC);
            RegRow(s, "DE", r.DE, p.DE, "DE'", r._DE, p._DE);
            RegRow(s, "HL", r.HL, p.HL, "HL'", r._HL, p._HL);
            RegRow(s, "IX", r.IX, p.IX, "IY", r.IY, p.IY);
            RegRow(s, "SP", r.SP, p.SP, "PC", r.PC, p.PC);
            ImGui.TableNextRow();
            Reg8(s, "I", r.I, p.I);
            Reg8(s, "R", r.R, p.R);
            ImGui.EndTable();

            ImGui.TextUnformatted("IM " + r.IM + "   IFF1 " + (r.IFF1 ? "1" : "0") + "   IFF2 " + (r.IFF2 ? "1" : "0"));

            // flags: S Z 5 H 3 P/V N C - click to toggle
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Flags");
            string[] names = { "C", "N", "P", "3", "H", "5", "Z", "S" };      // indexed by bit
            int f = r.AF & 0xff, pf = p.AF & 0xff;
            for (int bit = 7; bit >= 0; bit--)
            {
                ImGui.SameLine(0, S(bit == 7 ? 10 : 3));
                bool on = (f & (1 << bit)) != 0;
                bool changed = ((f ^ pf) & (1 << bit)) != 0;
                // set flags are lit buttons, clear ones dim text; red text = changed by the last step
                ImGui.PushStyleColor(ImGuiCol.Button, on ? ColFlagOn : ColFlagOff);
                ImGui.PushStyleColor(ImGuiCol.Text, changed ? ColChanged : (on ? ColNormal : ColDim));
                ImGui.BeginDisabled(!s.Stopped);
                int mask = 1 << bit;
                if (ImGui.Button(names[bit] + "##f" + bit, new Vector2(S(20), 0))) Plugin.SetRegisters(x => x.AF = (ushort)(x.AF ^ mask));
                ImGui.EndDisabled();
                ImGui.PopStyleColor(2);
            }

            ImGui.Spacing();
            TextCol(ColDim, "MMU");
            for (int i = 0; i < 8; i++)
            {
                ImGui.SameLine(0, S(i == 0 ? 10 : 6));
                ImGui.TextUnformatted(s.Mmu[i] == 0xff ? "ROM" : s.Mmu[i].ToString("X2"));
                if (ImGui.IsItemHovered())
                    Tip(string.Format("MMU{0} ${1:X4}-${2:X4}: {3}", i, i * 0x2000, i * 0x2000 + 0x1fff,
                        s.Mmu[i] == 0xff ? "ROM" : "8K bank " + s.Mmu[i] + " (16K bank " + (s.Mmu[i] >> 1) + ")"));
            }
            TextCol(ColDim, s.Stopped ? "Double click a value to edit it" : "Break to edit registers");
        }

        void RegRow(MonitorSnapshot s, string _n1, int _v1, int _p1, string _n2, int _v2, int _p2)
        {
            ImGui.TableNextRow();
            Reg16(s, _n1, _v1, _p1);
            Reg16(s, _n2, _v2, _p2);
        }

        void Reg16(MonitorSnapshot s, string _name, int _value, int _prev) { RegCell(s, _name, _value, _prev, 4); }
        void Reg8(MonitorSnapshot s, string _name, int _value, int _prev) { RegCell(s, _name, _value, _prev, 2); }

        void RegCell(MonitorSnapshot s, string _name, int _value, int _prev, int _digits)
        {
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            TextCol(ColDim, _name);
            ImGui.TableNextColumn();
            if (RegEditName == _name && s.Stopped)
            {
                ImGui.SetKeyboardFocusHere();
                if (HexInput("##edit" + _name, ref RegEditText, 60))
                {
                    int v;
                    if (ParseHex(RegEditText, out v)) { string n = _name; Plugin.SetRegisters(r => SetReg(r, n, v)); }
                    RegEditName = null;
                }
                else if (ImGui.IsItemDeactivated()) RegEditName = null;
                return;
            }
            string text = "$" + _value.ToString(_digits == 4 ? "X4" : "X2");
            if (_value != _prev) TextCol(ColChanged, text); else ImGui.TextUnformatted(text);
            if (ImGui.IsItemHovered())
            {
                string label = _digits == 4 ? LabelForTooltip(s, _value) : null;
                Tip(string.Format("{0} = ${1:X} = {2}{3}", _name, _value, _value, label != null ? "\n" + label : ""));
                if (s.Stopped && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { RegEditName = _name; RegEditText = _value.ToString(_digits == 4 ? "X4" : "X2"); }
            }
        }

        static string LabelForTooltip(MonitorSnapshot s, int _value) { return LabelAt(s, _value); }

        static void SetReg(Z80Regs r, string _name, int v)
        {
            ushort w = (ushort)(v & 0xffff);
            switch (_name)
            {
                case "AF": r.AF = w; break; case "BC": r.BC = w; break; case "DE": r.DE = w; break; case "HL": r.HL = w; break;
                case "AF'": r._AF = w; break; case "BC'": r._BC = w; break; case "DE'": r._DE = w; break; case "HL'": r._HL = w; break;
                case "IX": r.IX = w; break; case "IY": r.IY = w; break; case "SP": r.SP = w; break; case "PC": r.PC = w; break;
                case "I": r.I = (byte)v; break; case "R": r.R = (byte)v; break;
            }
        }

        // ****************************************************************************************
        //  Stack: words from SP (a few below too), with labels for return addresses
        // ****************************************************************************************
        void DrawStack(MonitorSnapshot s)
        {
            int sp = s.Regs.SP;
            if (!ImGui.BeginTable("##stack", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg)) return;
            ImGui.TableSetupColumn("addr", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("value", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthStretch);
            int first = sp - MonitorSnapshot.StackBelow * 2;
            for (int i = 0; i < s.StackLabels.Length; i++)
            {
                int at = (first + i * 2) & 0xffff;
                int word = s.Memory[at] | (s.Memory[(at + 1) & 0xffff] << 8);
                ImGui.TableNextRow();
                if (at == sp) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ColSP));
                ImGui.TableNextColumn();
                int off = at - sp; if (off > 0x8000) off -= 0x10000; if (off < -0x8000) off += 0x10000;
                TextCol(ColDim, (off == 0 ? "SP  " : (off > 0 ? "+" : "-") + Math.Abs(off).ToString("X2") + " ") + "$" + at.ToString("X4"));
                ImGui.TableNextColumn();
                bool changed = PrevMemory != null && (PrevMemory[at] != s.Memory[at] || PrevMemory[(at + 1) & 0xffff] != s.Memory[(at + 1) & 0xffff]);
                string v = "$" + word.ToString("X4");
                if (changed) TextCol(ColChanged, v); else ImGui.TextUnformatted(v);
                ImGui.TableNextColumn();
                if (s.StackLabels[i] != null) TextCol(ColLabel, s.StackLabels[i]);
            }
            ImGui.EndTable();
            if (s.StopCount != stackScrolledFor) { stackScrolledFor = s.StopCount; ImGui.SetScrollY(0); }
        }
        int stackScrolledFor = -1;

        // ****************************************************************************************
        //  Memory: 16 bytes a row + ASCII. Goto, follow a register, double click a byte to edit it.
        //  Bytes changed since the last step are highlighted.
        // ****************************************************************************************
        void DrawMemory(MonitorSnapshot s)
        {
            bool any = false;
            if (ImGui.BeginTabBar("##memtabs", ImGuiTabBarFlags.None))
            {
                for (int i = 0; i < MEM_VIEWS; i++)
                {
                    MemView m = Mems[i];
                    if (!m.Enabled) continue;
                    any = true;
                    bool open = true;
                    ImGuiTabItemFlags tf = SelectMem == i ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                    if (ImGui.BeginTabItem("Memory " + (i + 1) + "##mem" + i, ref open, tf))
                    {
                        if (ActiveMem != i) { ActiveMem = i; MemEditAddress = -1; }
                        DrawMemoryView(s, m, i);
                        ImGui.EndTabItem();
                    }
                    if (!open) m.Enabled = false;       // closed with its x (the Memory menu brings it back)
                }
                SelectMem = -1;
                ImGui.EndTabBar();
            }
            if (!any)
            {
                TextDim("No memory windows open - turn one on from the Memory menu");
                Plugin.SetPhysicalWindow(0, 0);
            }
        }

        /// <summary>The memory window to send things to ("Show in memory", a memory Goto): the one on show, else turn on Memory 1</summary>
        MemView CurrentMem()
        {
            if (!Mems[ActiveMem].Enabled)
            {
                int i = Array.FindIndex(Mems, m => m.Enabled);
                if (i < 0) { i = 0; Mems[0].Enabled = true; }
                ActiveMem = i; SelectMem = i;
            }
            return Mems[ActiveMem];
        }

        void ShowInMemory(int _address, bool _physical)
        {
            MemView m = CurrentMem();
            m.Physical = _physical; m.ScrollTo = _address; m.Follow = 0;
        }

        void DrawMemoryView(MonitorSnapshot s, MemView m, int _index)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Goto");
            ImGui.SameLine();
            DrawGotoBox(s, MonitorPlugin.TargetMemory, m.Box);
            ImGui.SameLine(0, S(16));
            int space = m.Physical ? 1 : 0;
            ImGui.SetNextItemWidth(S(90));
            if (ImGui.Combo("##memspace" + _index, ref space, SpaceNames, SpaceNames.Length) && (space == 1) != m.Physical)
            {
                // switch view, staying on the same memory if it's paged in
                int other = m.Physical ? s.ToLogical(m.TopAddress) : s.ToPhysical(m.TopAddress & 0xffff);
                m.Physical = space == 1;
                m.ScrollTo = Math.Max(0, other);
                if (m.Physical) m.Follow = 0;
            }
            if (ImGui.IsItemHovered()) Tip("64K: what the CPU sees through the MMU\nPhysical: all 2MB, addressed as bank:offset (8K banks)");
            ImGui.SameLine(0, S(16));
            ImGui.TextUnformatted("Follow");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(S(70));
            if (ImGui.Combo("##memfollow" + _index, ref m.Follow, FollowNames, FollowNames.Length)) { m.FollowLast = -1; if (m.Follow != 0) m.Physical = false; }

            if (m.Follow != 0)
            {
                int target = FollowValue(s.Regs, m.Follow);
                if (target != m.FollowLast) { m.ScrollTo = target; m.FollowLast = target; }
            }

            if (!ImGui.BeginChild("##memrows" + _index, Vector2.Zero, ImGuiChildFlags.None, WheelFlags())) { ImGui.EndChild(); return; }
            float lineH = ImGui.GetTextLineHeightWithSpacing();
            if (m.ScrollTo >= 0)
            {
                ImGui.SetScrollY((m.ScrollTo >> 4) * lineH);
                m.ScrollTo = -1;
            }

            bool phys = m.Physical;
            int rows = phys ? MonitorAddress.PhysicalSize / 16 : 4096;
            float charW = ImGui.CalcTextSize("0").X;
            ImGuiListClipperPtr clipper;
            unsafe { clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper()); }
            clipper.Begin(rows, lineH);
            int firstRow = int.MaxValue, lastRow = 0;
            while (clipper.Step())
            {
                for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                {
                    firstRow = Math.Min(firstRow, row); lastRow = Math.Max(lastRow, row);
                    int a = row << 4;
                    // address: $XXXX in the 64K view, bank:$offset for physical memory
                    TextCol(ColDim, phys ? MonitorAddress.Format(a, true).PadLeft(9) : a.ToString("X4"));
                    ImGui.SameLine(0, charW * 2);
                    var ascii = new StringBuilder(16);
                    for (int i = 0; i < 16; i++)
                    {
                        int at = a + i;
                        if (i == 8) ImGui.SameLine(0, charW); else if (i > 0) ImGui.SameLine(0, charW);
                        int v = MemByte(s, at, phys);
                        ascii.Append(v >= 32 && v < 127 ? (char)v : '.');
                        if (MemEditAddress == at && MemEditPhysical == phys && s.Stopped)
                        {
                            ImGui.SetKeyboardFocusHere();
                            if (HexInput("##memedit", ref MemEditText, charW * 3))
                            {
                                int nv; if (ParseHex(MemEditText, out nv)) Plugin.Poke(at, phys, (byte)nv);
                                MemEditAddress = -1;
                            }
                            else if (ImGui.IsItemDeactivated()) MemEditAddress = -1;
                            continue;
                        }
                        if (v < 0) { TextCol(ColDim, "--"); continue; }        // physical bytes not read yet
                        int was = PrevByte(at, phys);
                        bool changed = was >= 0 && was != v;
                        bool pc = !phys && at == s.Regs.PC, sp = !phys && (at == s.Regs.SP || at == ((s.Regs.SP + 1) & 0xffff));
                        if (changed) ImGui.PushStyleColor(ImGuiCol.Text, ColChanged);
                        Vector2 cp = ImGui.GetCursorScreenPos();
                        if (pc || sp) ImGui.GetWindowDrawList().AddRectFilled(cp, cp + new Vector2(charW * 2, ImGui.GetTextLineHeight()), ImGui.GetColorU32(pc ? ColPCRow : ColSP));
                        ImGui.TextUnformatted(v.ToString("X2"));
                        if (changed) ImGui.PopStyleColor();
                        if (ImGui.IsItemHovered())
                        {
                            Tip(string.Format("{0}{1}: ${2:X2} = {2}{3}", MonitorAddress.Format(at, phys), OtherForm(s, at, phys), v,
                                (v >= 32 && v < 127) ? "  '" + (char)v + "'" : ""));
                            if (s.Stopped && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { MemEditAddress = at; MemEditPhysical = phys; MemEditText = v.ToString("X2"); }
                        }
                    }
                    ImGui.SameLine(0, charW * 2);
                    TextCol(ColDim, ascii.ToString());
                }
            }
            clipper.End();
            clipper.Destroy();
            if (firstRow != int.MaxValue) m.TopAddress = firstRow << 4;

            // physical memory is read on demand: the rows on screen plus a screen's worth either side for scrolling
            if (phys && firstRow != int.MaxValue)
            {
                int pad = lastRow - firstRow + 1;
                int from = Math.Max(0, firstRow - pad);
                int to = Math.Min(rows, lastRow + 1 + pad);
                Plugin.SetPhysicalWindow(from << 4, (to - from) << 4);
            }
            else Plugin.SetPhysicalWindow(0, 0);
            ImGui.EndChild();
        }

        /// <summary>A byte as last captured, or -1 if not read</summary>
        static int MemByte(MonitorSnapshot s, int _address, bool _physical)
        {
            if (!_physical) return s.Memory[_address & 0xffff];
            if (s.PhysMemory == null) return -1;
            int i = _address - s.PhysStart;
            return (i >= 0 && i < s.PhysMemory.Length) ? s.PhysMemory[i] : -1;
        }

        /// <summary>The same byte before the last step/stop, or -1</summary>
        int PrevByte(int _address, bool _physical)
        {
            if (!_physical) return PrevMemory == null ? -1 : PrevMemory[_address & 0xffff];
            if (PrevPhys == null) return -1;
            int i = _address - PrevPhysStart;
            return (i >= 0 && i < PrevPhys.Length) ? PrevPhys[i] : -1;
        }

        /// <summary>" (10:$0000)" for a paged-in 64K address, " ($C000)" for a paged-in physical one</summary>
        static string OtherForm(MonitorSnapshot s, int _address, bool _physical)
        {
            if (_physical) { int l = s.ToLogical(_address); return l >= 0 ? " ($" + l.ToString("X4") + ")" : ""; }
            int p = s.ToPhysical(_address);
            return p >= 0 ? " (" + MonitorAddress.Format(p, true) + ")" : " (ROM)";
        }

        // ****************************************************************************************
        //  Goto box: $hex, decimal, bank:offset, or a symbol (name, name+offset, name-offset).
        //  Typing a name lists matching symbols underneath (click one, or arrow keys + Enter).
        //  Numbers are handled here; symbols are resolved by the plugin on the emulator thread.
        // ****************************************************************************************
        void DrawGotoBox(MonitorSnapshot s, int _target, GotoBox b)
        {
            ImGui.SetNextItemWidth(S(130));
            bool enter = ImGui.InputText("##goto" + _target, ref b.Text, 64, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.CharsNoBlank);
            // Enter deactivates the box in the same frame - it still counts as typing in it here
            bool active = ImGui.IsItemActive() || enter;
            if (ImGui.IsItemEdited()) { b.Selected = -1; b.Error = null; }
            if (ImGui.IsItemHovered() && !active)
                Tip("$8000 hex, 32768 decimal, bank:offset for physical memory (8K banks, e.g. 10:$0000),\nor a symbol: main_loop, buffer+$20, table-2");
            Vector2 boxMin = ImGui.GetItemRectMin(), boxMax = ImGui.GetItemRectMax();

            string name; int offset;
            bool symbolish = MonitorAddress.TrySplitSymbol(b.Text, out name, out offset);
            // keep the list while the mouse is over it (it was there last frame) - clicking it takes focus off the box
            if (b.ListHovered) b.ListHovered = ImGui.IsMouseHoveringRect(b.ListMin, b.ListMax, false);
            bool wanted = (active || b.ListHovered) && symbolish;
            Plugin.SetSymbolQuery(_target, wanted ? b.Text : null);
            SymbolMatch[] matches = (wanted && s.SymbolQuery[_target] == b.Text) ? s.SymbolMatches[_target] : null;
            bool open = matches != null && matches.Length > 0;

            if (open && active)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) b.Selected = Math.Min(b.Selected + 1, matches.Length - 1);
                if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) b.Selected = Math.Max(b.Selected - 1, -1);
            }
            if (enter)
            {
                if (open && b.Selected >= 0 && b.Selected < matches.Length) PickSymbol(_target, b, matches[b.Selected], name);
                else Go(_target, b);
                b.Selected = -1;
            }

            // a symbol Goto came back from the emulator thread
            GotoResult r = Plugin.LastGoto(_target);
            if (r != null && r.Sequence != SeenGoto[_target])
            {
                SeenGoto[_target] = r.Sequence;
                if (r.Error != null) { b.Error = r.Error; b.ErrorTime = ImGui.GetTime(); }
                else { b.Error = null; ShowAddress(_target, r.Address, r.Physical, true); }
            }

            if (open)
            {
                // below the box, or above it if there's more room there (the memory box is near the bottom)
                float lineH = ImGui.GetTextLineHeightWithSpacing();
                float wantH = matches.Length * lineH + ImGui.GetStyle().WindowPadding.Y * 2;
                float display = ImGui.GetIO().DisplaySize.Y;
                float below = display - boxMax.Y - 4, above = boxMin.Y - 4;
                bool up = wantH > below && above > below;
                if (up) ImGui.SetNextWindowPos(new Vector2(boxMin.X, boxMin.Y - 2), ImGuiCond.Always, new Vector2(0, 1));
                else ImGui.SetNextWindowPos(new Vector2(boxMin.X, boxMax.Y + 2));
                ImGui.SetNextWindowSizeConstraints(Vector2.Zero, new Vector2(float.MaxValue, Math.Max(lineH * 3, up ? above : below)));
                ImGui.SetNextWindowBgAlpha(1.0f);
                ImGuiWindowFlags lf = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings |
                                      ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;
                bool keep = false;
                if (ImGui.Begin("##symbols" + _target, lf))
                {
                    Vector2 wp = ImGui.GetWindowPos();
                    b.ListMin = wp; b.ListMax = wp + ImGui.GetWindowSize();
                    keep = ImGui.IsMouseHoveringRect(b.ListMin, b.ListMax, false);
                    // name column as wide as the longest name (up to a limit), then the address; the whole row is clickable
                    float nameW = S(120);
                    foreach (SymbolMatch m in matches) nameW = Math.Max(nameW, ImGui.CalcTextSize(m.Name).X);
                    nameW = Math.Min(nameW, S(360));
                    float rowW = nameW + S(16) + ImGui.CalcTextSize("255:$1FFF").X;
                    for (int i = 0; i < matches.Length; i++)
                    {
                        SymbolMatch m = matches[i];
                        bool picked = ImGui.Selectable("##sym" + i, i == b.Selected, ImGuiSelectableFlags.AllowOverlap, new Vector2(rowW, 0));
                        // take the click on the press, by position: the text box is still active then and can stop
                        // the list seeing it as hovered
                        Vector2 rmin = ImGui.GetItemRectMin(), rmax = ImGui.GetItemRectMax();
                        if (!picked && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsMouseHoveringRect(rmin, rmax, false)) picked = true;
                        ImGui.SameLine(ImGui.GetStyle().WindowPadding.X);
                        ImGui.TextUnformatted(m.Name);
                        ImGui.SameLine(ImGui.GetStyle().WindowPadding.X + nameW + S(16));
                        TextCol(ColDim, MonitorAddress.Format(m.Address, m.Physical));
                        if (picked)
                        {
                            PickSymbol(_target, b, m, name);
                            keep = false;
                            break;
                        }
                    }
                }
                ImGui.End();
                b.ListHovered = keep;
            }
            else b.ListHovered = false;

            if (b.Error != null && ImGui.GetTime() - b.ErrorTime < 4.0)
            {
                ImGui.SetNextWindowPos(new Vector2(boxMin.X, boxMax.Y + 2));
                ImGui.SetNextWindowBgAlpha(1.0f);
                if (ImGui.Begin("##gotoerr" + _target, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings |
                                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoInputs))
                    TextCol(ColError, b.Error);
                ImGui.End();
            }
        }

        /// <summary>Goto whatever was typed: numbers right away, symbols via the plugin</summary>
        void Go(int _target, GotoBox _b)
        {
            int a; bool phys;
            if (MonitorAddress.TryParse(_b.Text, out a, out phys)) { _b.Error = null; ShowAddress(_target, a, phys, false); }
            else Plugin.GotoSymbol(_b.Text, _target);
        }

        /// <summary>A suggestion was chosen: keep any +/- offset that was typed after the name</summary>
        void PickSymbol(int _target, GotoBox _b, SymbolMatch _m, string _typedName)
        {
            string t = _b.Text.Trim();
            string suffix = (_typedName != null && t.Length > _typedName.Length) ? t.Substring(_typedName.Length) : "";
            string dummy; int offset;
            MonitorAddress.TrySplitSymbol(t, out dummy, out offset);
            _b.Text = _m.Name + suffix;
            _b.Error = null;
            int a = _m.Address + offset;
            if (_m.Physical) { a %= MonitorAddress.PhysicalSize; if (a < 0) a += MonitorAddress.PhysicalSize; } else a &= 0xffff;
            ShowAddress(_target, a, _m.Physical, false);
        }

        void ShowAddress(int _target, int _address, bool _physical, bool _pluginDidDisasm)
        {
            if (_target == MonitorPlugin.TargetDisasm) { if (!_pluginDidDisasm) Plugin.DisasmGoto(_address, _physical); }
            else ShowInMemory(_address, _physical);
        }

        static int FollowValue(Z80Regs r, int _which)
        {
            switch (_which)
            {
                case 1: return r.PC; case 2: return r.SP; case 3: return r.HL; case 4: return r.DE;
                case 5: return r.BC; case 6: return r.IX; case 7: return r.IY;
            }
            return 0;
        }
    }
}

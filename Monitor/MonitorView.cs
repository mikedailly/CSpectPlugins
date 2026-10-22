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
        static readonly Vector4 ColPCRow = new Vector4(0.55f, 0.45f, 0.05f, 1.0f);
        static readonly Vector4 ColBreakpoint = new Vector4(0.90f, 0.20f, 0.20f, 1.0f);
        static readonly Vector4 ColChanged = new Vector4(1.00f, 0.45f, 0.35f, 1.0f);
        static readonly Vector4 ColLabel = new Vector4(0.45f, 0.80f, 1.00f, 1.0f);
        static readonly Vector4 ColDim = new Vector4(0.55f, 0.58f, 0.65f, 1.0f);
        static readonly Vector4 ColSP = new Vector4(0.25f, 0.35f, 0.55f, 1.0f);
        static readonly Vector4 ColRunning = new Vector4(0.45f, 0.85f, 0.45f, 1.0f);
        static readonly Vector4 ColFlagOn = new Vector4(0.20f, 0.45f, 0.80f, 1.0f);
        static readonly Vector4 ColFlagOff = new Vector4(0.13f, 0.15f, 0.19f, 1.0f);
        static readonly Vector4 ColNormal = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        static readonly Vector4 ColStopped = new Vector4(1.00f, 0.75f, 0.30f, 1.0f);

        const float SPLIT = 6.0f;               // splitter bar thickness
        const float MIN_PANE = 60.0f;

        static readonly string[] FollowNames = { "None", "PC", "SP", "HL", "DE", "BC", "IX", "IY" };

        MonitorPlugin Plugin;

        // pane sizes, as fractions so they scale with the window
        internal float SplitX = 0.64f;          // left column width
        internal float SplitLeftY = 0.60f;      // disassembly / memory
        internal float SplitRightY = 0.42f;     // registers / stack

        // previous stop, for "changed" highlighting
        MonitorSnapshot Shown;
        Z80Regs PrevRegs;
        byte[] PrevMemory;
        int ShownStopCount = -1;

        string DisasmGotoText = "";
        string MemGotoText = "";
        int MemFollow = 0;
        int MemFollowLast = -1;
        int MemScrollTo = -1;
        int MemEditAddress = -1;
        string MemEditText = "";
        string RegEditName;
        string RegEditText = "";
        bool HideCSpectDebugger;

        public MonitorView(MonitorPlugin _plugin) { Plugin = _plugin; }

        // ****************************************************************************************
        //  iWindow ImGui draw callback
        // ****************************************************************************************
        public void Draw()
        {
            MonitorSnapshot s = Plugin.Snapshot;
            if (s != null && s.StopCount != ShownStopCount)
            {
                // the CPU moved on: remember what it looked like before, for highlighting changes
                if (Shown != null) { PrevRegs = Shown.Regs; PrevMemory = Shown.Memory; }
                ShownStopCount = s.StopCount;
            }
            Shown = s;

            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                                     ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoScrollbar;
            bool open = true;
            if (ImGui.Begin("##Monitor", ref open, flags))
            {
                DrawToolbar(s);
                ImGui.Separator();
                if (s == null) ImGui.TextDisabled("Waiting for the emulator...");
                else DrawPanes(s);
            }
            ImGui.End();
        }

        // ****************************************************************************************
        //  Run/Break, Step, Step Over, Back, breakpoints, status
        // ****************************************************************************************
        void DrawToolbar(MonitorSnapshot s)
        {
            bool stopped = s != null && s.Stopped;
            if (stopped) { if (ImGui.Button("Run", new Vector2(70, 0))) Plugin.Run(); }
            else { if (ImGui.Button("Break", new Vector2(70, 0))) Plugin.Break(); }
            ImGui.SameLine();
            ImGui.BeginDisabled(!stopped);
            if (ImGui.Button("Step", new Vector2(70, 0))) Plugin.Step();
            ImGui.SameLine();
            if (ImGui.Button("Step Over", new Vector2(80, 0))) Plugin.StepOver();
            ImGui.SameLine();
            if (ImGui.Button("Back", new Vector2(60, 0))) Plugin.StepBack();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Rewind the CPU one instruction");
            ImGui.EndDisabled();
            ImGui.SameLine(0, 20);
            if (ImGui.Button("Clear Breakpoints")) Plugin.ClearAllBreakpoints();
            ImGui.SameLine(0, 20);
            if (ImGui.Checkbox("Hide CSpect debugger", ref HideCSpectDebugger)) Plugin.SetRemote(HideCSpectDebugger);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("While stopped, show the game screen in the main window instead of CSpect's own debugger");

            if (s != null)
            {
                ImGui.SameLine(0, 20);
                if (s.Stopped)
                {
                    string label = LabelAt(s, s.Regs.PC);
                    ImGui.TextColored(ColStopped, "Stopped at $" + s.Regs.PC.ToString("X4") + (label != null ? "  " + label : ""));
                }
                else ImGui.TextColored(ColRunning, "Running");
            }
        }

        static string LabelAt(MonitorSnapshot s, int _address)
        {
            foreach (DisasmLine l in s.Disasm) if (l.Address == _address) return l.Label;
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

            float leftW = Clamp(W * SplitX, MIN_PANE, W - SPLIT - MIN_PANE);
            float rightW = W - leftW - SPLIT;
            float leftTop = Clamp(H * SplitLeftY, MIN_PANE, H - SPLIT - MIN_PANE);
            float rightTop = Clamp(H * SplitRightY, MIN_PANE, H - SPLIT - MIN_PANE);
            float rightX = origin.X + leftW + SPLIT;

            ImGui.SetCursorPos(origin);
            Pane("Disassembly", "##disasm", new Vector2(leftW, leftTop), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse, () => DrawDisassembly(s));
            Splitter("##splitLeft", new Vector2(origin.X, origin.Y + leftTop), new Vector2(leftW, SPLIT), false, ref SplitLeftY, H);
            ImGui.SetCursorPos(new Vector2(origin.X, origin.Y + leftTop + SPLIT));
            Pane("Memory", "##memory", new Vector2(leftW, H - leftTop - SPLIT), ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse, () => DrawMemory(s));

            Splitter("##splitX", new Vector2(origin.X + leftW, origin.Y), new Vector2(SPLIT, H), true, ref SplitX, W);

            ImGui.SetCursorPos(new Vector2(rightX, origin.Y));
            Pane("Registers", "##regs", new Vector2(rightW, rightTop), ImGuiWindowFlags.None, () => DrawRegisters(s));
            Splitter("##splitRight", new Vector2(rightX, origin.Y + rightTop), new Vector2(rightW, SPLIT), false, ref SplitRightY, H);
            ImGui.SetCursorPos(new Vector2(rightX, origin.Y + rightTop + SPLIT));
            Pane("Stack", "##stack", new Vector2(rightW, H - rightTop - SPLIT), ImGuiWindowFlags.None, () => DrawStack(s));
        }

        static float Clamp(float v, float lo, float hi) { return Math.Max(lo, Math.Min(hi, v)); }

        static void Pane(string _title, string _id, Vector2 _size, ImGuiWindowFlags _flags, Action _body)
        {
            if (ImGui.BeginChild(_id, _size, ImGuiChildFlags.Borders, _flags))
            {
                ImGui.TextColored(ColLabel, _title);
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
        void DrawDisassembly(MonitorSnapshot s)
        {
            ImGui.SameLine(0, 16);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Goto");
            ImGui.SameLine();
            if (HexInput("##disgoto", ref DisasmGotoText, 60)) { int a; if (ParseHex(DisasmGotoText, out a)) Plugin.DisasmGoto(a); }
            ImGui.SameLine();
            bool follow = Plugin.IsFollowingPC;
            ImGui.BeginDisabled(follow);
            if (ImGui.Button("PC")) Plugin.DisasmFollowPC();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(follow ? "Following PC" : "Follow PC again");

            float line = ImGui.GetTextLineHeightWithSpacing();
            Plugin.SetDisasmLines((int)(ImGui.GetContentRegionAvail().Y / line) + 1);

            // mouse wheel scrolls by instructions
            if (ImGui.IsWindowHovered())
            {
                float wheel = ImGui.GetIO().MouseWheel;
                if (wheel != 0) Plugin.DisasmScrollBy(wheel > 0 ? -3 : 3);
            }

            ImGuiTableFlags tf = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX;
            if (!ImGui.BeginTable("##dis", 4, tf)) return;
            ImGui.TableSetupColumn("bp", ImGuiTableColumnFlags.WidthFixed, 14);
            ImGui.TableSetupColumn("addr", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("bytes", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("00 00 00 00 ").X);
            ImGui.TableSetupColumn("text", ImGuiTableColumnFlags.WidthStretch);

            float bottom = ImGui.GetWindowPos().Y + ImGui.GetWindowHeight() - ImGui.GetStyle().WindowPadding.Y;
            int fitted = 0;                 // instructions shown in full - tells the plugin where the bottom is
            foreach (DisasmLine l in s.Disasm)
            {
                if (ImGui.GetCursorScreenPos().Y > bottom) break;
                if (l.Label != null)
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetColumnIndex(3);
                    ImGui.TextColored(ColLabel, l.Label + ":");
                }
                ImGui.TableNextRow();
                if (s.Stopped && l.Address == s.Regs.PC) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ColPCRow));

                ImGui.TableSetColumnIndex(1);
                ImGui.Selectable("$" + l.Address.ToString("X4") + "##dl" + l.Address, false, ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap);
                if (ImGui.BeginPopupContextItem("##dctx" + l.Address))
                {
                    ImGui.TextDisabled("$" + l.Address.ToString("X4"));
                    if (ImGui.MenuItem(l.Breakpoint ? "Clear breakpoint" : "Set breakpoint")) Plugin.ToggleBreakpoint(l.Address);
                    if (ImGui.MenuItem("Run to here", null, false, s.Stopped)) Plugin.RunTo(l.Address);
                    int addr = l.Address;
                    if (ImGui.MenuItem("Set PC here", null, false, s.Stopped)) Plugin.SetRegisters(r => r.PC = (ushort)addr);
                    if (ImGui.MenuItem("Show in memory")) MemScrollTo = l.Address;
                    ImGui.EndPopup();
                }
                if (ImGui.IsItemHovered() && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId))
                {
                    ImGui.SetTooltip(l.TStates2 > 0 ? string.Format("{0}/{1} T-states", l.TStates1, l.TStates2) : string.Format("{0} T-states", l.TStates1));
                }

                // gutter: breakpoint marker, click to toggle (after the row selectable, so it gets the click)
                ImGui.TableSetColumnIndex(0);
                Vector2 p = ImGui.GetCursorScreenPos();
                if (ImGui.InvisibleButton("##bp" + l.Address, new Vector2(14, ImGui.GetTextLineHeight()))) Plugin.ToggleBreakpoint(l.Address);
                if (l.Breakpoint)
                    ImGui.GetWindowDrawList().AddCircleFilled(p + new Vector2(7, ImGui.GetTextLineHeight() * 0.5f), 5, ImGui.GetColorU32(ColBreakpoint));
                else if (ImGui.IsItemHovered())
                    ImGui.GetWindowDrawList().AddCircle(p + new Vector2(7, ImGui.GetTextLineHeight() * 0.5f), 5, ImGui.GetColorU32(ColBreakpoint));

                ImGui.TableSetColumnIndex(2);
                var sb = new StringBuilder();
                for (int b = 0; b < l.Bytes.Length && b < 4; b++) sb.Append(l.Bytes[b].ToString("X2")).Append(' ');
                ImGui.TextColored(ColDim, sb.ToString());

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(l.Text);
                if (ImGui.GetItemRectMax().Y <= bottom) fitted++;
            }
            ImGui.EndTable();
            Plugin.SetDisasmVisible(fitted);
        }

        // ****************************************************************************************
        //  Registers: pairs and shadows, specials, flags, MMU. Double click a value to edit it
        //  (while stopped); click a flag to toggle it. Changes since the last step are highlighted.
        // ****************************************************************************************
        void DrawRegisters(MonitorSnapshot s)
        {
            Z80Regs r = s.Regs, p = PrevRegs ?? s.Regs;
            if (!ImGui.BeginTable("##regs", 4, ImGuiTableFlags.SizingFixedFit)) return;
            ImGui.TableSetupColumn("n1", ImGuiTableColumnFlags.WidthFixed, 34);
            ImGui.TableSetupColumn("v1", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("n2", ImGuiTableColumnFlags.WidthFixed, 34);
            ImGui.TableSetupColumn("v2", ImGuiTableColumnFlags.WidthFixed, 70);

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
                ImGui.SameLine(0, bit == 7 ? 10 : 3);
                bool on = (f & (1 << bit)) != 0;
                bool changed = ((f ^ pf) & (1 << bit)) != 0;
                // set flags are lit buttons, clear ones dim text; red text = changed by the last step
                ImGui.PushStyleColor(ImGuiCol.Button, on ? ColFlagOn : ColFlagOff);
                ImGui.PushStyleColor(ImGuiCol.Text, changed ? ColChanged : (on ? ColNormal : ColDim));
                ImGui.BeginDisabled(!s.Stopped);
                int mask = 1 << bit;
                if (ImGui.Button(names[bit] + "##f" + bit, new Vector2(20, 0))) Plugin.SetRegisters(x => x.AF = (ushort)(x.AF ^ mask));
                ImGui.EndDisabled();
                ImGui.PopStyleColor(2);
            }

            ImGui.Spacing();
            ImGui.TextColored(ColDim, "MMU");
            for (int i = 0; i < 8; i++)
            {
                ImGui.SameLine(0, i == 0 ? 10 : 6);
                ImGui.TextUnformatted(s.Mmu[i] == 0xff ? "ROM" : s.Mmu[i].ToString("X2"));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(string.Format("MMU{0} ${1:X4}-${2:X4}: {3}", i, i * 0x2000, i * 0x2000 + 0x1fff,
                        s.Mmu[i] == 0xff ? "ROM" : "8K bank " + s.Mmu[i] + " (16K bank " + (s.Mmu[i] >> 1) + ")"));
            }
            ImGui.TextColored(ColDim, s.Stopped ? "Double click a value to edit it" : "Break to edit registers");
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
            ImGui.TextColored(ColDim, _name);
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
            if (_value != _prev) ImGui.TextColored(ColChanged, text); else ImGui.TextUnformatted(text);
            if (ImGui.IsItemHovered())
            {
                string label = _digits == 4 ? LabelForTooltip(s, _value) : null;
                ImGui.SetTooltip(string.Format("{0} = ${1:X} = {2}{3}", _name, _value, _value, label != null ? "\n" + label : ""));
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
            if (!ImGui.BeginTable("##stack", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg)) return;
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
                ImGui.TextColored(ColDim, (off == 0 ? "SP  " : (off > 0 ? "+" : "-") + Math.Abs(off).ToString("X2") + " ") + "$" + at.ToString("X4"));
                ImGui.TableNextColumn();
                bool changed = PrevMemory != null && (PrevMemory[at] != s.Memory[at] || PrevMemory[(at + 1) & 0xffff] != s.Memory[(at + 1) & 0xffff]);
                string v = "$" + word.ToString("X4");
                if (changed) ImGui.TextColored(ColChanged, v); else ImGui.TextUnformatted(v);
                ImGui.TableNextColumn();
                if (s.StackLabels[i] != null) ImGui.TextColored(ColLabel, s.StackLabels[i]);
            }
            if (s.StopCount != stackScrolledFor) { stackScrolledFor = s.StopCount; ImGui.SetScrollY(0); }
            ImGui.EndTable();
        }
        int stackScrolledFor = -1;

        // ****************************************************************************************
        //  Memory: 16 bytes a row + ASCII. Goto, follow a register, double click a byte to edit it.
        //  Bytes changed since the last step are highlighted.
        // ****************************************************************************************
        void DrawMemory(MonitorSnapshot s)
        {
            ImGui.SameLine(0, 16);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Goto");
            ImGui.SameLine();
            if (HexInput("##memgoto", ref MemGotoText, 60)) { int a; if (ParseHex(MemGotoText, out a)) { MemScrollTo = a & 0xffff; MemFollow = 0; } }
            ImGui.SameLine(0, 16);
            ImGui.TextUnformatted("Follow");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(70);
            if (ImGui.Combo("##memfollow", ref MemFollow, FollowNames, FollowNames.Length)) MemFollowLast = -1;

            if (MemFollow != 0)
            {
                int target = FollowValue(s.Regs, MemFollow);
                if (target != MemFollowLast) { MemScrollTo = target; MemFollowLast = target; }
            }

            if (!ImGui.BeginChild("##memrows", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.None)) { ImGui.EndChild(); return; }
            float lineH = ImGui.GetTextLineHeightWithSpacing();
            if (MemScrollTo >= 0)
            {
                ImGui.SetScrollY((MemScrollTo >> 4) * lineH);
                MemScrollTo = -1;
            }

            float charW = ImGui.CalcTextSize("0").X;
            ImGuiListClipperPtr clipper;
            unsafe { clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper()); }
            clipper.Begin(4096, lineH);
            byte[] m = s.Memory;
            while (clipper.Step())
            {
                for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                {
                    int a = row << 4;
                    ImGui.TextColored(ColDim, a.ToString("X4"));
                    ImGui.SameLine(0, charW * 2);
                    for (int i = 0; i < 16; i++)
                    {
                        int at = a + i;
                        if (i == 8) ImGui.SameLine(0, charW); else if (i > 0) ImGui.SameLine(0, charW);
                        if (MemEditAddress == at && s.Stopped)
                        {
                            ImGui.SetKeyboardFocusHere();
                            if (HexInput("##memedit", ref MemEditText, charW * 3))
                            {
                                int v; if (ParseHex(MemEditText, out v)) Plugin.Poke(at, (byte)v);
                                MemEditAddress = -1;
                            }
                            else if (ImGui.IsItemDeactivated()) MemEditAddress = -1;
                            continue;
                        }
                        bool changed = PrevMemory != null && PrevMemory[at] != m[at];
                        bool pc = at == s.Regs.PC, sp = at == s.Regs.SP || at == ((s.Regs.SP + 1) & 0xffff);
                        if (changed) ImGui.PushStyleColor(ImGuiCol.Text, ColChanged);
                        Vector2 cp = ImGui.GetCursorScreenPos();
                        if (pc || sp) ImGui.GetWindowDrawList().AddRectFilled(cp, cp + new Vector2(charW * 2, ImGui.GetTextLineHeight()), ImGui.GetColorU32(pc ? ColPCRow : ColSP));
                        ImGui.TextUnformatted(m[at].ToString("X2"));
                        if (changed) ImGui.PopStyleColor();
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(string.Format("${0:X4}: ${1:X2} = {1}{2}", at, m[at], (m[at] >= 32 && m[at] < 127) ? "  '" + (char)m[at] + "'" : ""));
                            if (s.Stopped && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { MemEditAddress = at; MemEditText = m[at].ToString("X2"); }
                        }
                    }
                    ImGui.SameLine(0, charW * 2);
                    var sb = new StringBuilder(16);
                    for (int i = 0; i < 16; i++) { byte b = m[a + i]; sb.Append(b >= 32 && b < 127 ? (char)b : '.'); }
                    ImGui.TextColored(ColDim, sb.ToString());
                }
            }
            clipper.End();
            clipper.Destroy();
            ImGui.EndChild();
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

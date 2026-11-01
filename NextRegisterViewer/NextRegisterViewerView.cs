// ********************************************************************************************************************************************
//      CSpect Next Register Viewer extension, shows the next registers in realtime
//      Written by:
//                  Mike Dailly
//
//      contributions by:
//
//      Released under the GNU 3 license - please see license file for more details
//
//      The viewer UI, drawn with Dear ImGui into the plugin's own OS window (iWindow.SetImGuiDraw).
//      Registers are read on the emulator thread (NextRegisterViewerPlugin.Tick), drawn here on the main thread.
//
// ********************************************************************************************************************************************
using ImGuiNET;
using Plugin;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NextRegisterViewer
{
    // *********************************************************************************************************
    /// <summary>
    ///     The Next Register viewer window contents
    /// </summary>
    // *********************************************************************************************************
    public class NextRegisterViewerView
    {
        /// <summary>Normal register text</summary>
        static readonly Vector4 ColNormal = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        /// <summary>Register has been written to recently</summary>
        static readonly Vector4 ColWritten = new Vector4(0.565f, 0.933f, 0.565f, 1.0f);      // LightGreen
        /// <summary>Tool tip labels</summary>
        static readonly Vector4 ColLabel = new Vector4(0.70f, 0.75f, 0.85f, 1.0f);

        RegDetails Regs;
        iCSpect CSpect;

        /// <summary>Only show registers that have a description</summary>
        bool ShowKnownOnly = false;
        /// <summary>Tooltip colour swatch (ARGB), 0 = none. Set by the register description functions</summary>
        uint TTColour;
        bool TTHasColour;


        // ******************************************************************************************
        /// <summary>
        ///     Create Next Register viewer
        /// </summary>
        /// <param name="_regs">The register state (filled in by the plugin each emulator frame)</param>
        /// <param name="_CSpect">CSpect interface</param>
        // ******************************************************************************************
        public NextRegisterViewerView(RegDetails _regs, iCSpect _CSpect)
        {
            Regs = _regs;
            CSpect = _CSpect;
        }


        #region Drawing
        // ******************************************************************************************
        /// <summary>
        ///     ImGui draw callback - main thread, inside this window's own ImGui frame.
        ///     One undecorated ImGui panel fills the OS window.
        /// </summary>
        // ******************************************************************************************
        public unsafe void Draw()
        {
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
            ImGuiWindowFlags wflags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                                      ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus;
            bool open = true;
            if (ImGui.Begin("##NextRegisterViewer", ref open, wflags))
            {
                ImGui.Checkbox("Described registers only", ref ShowKnownOnly);
                ImGui.SameLine();
                ImGui.TextDisabled("(Ctrl+Shift+R)");

                ImGuiTableFlags flags = ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV |
                                        ImGuiTableFlags.SizingFixedFit;
                if (ImGui.BeginTable("nextregs", 4, flags))
                {
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableSetupColumn("Reg", ImGuiTableColumnFlags.WidthFixed);
                    ImGui.TableSetupColumn("Hex", ImGuiTableColumnFlags.WidthFixed);
                    ImGui.TableSetupColumn("Dec", ImGuiTableColumnFlags.WidthFixed);
                    ImGui.TableSetupColumn("Binary", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();

                    if (ShowKnownOnly)
                    {
                        for (int r = 0; r < 256; r++)
                        {
                            if (HasDescription(r)) DrawRow(r);
                        }
                    }
                    else
                    {
                        ImGuiListClipperPtr clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                        clipper.Begin(256);
                        while (clipper.Step())
                        {
                            for (int r = clipper.DisplayStart; r < clipper.DisplayEnd; r++) DrawRow(r);
                        }
                        clipper.End();
                        clipper.Destroy();
                    }
                    ImGui.EndTable();
                }
            }
            ImGui.End();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw one register row (green if recently written), with a tooltip on hover
        /// </summary>
        /// <param name="_reg">Register number</param>
        // ******************************************************************************************
        void DrawRow(int _reg)
        {
            int v = Regs.NextRegisters[_reg];
            bool written = Regs.RegisterIsWritten[_reg] > 0;

            ImGui.TableNextRow();
            ImGui.PushStyleColor(ImGuiCol.Text, written ? ColWritten : ColNormal);

            ImGui.TableNextColumn();
            ImGui.Selectable("$" + _reg.ToString("X2") + "##r" + _reg, false, ImGuiSelectableFlags.SpanAllColumns);
            bool hovered = ImGui.IsItemHovered();
            ImGui.TableNextColumn(); ImGui.TextUnformatted("$" + v.ToString("X2"));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(v.ToString().PadLeft(3));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(Convert.ToString(v, 2).PadLeft(8, '0').Insert(4, " "));

            ImGui.PopStyleColor();

            if (hovered) DrawTooltip(_reg);
        }

        // ******************************************************************************************
        /// <summary>
        ///     Show the register description as a tooltip.
        ///     Description text: first line(s) = title, then "\tLabel:\tValue" lines which are laid
        ///     out in an aligned 2 column table.
        /// </summary>
        /// <param name="_reg">Register number</param>
        // ******************************************************************************************
        void DrawTooltip(int _reg)
        {
            TTHasColour = false;
            string text = GetRegisterText(_reg);

            ImGui.BeginTooltip();

            List<string[]> rows = new List<string[]>();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0) continue;
                if (line[0] == '\t')
                {
                    // "\tLabel:\t\t\tValue" -> label, value
                    string[] parts = line.Split(new char[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) continue;
                    string value = parts.Length > 1 ? string.Join("   ", parts, 1, parts.Length - 1) : "";
                    rows.Add(new string[] { parts[0].Trim(), value.Trim() });
                }
                else
                {
                    // Title line - "Title: \t$XX" -> "Title: $XX"
                    FlushRows(rows);
                    string[] parts = line.Split(new char[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    ImGui.TextUnformatted(string.Join(" ", parts).Trim());
                }
            }
            FlushRows(rows);

            if (TTHasColour)
            {
                uint c = TTColour;
                Vector4 col = new Vector4(((c >> 16) & 0xff) / 255.0f, ((c >> 8) & 0xff) / 255.0f, (c & 0xff) / 255.0f, 1.0f);
                ImGui.ColorButton("##ttcol", col, ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker, new Vector2(32, 16));
            }

            ImGui.EndTooltip();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw any pending label/value rows as an aligned table
        /// </summary>
        // ******************************************************************************************
        static void FlushRows(List<string[]> _rows)
        {
            if (_rows.Count == 0) return;
            if (ImGui.BeginTable("##tt", 2, ImGuiTableFlags.SizingFixedFit))
            {
                foreach (string[] r in _rows)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.PushStyleColor(ImGuiCol.Text, ColLabel);
                    ImGui.TextUnformatted(r[0]);
                    ImGui.PopStyleColor();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(r[1]);
                }
                ImGui.EndTable();
            }
            _rows.Clear();
        }
        #endregion


        #region Display Next Register descriptions
        // ***************************************************************************************************************************
        /// <summary>
        ///     Does this register have a proper description?
        /// </summary>
        // ***************************************************************************************************************************
        static bool HasDescription(int _reg)
        {
            switch (_reg)
            {
                case 0x00: case 0x05: case 0x07:
                case 0x12: case 0x13: case 0x14: case 0x15: case 0x16: case 0x17:
                case 0x18: case 0x19: case 0x1a: case 0x1b: case 0x1e: case 0x1f:
                case 0x26: case 0x27: case 0x2f: case 0x30: case 0x31: case 0x32: case 0x33:
                case 0x50: case 0x51: case 0x52: case 0x53: case 0x54: case 0x55: case 0x56: case 0x57:
                case 0x68:
                    return true;
                default:
                    return false;
            }
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      Sprite and Layers System
        /// </summary>
        /// <param name="_v">Next Register value</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetReg0x15(int _v, string _BBB)
        {
            string Low = "OFF";
            string SpritePri = "Sprite 127 on top";
            string SpriteClipping = "OFF";
            string LayerPri = "";
            string SpriteOverBorder = "OFF";
            string SpriteEnable = "OFF";
            if ((_v & 0x80) != 0) Low = "ON";
            if ((_v & 0x40) != 0) SpritePri = "Sprite 0 on top";
            if ((_v & 0x2) != 0) SpriteOverBorder = "ON";
            if ((_v & 0x1) != 0) SpriteEnable = "ON";
            switch ((_v >> 2) & 0x7)
            {
                case 0: LayerPri = "S L U"; break;
                case 1: LayerPri = "L S U"; break;
                case 2: LayerPri = "S U L"; break;
                case 3: LayerPri = "L U S"; break;
                case 4: LayerPri = "U S L"; break;
                case 5: LayerPri = "U L S"; break;
                case 6: LayerPri = "(U|T)S(T|U)(B+L)"; break;
                case 7: LayerPri = "(U|T)S(T|U)(B+L-5)"; break;
            }

            string s = string.Format(
            "Sprite and Layers System" + _BBB +
            "\tLowResMode:\t\t\t{0}" + _BBB +
            "\tSprite priority:\t\t\t{1}" + _BBB +
            "\tSpriteClip in over border mode:\t{2}" + _BBB +
            "\tLayer Priority:\t\t\t{3}" + _BBB +
            "\tSprites over Border:\t\t{4}" + _BBB +
            "\tSprites Enable:\t\t\t{5}" + _BBB,
            Low, SpritePri, SpriteClipping, LayerPri, SpriteOverBorder, SpriteEnable);
            return s;
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      Global Transparency Colour
        /// </summary>
        /// <param name="_v">Next Register value - RRRGGGBB</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public unsafe string GetReg0x14(int _v, string _BBB)
        {
            int r, g, b;
            b = _v & 3;
            g = (_v >> 2) & 7;
            r = (_v >> 5) & 7;
            string s = string.Format("Global Transparency Colour: \t${0:X2}" + _BBB +
                          "\tRed:\t{1}" + _BBB +
                          "\tGreen:\t{2}" + _BBB +
                          "\tBlue:\t{3}" + _BBB, _v, r, g, b);

            UInt32* pCols = CSpect.Get32BITColours();
            if (pCols != null)
            {
                TTColour = pCols[_v << 1];
                TTHasColour = true;
            }
            return s;
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      CPU Speed
        /// </summary>
        /// <param name="_v">Next Register value</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetReg0x07(int _v, string _BBB)
        {
            string[] speeds = { "3.5Mhz", "7Mhz", "14Mhz", "28Mhz" };
            string s = string.Format("CPU Speed: \t${0:X2}" + _BBB +
                       "\tActual CPU speed:\t" + speeds[_v & 3] + _BBB +       // use set speed as emulator is the same
                       "\tProgrammed CPU speed:\t" + speeds[_v & 3] + _BBB, _v);
            return s;
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      Peripheral 1 Setting
        /// </summary>
        /// <param name="_v">Next Register value</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetReg0x05(int _v, string _BBB)
        {
            string[] joystick = { "Sinclair 2", "Kempston 1","Cursor", "Sinclair 1",
                                "Kempston 2","MegaDrive 1","MegaDrive 1", "User Defined Keys Joystick" };

            int j1 = ((_v >> 6) & 3) + ((_v >> 1) & 4);
            int j2 = ((_v >> 4) & 3) + ((_v & 2) << 1);
            string _50Hz = "50Hz";
            string ScanDoub = "CRT";
            if ((_v & 4) != 0) _50Hz = "60Hz";
            if ((_v & 1) != 0) ScanDoub = "VGA";
            string s = string.Format("Peripheral 1 Setting: \t${0:X2}" + _BBB +
                       "\tJoystick 1 mode:\t" + joystick[j1] + _BBB +
                       "\tJoystick 2 mode:\t" + joystick[j2] + _BBB +
                       "\t50/60 Hz mode:\t" + _50Hz + _BBB +
                       "\tScandoubler:\t" + ScanDoub + _BBB, _v);
            return s;
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      Get machine ID
        /// </summary>
        /// <param name="_v">Next Register value</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetReg0x00(int _v, string _BBB)
        {
            string id;
            switch (_v)
            {
                case 1: id = "DE1A"; break;
                case 2: id = "DE2A"; break;
                case 5: id = "FBLABS"; break;
                case 6: id = "VTRUCCO"; break;
                case 7: id = "WXEDA"; break;
                case 11: id = "Multicore"; break;

                case 8: id = "EMULATORS"; break;

                case 10: id = "ZX Spectrum Next"; break;
                case 0xfa: id = "ZX Spectrum Next Anti-brick"; break;

                case 0x9a: id = "ZX Spectrum Next Core on UnAmiga Reloaded"; break;
                case 0xaa: id = "ZX Spectrum Next Core on UnAmiga"; break;
                case 0xba: id = "ZX Spectrum Next Core on SiDi"; break;
                case 0xca: id = "ZX Spectrum Next Core on MIST"; break;
                case 0xda: id = "ZX Spectrum Next Core on MiSTer"; break;
                case 0xea: id = "ZX Spectrum Next Core on ZX-DOS"; break;
                default: id = "unknown"; break;
            }
            return "Machine ID" + _BBB + "\tMachine:\t" + id + _BBB;
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      Clip windows (Layer 2, Sprites, ULA, Tilemap)
        /// </summary>
        /// <param name="_title">Window name</param>
        /// <param name="_x1">left</param>
        /// <param name="_y1">top</param>
        /// <param name="_x2">right</param>
        /// <param name="_y2">bottom</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        static string ClipWindow(string _title, int _x1, int _y1, int _x2, int _y2, string _BBB)
        {
            return string.Format(_title + _BBB +
                          "\tX1 position:\t{0}" + _BBB +
                          "\tY1 position:\t{1}" + _BBB +
                          "\tX2 position:\t{2}" + _BBB +
                          "\tY2 position:\t{3}" + _BBB, _x1, _y1, _x2, _y2);
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      Active video line
        /// </summary>
        // ***************************************************************************************************************************
        public string GetReg0x1E_1F(int _v, string _BBB)
        {
            int line = ((Regs.NextRegisters[0x1e] & 0x1) << 8) | Regs.NextRegisters[0x1f];
            return "Active video line:\t" + line.ToString();
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      ULA Control register
        /// </summary>
        /// <param name="_v">Next Register value</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetReg0x68(int _v, string _BBB)
        {
            string ULAOut = "Enabled";
            string BlendinginSLUModes = "";
            string CancelMatirx = "OFF";
            string ULAPlus = "Disabled";
            string ULAHalfPixel = "0";
            string StencilMode = "Disabled";

            if ((_v & 0x80) != 0) ULAOut = "Disabled";
            switch (_v & 0x60)
            {
                case 0x20: BlendinginSLUModes = "no blending"; break;
                case 0x40: BlendinginSLUModes = "ula/tilemap mix result as blend colour"; break;
                case 0x60: BlendinginSLUModes = "tilemap as blend colour"; break;
                case 0x00:
                default:
                    BlendinginSLUModes = "ula as blend colour"; break;
            }
            if ((_v & 0x10) != 0) CancelMatirx = "ON";
            if ((_v & 0x08) != 0) ULAPlus = "Enabled";
            if ((_v & 0x02) != 0) ULAHalfPixel = "1";
            if ((_v & 0x01) != 0) StencilMode = "Enabled";

            string s = string.Format("ULA Control" + _BBB +
                          "\tULA output:\t\t\t{0}" + _BBB +
                          "\tSLU Blending:\t\t\t{1}" + _BBB +
                          "\tExtended keys Matrix control:\t{2}" + _BBB +
                          "\tULA+:\t\t\t\t{3}" + _BBB +
                          "\tULA half pixel scroll:\t\t{4}" + _BBB +
                          "\tstencil mode:\t\t\t{5}" + _BBB,
                          ULAOut, BlendinginSLUModes, CancelMatirx, ULAPlus, ULAHalfPixel, StencilMode);
            return s;
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///     Format Next registers nicely for tool tips
        /// </summary>
        /// <param name="_reg">register number</param>
        /// <returns>
        ///     A well formatted Tooltip for a register
        /// </returns>
        // ***************************************************************************************************************************
        public string GetRegisterText(int _reg)
        {
            string BBB = "\n";
            int b = Regs.NextRegisters[_reg];
            switch (_reg)
            {
                case 0x00: return GetReg0x00(b, BBB);
                case 0x05: return GetReg0x05(b, BBB);
                case 0x07: return GetReg0x07(b, BBB);
                case 0x12: return string.Format("Layer2 Active RAM Bank: {0} , ${0:X2}", b);
                case 0x13: return string.Format("Layer2 Shadow RAM Bank: {0} , ${0:X2}", b);
                case 0x14: return GetReg0x14(b, BBB);
                case 0x15: return GetReg0x15(b, BBB);
                case 0x16: return string.Format("Layer2 X Scroll LSB: {0}", b);
                case 0x17: return string.Format("Layer2 Y Scroll: {0}", b);
                case 0x18: return ClipWindow("Clip Window Layer 2", Regs.ClipWindowLayer2[0], Regs.ClipWindowLayer2[1], Regs.ClipWindowLayer2[2], Regs.ClipWindowLayer2[3], BBB);
                case 0x19: return ClipWindow("Clip Window Sprites", Regs.ClipWindowSprites[0], Regs.ClipWindowSprites[1], Regs.ClipWindowSprites[2], Regs.ClipWindowSprites[3], BBB);
                // ULA clip register order is X1,X2,Y1,Y2
                case 0x1a: return ClipWindow("Clip Window ULA (and LowRes)", Regs.ClipWindowULA[0], Regs.ClipWindowULA[2], Regs.ClipWindowULA[1], Regs.ClipWindowULA[3], BBB);
                case 0x1b: return ClipWindow("Clip Window Tilemap", Regs.ClipWindowTilemap[0] * 2, Regs.ClipWindowTilemap[1], Regs.ClipWindowTilemap[2] * 2, Regs.ClipWindowTilemap[3], BBB);
                case 0x1e:
                case 0x1f: return GetReg0x1E_1F(b, BBB);
                case 0x26: return string.Format("ULA X Scroll: {0}", b);
                case 0x27: return string.Format("ULA Y Scroll: {0}", b);
                case 0x2f: return string.Format("Tilemap X Scroll MSB: {0}", b);
                case 0x30: return string.Format("Tilemap X Scroll LSB: {0}", b);
                case 0x31: return string.Format("Tilemap Offset Y: {0}", b);
                case 0x32: return string.Format("LoRes X Scroll: {0}", b);
                case 0x33: return string.Format("LoRes Y Scroll: {0}", b);
                case 0x50: case 0x51: case 0x52: case 0x53:
                case 0x54: case 0x55: case 0x56: case 0x57:
                    return string.Format("MMU {0}: ${1:X2}  (${2:X4}-${3:X4})", _reg - 0x50, b, (_reg - 0x50) * 0x2000, (_reg - 0x50) * 0x2000 + 0x1fff);
                case 0x68: return GetReg0x68(b, BBB);
                default:
                    // if not available, return the decimal version
                    return string.Format("Reg ${0:X2}:   {1}", _reg, b);
            }
        }
        #endregion
    }
}

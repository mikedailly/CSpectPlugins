// ********************************************************************************************************************************************
//      CSpect Copper Disassembler extension, shows the copper in realtime
//      Written by:
//                  Mike Dailly
//
//      contributions by:
//
//      Released under the GNU 3 license - please see license file for more details
//
//      Uses the KEY extension method to start, Tick() to capture the copper, and its own OS window
//      (iCSpect.OpenWindow) drawn with Dear ImGui (iWindow.SetImGuiDraw) - no WinForms.
//
//      <ctrl><alt>c    open the disassembler
//      <ctrl><alt>x    toggle the copper WAIT / IRQ line visualiser
//
// ********************************************************************************************************************************************
using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using Plugin;

namespace CopperDissassembler
{
    // *********************************************************************************************************
    /// <summary>
    ///     The copper disassembler
    /// </summary>
    // *********************************************************************************************************
    class CopperPlugin : iPlugin
    {
        const int COPPER_SIZE = 2048;                   // bytes
        const int COPPER_INSTRUCTIONS = COPPER_SIZE / 2;

        /// <summary>CSpect emulator interface</summary>
        iCSpect CSpect;
        /// <summary>Window open? (Tick only captures the copper while it is)</summary>
        public static volatile bool Active;

        byte[] CopperMemory = new byte[COPPER_SIZE];
        bool[] CopperIsWritten = new bool[COPPER_INSTRUCTIONS];

        volatile bool OpenCopperWindow = false;
        /// <summary>Our window (null when closed)</summary>
        iWindow Window;

        static readonly Vector4 ColUnwritten = new Vector4(1.0f, 0.35f, 0.35f, 1.0f);
        static readonly Vector4 ColWaitRow = new Vector4(0.22f, 0.24f, 0.32f, 1.0f);

        // *********************************************************************************************************
        /// <summary>
        ///     Init the plugin
        /// </summary>
        /// <param name="_CSpect">CSpect interface</param>
        /// <returns>
        ///     A list of plugin stuff
        /// </returns>
        // *********************************************************************************************************
        public List<sIO> Init(iCSpect _CSpect)
        {
            Console.WriteLine(" Copper Disassembler added");

            CSpect = _CSpect;

            // Detect keypress for starting disassembler
            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO("<ctrl><alt>c", eAccess.KeyPress, 0));                   // Key press callback
            ports.Add(new sIO("<ctrl><alt>x", eAccess.KeyPress, 1));                   // toggle copper/irq visualiser
            return ports;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Key pressed callback.
        /// </summary>
        /// <param name="_id"></param>
        /// <returns></returns>
        // ******************************************************************************************
        public bool KeyPressed(int _id)
        {
            if (_id == 0)
            {
                OpenCopperWindow = true;
                return true;                // handled - CSpect clears the keyboard so the Next doesn't see the key press
            }
            else if (_id == 1)
            {
                // Toggle the copper wait and IRQ trigger visualiser
                bool b = (bool)CSpect.GetGlobal(eGlobal.copper_wait);
                bool i = (bool)CSpect.GetGlobal(eGlobal.irq_wait);
                CSpect.SetGlobal(eGlobal.copper_wait, !b);
                CSpect.SetGlobal(eGlobal.irq_wait, !i);
                return true;
            }
            return false;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Showdown - free any non-managed resources
        /// </summary>
        // ******************************************************************************************
        public void Quit()
        {
            Active = false;
            if (Window != null)
            {
                iWindow w = Window;
                Window = null;
                w.Close();
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Read access type - not used
        /// </summary>
        // ******************************************************************************************
        public byte Read(eAccess _type, int _address, int _id, out bool _isvalid)
        {
            _isvalid = false;
            return 0;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Machine has been reset
        /// </summary>
        // ******************************************************************************************
        public void Reset()
        {
        }

        // ******************************************************************************************
        /// <summary>
        ///     Called once an emulator frame - capture the copper if the window is open
        /// </summary>
        // ******************************************************************************************
        public void Tick()
        {
            if (!Active) return;

            for (int i = 0; i < COPPER_SIZE; i++)
            {
                CopperMemory[i] = CSpect.CopperRead(i);
            }
            for (int i = 0; i < COPPER_SIZE; i += 2)
            {
                CopperIsWritten[i >> 1] = CSpect.CopperIsWritten(i);
            }
        }


        // ******************************************************************************************
        /// <summary>
        ///     Called once an OS frame - open the window when asked
        /// </summary>
        // ******************************************************************************************
        public void OSTick()
        {
            if (!OpenCopperWindow) return;
            OpenCopperWindow = false;
            if (Window != null) return;                     // already open

            iWindow w = CSpect.OpenWindow("Copper Disassembler", 560, 700);
            if (w == null) return;
            if (!w.SetImGuiDraw(Draw))
            {
                Console.WriteLine(" Copper Disassembler: Dear ImGui not available on this system");
                w.Close();
                return;
            }
            w.OnClosed += Window_OnClosed;
            Window = w;
            Active = true;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Our window was closed (by the user, or Quit)
        /// </summary>
        // ******************************************************************************************
        private void Window_OnClosed(object sender, EventArgs e)
        {
            Active = false;
            Window = null;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Write access type - not used
        /// </summary>
        // ******************************************************************************************
        public bool Write(eAccess _type, int _port, int _id, byte _value)
        {
            return false;
        }

        // ******************************************************************************************
        /// <summary>
        ///     ImGui draw callback - main thread, inside this window's own ImGui frame.
        ///     One undecorated ImGui panel fills the OS window.
        /// </summary>
        // ******************************************************************************************
        unsafe void Draw()
        {
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
            ImGuiWindowFlags wflags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                                      ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus;
            bool open = true;                               // no close button (NoDecoration) - the OS window closes it
            if (ImGui.Begin("##CopperDisassembler", ref open, wflags))
            {
                // Copper WAIT / IRQ line visualiser (also <ctrl><alt>x)
                bool copperWait = (bool)CSpect.GetGlobal(eGlobal.copper_wait);
                bool irqWait = (bool)CSpect.GetGlobal(eGlobal.irq_wait);
                if (ImGui.Checkbox("Show copper WAITs", ref copperWait)) CSpect.SetGlobal(eGlobal.copper_wait, copperWait);
                ImGui.SameLine();
                if (ImGui.Checkbox("Show IRQ lines", ref irqWait)) CSpect.SetGlobal(eGlobal.irq_wait, irqWait);
                ImGui.SameLine();
                ImGui.TextDisabled("(Ctrl+Alt+X)");

                ImGuiTableFlags flags = ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV |
                                        ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable;
                if (ImGui.BeginTable("copper", 5, flags))
                {
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableSetupColumn("Addr", ImGuiTableColumnFlags.WidthFixed);
                    ImGui.TableSetupColumn("Data", ImGuiTableColumnFlags.WidthFixed);
                    ImGui.TableSetupColumn("Op", ImGuiTableColumnFlags.WidthFixed);
                    ImGui.TableSetupColumn("Reg", ImGuiTableColumnFlags.WidthFixed);
                    ImGui.TableSetupColumn("Args", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();

                    // Only build the rows that are on screen
                    ImGuiListClipperPtr clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                    clipper.Begin(COPPER_INSTRUCTIONS);
                    while (clipper.Step())
                    {
                        for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                        {
                            DrawRow(row);
                        }
                    }
                    clipper.End();
                    clipper.Destroy();

                    ImGui.EndTable();
                }
            }
            ImGui.End();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw one copper instruction
        /// </summary>
        // ******************************************************************************************
        void DrawRow(int _row)
        {
            int Address = _row * 2;
            int instruction = CopperMemory[Address + 1] + ((int)CopperMemory[Address] << 8);

            string cmd, args, regnum;
            bool isWait = (instruction & 0x8000) != 0;
            if (isWait)
            {
                // wait
                int scan = instruction & 0x1ff;
                int hscan = ((instruction >> 9) & 0x3f);
                cmd = "WAIT";
                args = string.Format("V{0},H{1}", scan, hscan);
                regnum = "";
            }
            else
            {
                // move
                int reg = (instruction >> 8) & 0x7f;
                int value = instruction & 0xff;
                cmd = "MOVE";
                regnum = string.Format("({0})", reg);
                args = string.Format("{0},{1}", GetReg(reg), value);
            }

            ImGui.TableNextRow();
            if (isWait) ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.GetColorU32(ColWaitRow));

            bool written = CopperIsWritten[_row];
            if (!written) ImGui.PushStyleColor(ImGuiCol.Text, ColUnwritten);

            ImGui.TableNextColumn(); ImGui.TextUnformatted(Address.ToString("X4"));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(instruction.ToString("X4"));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(cmd);
            ImGui.TableNextColumn();
            if (written) ImGui.TextDisabled(regnum);            // register number dimmed (text has no '%')
            else ImGui.TextUnformatted(regnum);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(args);

            if (!written) ImGui.PopStyleColor();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Next register names
        /// </summary>
        // ******************************************************************************************
        static string GetReg(int reg)
        {
            switch (reg)
            {
                case 0: return "MachineID";
                case 1: return "CoreMajorVersion";
                case 2: return "Reset";
                case 3: return "MachineType";
                case 4: return "ConfigMapping";
                case 5: return "Peripheral1";
                case 6: return "Peripheral2";
                case 7: return "CPUSpeed";
                case 8: return "Peripheral3";
                case 9: return "Peripheral4";
                case 10: return "Peripheral5";
                case 11: return "JoystickMode";
                case 14: return "CoreMinorVersion";
                case 15: return "BoardID";
                case 16: return "CoreBoot";
                case 17: return "VideoTiming";
                case 18: return "Layer2_Bank";
                case 19: return "Layer2_Shadow";
                case 20: return "GlobalTransparency";
                case 21: return "Layers";
                case 22: return "Layer2_XScroll";
                case 23: return "Layer2_YScroll";
                case 24: return "Clip_Layer2";
                case 25: return "Clip_Sprites";
                case 26: return "Clip_ULA";
                case 27: return "Clip_Tilemap";
                case 28: return "Clip_Control";
                case 30: return "ActiveVideoLine_MSB";
                case 31: return "ActiveVideoLine_LSB";
                case 32: return "MaskableInterrupt";
                case 34: return "LineInterrupControl";
                case 35: return "LineInterrupt_LSB";
                case 36: return "Reserved";
                case 38: return "ULA_XScroll";
                case 39: return "ULA_YScroll";
                case 40: return "PS2_Keymap_Address_MSB";
                case 41: return "PS2_Keymap_Address_LSB";
                case 42: return "PS2_Keymap_Data_MSB";
                case 43: return "PS2_Keymap_Data_LSB";
                case 44: return "DAC_B_Mirror_Left";
                case 45: return "DAC_AD_Mirror_Mono";
                case 46: return "DAC_C_Mirror_Right";
                case 47: return "Tilemap_X_Scroll_MSB";
                case 48: return "Tilemap_X_Scroll_LSB";
                case 49: return "Tilemap_Y_Scroll";
                case 50: return "LoRes_X_Scroll";
                case 51: return "LoRes_Y_Scroll";
                case 52: return "Sprite_Number";
                case 117:
                case 53: return "Sprite_Attribute_0";
                case 118:
                case 54: return "Sprite_Attribute_1";
                case 119:
                case 55: return "Sprite_Attribute_2";
                case 120:
                case 56: return "Sprite_Attribute_3";
                case 121:
                case 57: return "Sprite_Attribute_4";
                case 64: return "Palette_Index";
                case 65: return "Palette_Value";
                case 66: return "ULANext_Format";
                case 67: return "PaletteControl";
                case 68: return "PaletteValue";
                case 74: return "FallbackColour";
                case 75: return "SpriteTransparencyIndex";
                case 76: return "TilemapTransparencyIndex";
                case 80: return "MMU_0";
                case 81: return "MMU_1";
                case 82: return "MMU_2";
                case 83: return "MMU_3";
                case 84: return "MMU_4";
                case 85: return "MMU_5";
                case 86: return "MMU_6";
                case 87: return "MMU_7";
                case 96: return "Copper_Data8";
                case 97: return "Copper_Addr_LSB";
                case 98: return "Copper_Control";
                case 99: return "Copper_Data16";
                case 100: return "Vertical_Line_Offset";
                case 104: return "ULA_Control";
                case 105: return "DisplayControl1";
                case 106: return "LoResControl";
                case 107: return "TilemapControl";
                case 108: return "DefaultTilemapAttrib";
                case 110: return "TilemapBaseAddress";
                case 111: return "TilemapDefAddress";
                case 112: return "Layer2Control";
                case 113: return "Layer2_XScroll_MSB";
                case 127: return "UserReg0";
                default:
                    return reg.ToString();
            }
        }
    }
}

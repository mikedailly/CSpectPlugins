// ********************************************************************************************************************************************
//      CSpect Copper Disassembler extension, shows the copper in realtime
//      Written by:
//                  Mike Dailly

//      contributions by:
//                  
//      Released under the GNU 3 license - please see license file for more details
//
//      This extension uses the KEY extension method to start. then thje "tick()" to update
//
// ********************************************************************************************************************************************
using System;
using System.Drawing;
using System.Windows.Forms;

namespace CopperDissassembler
{
    public partial class CopperDissForm : Form
    {
        /// <summary>Current copper memory</summary>
        byte[] CopperMemory;
        /// <summary>Array of flags to say if this location has been written or not</summary>
        bool[] CopperIsWritten;

        /// <summary>Current viewing address</summary>
        int StartAddress = 0;
        /// <summary>The font used for drawing</summary>
        System.Drawing.Font drawFont;
        /// <summary>The font used for drawing</summary>
        System.Drawing.Font drawFont_small;

        /// <summary>A black brush for drawing text</summary>
        System.Drawing.SolidBrush drawBrush;
        /// <summary>A ref brush drawing the text when the copper hasn't been set at this location</summary>
        System.Drawing.SolidBrush drawRedBrush;
        /// <summary>A ref brush drawing the text when the copper hasn't been set at this location</summary>
        System.Drawing.SolidBrush drawLightGrayBrush;
        /// <summary>A ref brush drawing the background of WAITs</summary>
        System.Drawing.SolidBrush drawLightGrayBrushB;
        /// <summary>Number of visible lines if text</summary>
        int visible_lines;

        bool first_time = true;
        // ******************************************************************************************
        /// <summary>
        ///     Create copper disassembler
        /// </summary>
        /// <param name="_CopperMemory">reference to copper memory</param>
        /// <param name="_CopperIsWritten">reference to coppy flags</param>
        // ******************************************************************************************
        public CopperDissForm(byte[] _CopperMemory, bool[] _CopperIsWritten)
        {
            CopperMemory = _CopperMemory;
            CopperIsWritten = _CopperIsWritten;

            InitializeComponent();

            this.Paint += new System.Windows.Forms.PaintEventHandler(CopperDissForm_Paint);
            this.Refresh();
            this.Invalidate(true);
            Application.DoEvents();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Init on form load
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void Form1_Load(object sender, EventArgs e)
        {
            CalcScrollBar();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Create the font and get the number of visible lines
        /// </summary>
        // ******************************************************************************************
        void CreateFont()
        {
            if (!first_time) return;

            drawFont = new System.Drawing.Font("Courier New", 12);
            drawFont_small = new System.Drawing.Font("Courier New", 8);
            drawBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Black);
            drawRedBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Red);
            drawLightGrayBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Gray);
            drawLightGrayBrushB = new System.Drawing.SolidBrush(System.Drawing.Color.WhiteSmoke);
            visible_lines = ((this.ClientSize.Height / drawFont.Height) * 2);
            first_time = false;
        }


        // ******************************************************************************************
        /// <summary>
        ///     Update the VScroll bar
        /// </summary>
        // ******************************************************************************************
        void CalcScrollBar()
        {
            CreateFont();
            vAddressScrollBar.Maximum = 2048 - visible_lines;

            vAddressScrollBar.Minimum = 0;
            vAddressScrollBar.SmallChange = 1;
            vAddressScrollBar.LargeChange = visible_lines;
            vAddressScrollBar.Maximum = 2048 + visible_lines;
        }

        string GetReg(int reg)
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


        // *******************************************************************************************************************
            /// <summary>
            ///     Render the copper disassembly
            /// </summary>
            /// <param name="_g">graphics rendering interface</param>
            // *******************************************************************************************************************
        public void DrawDisasssembly(Graphics _g)
        {
            CreateFont();

            int h = drawFont.Height;
            float x = 10.0F;
            float y = 10.0F;
            int Address = StartAddress;
            SizeF size = _g.MeasureString("MMMMMMMMMMMMM", drawFont);
            SizeF sizeW = _g.MeasureString("MMMMMMMMM", drawFont);
            int w = (int) size.Width;
            int w3 = (int)sizeW.Width;
            int rows = this.Height / h;

            for (int i = 0; i < rows; i++)
            {
                string cmd,args,regnum ;
                if (Address >= 0x800) break;

                int instruction = CopperMemory[Address+1] + ((int)CopperMemory[Address] << 8);
                if( (instruction&0x8000)!=0)
                {
                    // wait
                    int scan = instruction & 0x1ff;
                    int hscan = ((instruction >> 9) & 0x3f);
                    cmd = string.Format("WAIT  ");
                    args = string.Format("V{0},H{1}", scan, hscan);
                    regnum = "";
                }
                else
                {
                    // move
                    int reg = (instruction >> 8) & 0x7f;
                    int value = instruction & 0xff;
                    cmd = string.Format("MOVE");
                    regnum = string.Format("({0})  ", reg);
                    string sReg = GetReg(reg);
                    args = string.Format("{0},{1}", sReg, value);
                }

                SolidBrush b = drawBrush;
                if (!CopperIsWritten[Address >> 1]) b = drawRedBrush;
                string addr = string.Format("{0:X4} {1:X4}", Address, instruction);
                size = _g.MeasureString(cmd, drawFont);
                int w2 = (int) size.Width;

                size = _g.MeasureString(args, drawFont);
                int hh = (int) size.Height;

                if (regnum == "")
                {
                    int ww = this.Width;
                    Rectangle r = new Rectangle(0, (int)y, ww, hh);
                    _g.FillRectangle(drawLightGrayBrushB, r);
                }

                _g.DrawString(addr, drawFont, b, x, y);
                _g.DrawString(cmd, drawFont, b, x+w, y);
                _g.DrawString(regnum, drawFont_small, drawLightGrayBrush, x +w+w2, y+3);
                _g.DrawString(args, drawFont, b, x + w+w3 , y);

                Address += 2;
                y += h;
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw the copper didassembly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void CopperDissForm_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            DrawDisasssembly(g);
        }

        // ******************************************************************************************
        /// <summary>
        ///     Close and free up
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void CopperDissForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            CopperPlugin.Active = false;
            CopperPlugin.form = null;
        }

        public void UpdateAddress()
        {
            StartAddress = vAddressScrollBar.Value & ~1;
            if (StartAddress > (2048 - visible_lines)) StartAddress = 2048 - visible_lines;
            Invalidate();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Scroll bar change - update start address
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void vScrollBar1_ValueChanged(object sender, EventArgs e)
        {
            CreateFont();
            UpdateAddress();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Rersize the window, and recalc the "max" and "step" values
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void CopperDissForm_ResizeEnd(object sender, EventArgs e)
        {
            CalcScrollBar();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Scroll bar-a-scrollin'
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void vAddressScrollBar_Scroll(object sender, ScrollEventArgs e)
        {
            CreateFont();
            UpdateAddress();
        }
    }
}

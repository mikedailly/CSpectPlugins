// ********************************************************************************************************************************************
//      CSpect Next Register Viewer extension, shows the next registers in realtime
//      Written by:
//                  Mike Dailly

//      contributions by:
//                  
//      Released under the GNU 3 license - please see license file for more details
//
//      This extension uses the KEY extension method to start. then the "tick()" to update
//      Next register changes are grabbed via the NextRegisterWrite IO handling system
//
// ********************************************************************************************************************************************
using Plugin;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NextRegisterViewer
{
    public partial class NextRegistersForm : Form
    {
        /// <summary>Current register array</summary>
        //byte[] NextRegisters;
        /// <summary>colouring timer array. !=0 means has been modified reciently</summary>
        //int[] RegisterIsWritten;

        RegDetails Regs;

        /// <summary>Current viewing address</summary>
        int StartAddress = 0;
        /// <summary>The font used for drawing</summary>
        System.Drawing.Font drawFont;
        /// <summary>Brush black colour</summary>
        System.Drawing.Pen drawBlackPen;
        /// <summary>Brush black colour</summary>
        System.Drawing.SolidBrush drawBlack;
        /// <summary>Brush white colour</summary>
        System.Drawing.SolidBrush drawWhite;
        /// <summary>A black brush for drawing text</summary>
        System.Drawing.SolidBrush drawBrush;
        /// <summary>A ref brush drawing the text when the register has changed</summary>
        System.Drawing.SolidBrush drawGreenBrush;
        /// <summary>Number of visible lines if text</summary>
        int visible_lines;

        iCSpect CSpect;

        int MouseX=-1;
        int MouseY=-1;
        bool MouseMoved = false;

        ToolTip CurrentTT;
        List<TTItem> TTItems = new List<TTItem>();

        bool first_time = true;
        // ******************************************************************************************
        /// <summary>
        ///     Create Next Register viewer
        /// </summary>
        /// <param name="_NextRegisters">reference to copper memory</param>
        /// <param name="_RegisterIsWritten">reference to coppy flags</param>
        // ******************************************************************************************
        public NextRegistersForm(RegDetails _regs, iCSpect _CSpect)
        {
            Regs = _regs;
            CSpect = _CSpect;

            InitializeComponent();

            this.Paint += new System.Windows.Forms.PaintEventHandler(RegisterViewForm_Paint);
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

            CurrentTT = new ToolTip();
            CurrentTT.OwnerDraw = true;
            CurrentTT.Draw += new DrawToolTipEventHandler(CurrentTT_Draw);
        }


        // ******************************************************************************************
        /// <summary>
        ///     Create the font and get the number of visible lines
        /// </summary>
        // ******************************************************************************************
        void CreateFont()
        {
            if (!first_time) return;

            drawFont = new System.Drawing.Font("Arial",12,FontStyle.Bold);
            drawBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            drawGreenBrush = new System.Drawing.SolidBrush(System.Drawing.Color.LightGreen);
            drawBlack = new SolidBrush(Color.FromArgb(255, 0, 0, 0));
            drawWhite = new SolidBrush(Color.FromArgb(255, 255, 255, 255));
            drawBlackPen = new Pen(drawBlack);
            visible_lines = ((this.ClientSize.Height / drawFont.Height) );
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
            vAddressScrollBar.Maximum = 256 - visible_lines;

            vAddressScrollBar.Minimum = 0;
            vAddressScrollBar.SmallChange = 1;
            vAddressScrollBar.LargeChange = visible_lines;
            vAddressScrollBar.Maximum = 256 + visible_lines;
        }

        #region Painting
        // *******************************************************************************************************************
        /// <summary>
        ///     Render the register view window
        /// </summary>
        /// <param name="_g">graphics rendering interface</param>
        // *******************************************************************************************************************
        public void DrawDisasssembly(Graphics _g)
        {
            CreateFont();

            int h = drawFont.Height;
            float x = 10.0F;
            float y = 0.0F;
            int Address = StartAddress;
            int rows = this.Height / h;

            for (int i = 0; i < rows; i++)
            {
                if (Address >= 256) break;

                int value = Regs.NextRegisters[Address];
                string reg = string.Format("REG:{0:X2}", Address);
                string val = string.Format("V:{0:X2}", value);

                SolidBrush b = drawBrush;
                if (Regs.RegisterIsWritten[Address] >0) b = drawGreenBrush;
                //size = _g.MeasureString(reg, drawFont);
                //int w2 = (int) size.Width;
                _g.DrawString(reg, drawFont, b, x, y);
                _g.DrawString(val, drawFont, b, x+100, y);

                Address++;
                y += h;
            }

            int index = StartAddress + (MouseY / h);
            if (MouseMoved || Regs.RegisterIsWritten[index] > 0)
            {
                string tip = GetRegisterText(index);
                IWin32Window win = this;
                CurrentTT.Show( tip, win, MouseX,MouseY);                
                MouseMoved = false;
            }
        }


        // ******************************************************************************************
        /// <summary>
        ///     Draw the Next Registers
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void RegisterViewForm_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            DrawDisasssembly(g);
        }

        // ********************************************************************************************************************
        /// <summary>
        ///     Draw extra ToolTip items (images, colours etc)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ********************************************************************************************************************
        private void CurrentTT_Draw(object sender, DrawToolTipEventArgs e)
        {
            Graphics g = e.Graphics;
            foreach (TTItem item in TTItems)
            {
                switch (item.TTType)
                {
                    case eTTItemType.ColourBox:
                        {
                            int rr = (int)(item.Colour >> 16) & 0xff;
                            int gg = (int)(item.Colour >> 8) & 0xff;
                            int bb = (int)(item.Colour & 0xff);
                            SolidBrush brush = new SolidBrush(Color.FromArgb(255, rr, gg, bb));
                            g.FillRectangle(brush, new Rectangle(item.X, item.Y, item.Width, item.Height));
                            g.DrawRectangle(drawBlackPen, new Rectangle(item.X - 1, item.Y - 1, item.Width + 1, item.Height + 1));
                            break;
                        }
                    default:
                        break;
                }
            }
        }
        #endregion


        // ******************************************************************************************
        /// <summary>
        ///     Close and free up
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void RegisterViewForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            NextRegisterViewerPlugin.Active = false;
            NextRegisterViewerPlugin.form = null;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Update the address based on the vertical scroll bar
        /// </summary>
        // ******************************************************************************************
        public void UpdateAddress()
        {
            StartAddress = vAddressScrollBar.Value & ~1;
            if (StartAddress > (256 - visible_lines)) StartAddress = 256 - visible_lines;
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
        private void RegisterViewForm_ResizeEnd(object sender, EventArgs e)
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

        // ******************************************************************************************
        /// <summary>
        ///     On mouse move, update position of the tool tip
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void NextRegistersForm_MouseMove(object sender, MouseEventArgs e)
        {
            if(MouseX != e.X || MouseY != e.Y)
            {
                MouseX = e.X;
                MouseY = e.Y;
                MouseMoved = true;

                this.Invalidate();
                Application.DoEvents();
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     On mouse leave, hide the tooltip
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void NextRegistersForm_MouseLeave(object sender, EventArgs e)
        {
            IWin32Window win = this;
            CurrentTT.Hide(win);
        }





        #region Display Next Register descriptions
        // ***************************************************************************************************************************
        /// <summary>
        ///      Sprite and Layers System
        /// </summary>
        /// <param name="_val">Next Register value</param>
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
        /// <param name="_val">Next Register value - RRRGGGBB</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public unsafe string GetReg0x14(int _v, string _BBB)
        {
            int r, g, b;
            b = _v & 3;
            g = (_v >> 2) & 7;
            r = (_v >> 5) & 7;
            string s = string.Format("Global Transparency Colour: \t${0:X2}" +_BBB+
                          "\tR:{1}\tG:{2}\tB:{3}", _v, r, g, b);

            UInt32* pCols = CSpect.Get32BITColours();

            int h = drawFont.Height;
            TTItem item = new TTItem();
            item.X = 5;
            item.Y = h-4;
            item.Width = 16;
            item.Height= 16;
            item.Colour = pCols[_v<<1];
            item.TTType = eTTItemType.ColourBox;
            TTItems.Add(item);
            return s;
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      CPU Speed
        /// </summary>
        /// <param name="_val">Next Register value</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetReg0x07(int _v, string _BBB)
        {
            string[] speeds = { "3.5Mhz", "7Mhz", "14Mhz", "28Mhz" };
            string s = string.Format("CPU Speed: \t${0:X2}" + _BBB +
                       "\tActual CPU speed:\t" + speeds[_v & 3] + _BBB +       // use set speed as emulator is the same
                       "\tProgrammed CPU speed:\t" + speeds[_v & 3] + _BBB,_v);
            return s;
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      Peripheral 1 Setting
        /// </summary>
        /// <param name="_val">Next Register value</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetReg0x05(int _v, string _BBB)
        {
            string[] joystick = { "Sinclair 2", "Kempston 1","Cursor", "Sinclair 1",
                                "Kempston 2","MegaDrive 1","MegaDrive 1", "User Defined Keys Joystick" };

            int j1 = ((_v >> 6) & 3) + ((_v>>1)&4);
            int j2 = ((_v >> 4) & 3) + ((_v&2)<<1);
            string _50Hz = "50Hz";
            string ScanDoub = "CRT";
            if ( (_v&4)!=0) _50Hz = "60Hz";
            if ((_v & 1) != 0) ScanDoub = "VGA";
            string s = string.Format("Peripheral 1 Setting: \t${0:X2}" + _BBB +
                       "\tJoystick 1 mode:\t" + joystick[j1] + _BBB +       // use set speed as emulator is the same
                       "\tJoystick 2 mode:\t" + joystick[j2] + _BBB+
                       "\t50/60 Hz mode:\t" + _50Hz + _BBB+
                       "\tScandoubler:\t" + ScanDoub + _BBB, _v);
            return s;
        }
        // ***************************************************************************************************************************
        /// <summary>
        ///      Get machine ID
        /// </summary>
        /// <param name="_val">Next Register value</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetReg0x00(int _v, string _BBB)
        {
            switch(_v)
            {
                case 1: return "DE1A";
                case 2: return "DE2A";
                case 5: return "FBLABS";
                case 6: return "VTRUCCO";
                case 7: return "WXEDA";
                case 11: return "Multicore";

                case 8: return "EMULATORS";

                case 10: return "ZX Spectrum Next";
                case 0xfa: return "ZX Spectrum Next Anti-brick";

                case 0x9a: return "ZX Spectrum Next Core on UnAmiga Reloaded";
                case 0xaa: return "ZX Spectrum Next Core on UnAmiga";
                case 0xba: return "ZX Spectrum Next Core on SiDi";
                case 0xca: return "ZX Spectrum Next Core on MIST";
                case 0xda: return "ZX Spectrum Next Core on MiSTer";
                case 0xea: return "ZX Spectrum Next Core on ZX-DOS";
                default:
                    return "unknown";
            }
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      Clip Window Layer 2
        /// </summary>
        /// <param name="_val">Next Register value - RRRGGGBB</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetReg0x18(int _v, string _BBB)
        {
            string s = string.Format("Clip Window Layer 2" + _BBB +
                          "\tX1 position:\t{0}" + _BBB +
                          "\tY1 position:\t{1}" + _BBB +
                          "\tX2 position:\t{2}" + _BBB +
                          "\tY2 position:\t{3}" + _BBB, Regs.ClipWindowLayer2[0], Regs.ClipWindowLayer2[1], Regs.ClipWindowLayer2[2], Regs.ClipWindowLayer2[3]);
            return s;
        }


        // ***************************************************************************************************************************
        /// <summary>
        ///      Clip Window Sprites
        /// </summary>
        /// <param name="_val">Next Register value - RRRGGGBB</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetReg0x19(int _v, string _BBB)
        {
            string s = string.Format("Clip Window Sprites" + _BBB +
                          "\tX1 position:\t{0}" + _BBB +
                          "\tY1 position:\t{1}" + _BBB +
                          "\tX2 position:\t{2}" + _BBB +
                          "\tY2 position:\t{3}" + _BBB, Regs.ClipWindowSprites[0], Regs.ClipWindowSprites[1], Regs.ClipWindowSprites[2], Regs.ClipWindowSprites[3]);
            return s;
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      Clip Window ULA 
        /// </summary>
        /// <param name="_val">Next Register value - RRRGGGBB</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetReg0x1A(int _v, string _BBB)
        {
            string s = string.Format("Clip Window ULA (and LowRes)" + _BBB +
                          "\tX1 position:\t{0}" + _BBB +
                          "\tX2 position:\t{1}" + _BBB +
                          "\tY1 position:\t{2}" + _BBB +
                          "\tY2 position:\t{3}" + _BBB, Regs.ClipWindowULA[0], Regs.ClipWindowULA[1], Regs.ClipWindowULA[2], Regs.ClipWindowULA[3]);
            return s;
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      Clip Window Tilemap 
        /// </summary>
        /// <param name="_val">Next Register value - RRRGGGBB</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetReg0x1B(int _v, string _BBB)
        {
            string s = string.Format("Clip Window Tilemap" + _BBB +
                          "\tX1 position:\t{0}" + _BBB +
                          "\tY1 position:\t{1}" + _BBB +
                          "\tX2 position:\t{2}" + _BBB +
                          "\tY2 position:\t{3}" + _BBB, 
                          Regs.ClipWindowTilemap[0]*2, Regs.ClipWindowTilemap[1], Regs.ClipWindowTilemap[2]*2, Regs.ClipWindowTilemap[3]);
            return s;
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      Active video line 
        /// </summary>
        /// <param name="_val">Next Register value - RRRGGGBB</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetReg0x1E_1F(int _v, string _BBB)
        {
            int line = ((Regs.NextRegisters[0x1e] & 0x1) << 8) | Regs.NextRegisters[0x1f];
            string s = "Active video line:\t" + line.ToString() + "     ";
            return s;
        }


        // ***************************************************************************************************************************
        /// <summary>
        ///      Clip ULA Control register
        /// </summary>
        /// <param name="_val">Next Register value - RRRGGGBB</param>
        /// <param name="_BBB">end of line spacing and newline</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetReg0x68(int _v, string _BBB)
        {
            string ULAOut = "Enabled";
            string BlendinginSLUModes = "";
            string CancelMatirx = "OFF";
            string ULAPlus= "Disabled";
            string ULAHalfPixel = "0";
            string StencilMode = "Disabled";

            if ((_v & 0x80) != 0) ULAOut = "Disabled";
            switch (_v & 0x60)
            {
                case 0x20: BlendinginSLUModes = "no blending"; break;
                case 0x40: BlendinginSLUModes = "ula/tilemap mix result as blend colour"; break;
                case 0x30: BlendinginSLUModes = "tilemap as blend colour"; break;
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
        /// <param name="_reg"></param>
        /// <returns>
        ///     Get a well formatted Tooltip for a register
        /// </returns>
        // ***************************************************************************************************************************
        public string GetRegisterText(int _reg)
        {
            string BBB = "       \n";
            int b = Regs.NextRegisters[_reg];
            TTItems.Clear();
            switch (_reg)
            {
                case 0x00: return GetReg0x00(b, BBB);
                case 0x05: return GetReg0x05(b, BBB);
                case 0x07: return GetReg0x07(b, BBB);
                case 0x12: return string.Format("Layer2 Active RAM Bank: {0} , {0:X2}", b);
                case 0x13: return string.Format("Layer2 Shadow RAM Bank: {0} , {0:X2}", b);
                case 0x14: return GetReg0x14(b, BBB);
                case 0x15: return GetReg0x15(b, BBB);
                case 0x16: return string.Format("Layer2 X Scroll LSB: {0}", b);
                case 0x17: return string.Format("Layer2 Y Scroll: {0}", b);
                case 0x18: return GetReg0x18(b, BBB);
                case 0x19: return GetReg0x19(b, BBB);
                case 0x1a: return GetReg0x1A(b, BBB);
                case 0x1b: return GetReg0x1B(b, BBB);
                case 0x1e:
                case 0x1f: return GetReg0x1E_1F(b, BBB);
                case 0x26: return string.Format("ULA X Scroll: {0}", b);
                case 0x27: return string.Format("ULA Y Scroll: {0}", b);
                case 0x2f: return string.Format("Tilemap X Scroll MSB: {0}", b);
                case 0x30: return string.Format("Tilemap X Scroll LSB: {0}", b);
                case 0x31: return string.Format("Tilemap Offset Y: {0}", b);
                case 0x32: return string.Format("LoRes X Scroll: {0}", b);
                case 0x33: return string.Format("LoRes Y Scroll: {0}", b);
                case 0x50: return string.Format("MMU 0: {0:X2}", b);
                case 0x51: return string.Format("MMU 1: {0:X2}", b);
                case 0x52: return string.Format("MMU 2: {0:X2}", b);
                case 0x53: return string.Format("MMU 3: {0:X2}", b);
                case 0x54: return string.Format("MMU 4: {0:X2}", b);
                case 0x55: return string.Format("MMU 5: {0:X2}", b);
                case 0x56: return string.Format("MMU 6: {0:X2}", b);
                case 0x57: return string.Format("MMU 7: {0:X2}", b);
                case 0x68: return GetReg0x68(b, BBB);
                default:
                    // if not available, return the decimal version
                    return string.Format("Reg {0}:   {1}", _reg,b);
            }
        }
        #endregion
    }
}


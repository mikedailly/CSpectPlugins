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
        /// <summary>A black brush for drawing text</summary>
        System.Drawing.SolidBrush drawBrush;
        /// <summary>A ref brush drawing the text when the copper hasn't been set at this location</summary>
        System.Drawing.SolidBrush drawRedBrush;
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

            drawFont = new System.Drawing.Font("Arial", 12);
            drawBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Black);
            drawRedBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Red);
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
            SizeF size = _g.MeasureString("MMMMMM", drawFont);
            int w = (int) size.Width;
            int rows = this.Height / h;

            for (int i = 0; i < rows; i++)
            {
                string cmd,args;
                if (Address >= 0x800) break;

                int instruction = CopperMemory[Address+1] + ((int)CopperMemory[Address] << 8);
                if( (instruction&0x8000)!=0)
                {
                    // wait
                    int scan = instruction & 0x1ff;
                    int hscan = ((instruction >> 9) & 0x3f);
                    cmd = string.Format("WAIT  ");
                    args = string.Format("{0},{1}", scan, hscan);
                }
                else
                {
                    // move
                    int reg = (instruction >> 8) & 0x7f;
                    int value = instruction & 0xff;
                    cmd = string.Format("MOVE  ");
                    args = string.Format("{0},{1}", reg, value);
                }

                SolidBrush b = drawBrush;
                if (!CopperIsWritten[Address >> 1]) b = drawRedBrush;
                string addr = string.Format("{0:X4}", Address);
                size = _g.MeasureString(cmd, drawFont);
                int w2 = (int) size.Width;
                _g.DrawString(addr, drawFont, b, x, y);
                _g.DrawString(cmd, drawFont, b, x+w, y);
                _g.DrawString(args, drawFont, b, x + w+(w-(w>>2)), y);

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

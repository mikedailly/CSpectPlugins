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

namespace SpriteViewer
{
    public partial class SpriteViewerForm : Form
    {
        /// <summary>Current copper memory</summary>
        byte[] SpriteMemory;

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

        /// <summary>Sprite size</summary>
        int SpriteSize = 16;

        bool Is16Bit = true;

        /// <summary>The palette offset to use</summary>
        int PaletteOffset = 0;

        public byte[] SpriteBuffer { get; set; }

        Bitmap[] SpriteBitmap;
        bool first_time = true;
        SpriteViewerPlugin Plugin;

        // ******************************************************************************************
        /// <summary>
        ///     Create copper disassembler
        /// </summary>
        /// <param name="_SpriteMemory">reference to copper memory</param>
        /// <param name="_CopperIsWritten">reference to coppy flags</param>
        /// <param name="_plugin">The pluging object</param>
        // ******************************************************************************************
        public SpriteViewerForm(byte[] _SpriteMemory, SpriteViewerPlugin _plugin)
        {
            Plugin = _plugin;
            SpriteMemory = _SpriteMemory;

            InitializeComponent();

            SpriteBitmap = new Bitmap[128];
            for (int i = 0; i < 128; i++)
            {
                SpriteBitmap[i] = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            }
            SpriteModeCombo.SelectedIndex = 1;  // set 16 colour as default
            SprPalette.SelectedIndex = 0;       // set palette offset 0
            SprSize.SelectedIndex = 0;          // Set 16x16 as default
            SpriteSize = 16;

            this.Paint += new System.Windows.Forms.PaintEventHandler(SpriteViewer_Paint);
            SpritePanel.Paint += new System.Windows.Forms.PaintEventHandler(SpriteViewerForm_Paint);
            this.Refresh();
            this.Invalidate(true);
            Application.DoEvents();
            this.DoubleBuffered = true;
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
            //float x = 10.0F;
            //float y = 10.0F;
            int Address = StartAddress;
            SizeF size = _g.MeasureString("MMMMMM", drawFont);
            int w = (int) size.Width;
            int rows = this.Height / h;
//                    cmd = string.Format("WAIT  ");
//                    args = string.Format("{0},{1}", scan, hscan);
  
            /*SolidBrush b = drawBrush;
                string addr = string.Format("{0:X4}", Address);
                size = _g.MeasureString(cmd, drawFont);
                int w2 = (int) size.Width;
                _g.DrawString(addr, drawFont, b, x, y);*/
        }


        // ******************************************************************************************
        /// <summary>
        ///     Draw the copper didassembly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void SpriteViewer_Paint(object sender, PaintEventArgs e)
        {
            SpritePanel.Invalidate();
        }
        // ******************************************************************************************
        /// <summary>
        ///     Draw the copper didassembly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void SpriteViewerForm_Paint(object sender, PaintEventArgs e)
        {
            
            Graphics g = e.Graphics;
            if( SpriteBuffer!=null) UpdateSprites(g);

            //g.FillRectangle(Brushes.Black, 0, 0, 100, 100);
            int cnt = 0;

            int lines = 1;
            if (!Is16Bit) lines = 2;

            if (SpriteSize == 32)
            {
                int ycnt = 4 / lines;
                for (int y = 0; y < ycnt; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        e.Graphics.DrawImage(SpriteBitmap[cnt++], (x * 70), y*70,     32, 32);
                        e.Graphics.DrawImage(SpriteBitmap[cnt++], (x * 70)+31, y*70,  32, 32);
                        e.Graphics.DrawImage(SpriteBitmap[cnt++], (x * 70), (y*70)+31,  32, 32);
                        e.Graphics.DrawImage(SpriteBitmap[cnt++], (x * 70)+31, (y*70)+31, 32, 32);
                    }
                }
            }
            else
            {
                int ycnt = 16 / lines;
                for (int y = 0; y < ycnt; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        e.Graphics.DrawImage(SpriteBitmap[cnt++], x * 40, y * 40, 32, 32);
                    }
                }

            }
        }


        public void UpdateSprites(Graphics _g)
        {
            int cnt = 128;
            if(!Is16Bit) cnt = 64;
            for (int i = 0; i < cnt; i++)
            {
                ZXSprite.DrawSprite(_g, SpriteBitmap[i], Is16Bit, i, PaletteOffset, SpriteBuffer);
            }
        }


        // ******************************************************************************************
        /// <summary>
        ///     Close and free up
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void SpriteViewerForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            SpriteViewerPlugin.Active = false;
            SpriteViewerPlugin.form = null;
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
        ///     Rersize the window, and recalc the "max" and "step" values
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void SpriteViewerForm_ResizeEnd(object sender, EventArgs e)
        {
            CalcScrollBar();
        }


        // ******************************************************************************************
        /// <summary>
        ///     Sprite Size changed
        /// </summary>
        // ******************************************************************************************
        private void SprSize_SelectedIndexChanged(object sender, EventArgs e)
        {
            ComboBox b = sender as ComboBox;
            if (b.SelectedIndex == 0) SpriteSize = 16; else SpriteSize = 32;
            this.Invalidate();
        }

        // ******************************************************************************************
        /// <summary>
        ///     256 or 16 colour mode selection
        /// </summary>
        // ******************************************************************************************
        private void SpriteModeCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            ComboBox b = sender as ComboBox;
            if (b.SelectedIndex == 0) Is16Bit = false; else Is16Bit = true;
            this.Invalidate();
        }

        private void SprPalette_SelectedIndexChanged(object sender, EventArgs e)
        {
            ComboBox b = sender as ComboBox;
            PaletteOffset = b.SelectedIndex * 16;
            this.Invalidate();
            
        }

        private void SpriteViewerForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Plugin.OpenSpriteWindow = false;
            SpriteViewerPlugin.Active = false;
        }
    }
}

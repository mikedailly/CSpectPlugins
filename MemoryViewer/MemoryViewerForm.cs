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
using Microsoft.Win32;
using Plugin;
using System;
using System.Configuration;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static System.Windows.Forms.AxHost;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Rebar;

namespace MemoryViewer
{
    public enum eMemoryMode : int
    {
        SpectrumScreen = 0,
        LinearBitmap,
        Layer2_256,
        Layer2_320,
        SixteenColour_Linear,
        SixteenColour_Stripped,
        Raw_Grayscale,
    }

    public partial class MemoryViewerForm : Form
    {
        int MAX_WIDTH = 1024;
        int MAX_HEIGHT = 1024;

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

        /// <summary>Memory window width</summary>
        int MemoryWidth = 32;

        eMemoryMode Mode = eMemoryMode.SpectrumScreen;

        /// <summary>Number of bytes to read from emulator</summary>
        int BufferSize = 16384;
        byte[] MemBuffer;

        Bitmap BackBuffer;
        bool first_time = true;
        MemoryViewerPlugin Plugin;
        iCSpect CSpect;

        TTItem TTItem;
        ToolTip CurrentTT;
        int MouseX = -1;
        int MouseY = -1;
        bool MouseMoved = false;

        /// <summary>Scale to draw the image at</summary>
        int DrawScale = 1;
        int CurrentAddress = 10 * 8192;
        bool DoClear = true;
        public bool DoSnapshot = false;

        /// <summary>Palette offset</summary>
        int PaletteOffset=0;
        /// <summary>Palette offset * 16 for 256 palette</summary>
        int PaletteOffset16 = 0;

        /// <summary>Last tooltip Physical PC address</summary>
        int LastPhysicalPC = -1;
        /// <summary>Last tooltip Logical PC address</summary>
        int LastLogicalPC = -1;


        /// <summary>Hex conversion</summary>
        public static string HEX = "0123456789abcdef";

        // ******************************************************************************************
        /// <summary>
        ///     Create copper disassembler
        /// </summary>
        /// <param name="_SpriteMemory">reference to copper memory</param>
        /// <param name="_CopperIsWritten">reference to coppy flags</param>
        /// <param name="_plugin">The pluging object</param>
        // ******************************************************************************************
        public MemoryViewerForm(MemoryViewerPlugin _plugin)
        {
            Plugin = _plugin;
            CSpect = Plugin.CSpect;

            InitializeComponent();

            BackBuffer = new Bitmap(MAX_WIDTH, MAX_HEIGHT, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            MemModeCombo.SelectedIndex = 1;  // set 16 colour as default
            WidthCombo.SelectedIndex = 0;       // set palette offset 0
            
            MemoryWidth = 16;
            MemModeCombo.SelectedIndex = 0;
            WidthCombo.SelectedIndex = 5;       // 32 bytes 
            ScaleCombo.SelectedIndex = 1;       // Scaling mode
            BankOffsetBox.Text = "10:$0000";    // Base address to read
            BuffSizeBox.Text = "$4000";         // Number of bytes to read a frame
            PaletteCombo.SelectedIndex = 1;     // Palette
            OffsetCombo.SelectedIndex = 0;      // palette offset index
            MemBuffer = new byte[BufferSize];   // pre-allocate our buffer

            CreateFont();

            CurrentTT = new ToolTip();
            CurrentTT.OwnerDraw = true;
            CurrentTT.Draw += new DrawToolTipEventHandler(CurrentTT_Draw);
            //CurrentTT.Show(tip, win, MouseX, MouseY);
            //GetMemoryDetails(0, "Hello");

            this.Paint += new System.Windows.Forms.PaintEventHandler(SpriteViewer_Paint);
            MemoryPanel.Paint += new System.Windows.Forms.PaintEventHandler(SpriteViewerForm_Paint);
            MemoryPanel.MouseDoubleClick += MemoryPanel_MouseDoubleClick;

            // Enable double buffering on the sprite panel to eliminate flicker
            MemoryPanel.GetType().GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(MemoryPanel, true, null);
            

            this.Refresh();
            this.Invalidate(true);
            Application.DoEvents();
        }


        // ******************************************************************************************
        /// <summary>
        ///     Create the font and get the number of visible lines
        /// </summary>
        // ******************************************************************************************
        void CreateFont()
        {
            if (!first_time) return;

            drawFont = new System.Drawing.Font("Arial", 12, FontStyle.Bold);
            drawBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            drawGreenBrush = new System.Drawing.SolidBrush(System.Drawing.Color.LightGreen);
            drawBlack = new SolidBrush(Color.FromArgb(255, 0, 0, 0));
            drawWhite = new SolidBrush(Color.FromArgb(255, 255, 255, 255));
            drawBlackPen = new Pen(drawBlack);
            visible_lines = ((this.ClientSize.Height / drawFont.Height));
            first_time = false;
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
            //foreach (TTItem item in TTItems)
            //TTItem item = CurrentTT;
            {                 
                /*switch (TTItem.TTType)
                {
                    case eTTItemType.ColourBox:
                        {
                            int rr = (int)(TTItem.Colour >> 16) & 0xff;
                            int gg = (int)(TTItem.Colour >> 8) & 0xff;
                            int bb = (int)(TTItem.Colour & 0xff);
                            SolidBrush brush = new SolidBrush(Color.FromArgb(255, rr, gg, bb));
                            g.FillRectangle(brush, new Rectangle(TTItem.X, TTItem.Y, TTItem.Width, TTItem.Height));
                            g.DrawRectangle(drawBlackPen, new Rectangle(TTItem.X - 1, TTItem.Y - 1, TTItem.Width + 1, TTItem.Height + 1));
                            break;
                        }
                    default:
                        break;
                }*/
            }
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
            CreateFont();
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
        ///     Peek physical memory, allowing us to swap to/from ULA memory access
        /// </summary>
        /// <param name="_PhysicalAddress">The physical address to peek</param>
        /// <param name="_count">number of bytes</param>
        /// <param name="_buffer">[optional]The buffer to fill</param>
        /// <returns>
        ///     The buffer returned
        /// </returns>
        // ******************************************************************************************
        public byte[] Peek(int _PhysicalAddress, int _count, byte[] _buffer=null)
        {
            if (ULAEnabledCheckbox.Checked)
            {
                return CSpect.PeekPhysicalULA(_PhysicalAddress, _count, _buffer);
            }
            else
            {
                return CSpect.PeekPhysicalULA(_PhysicalAddress, _count, _buffer);
            }
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
            MemoryPanel.Invalidate();
        }
        // ******************************************************************************************
        /// <summary>
        ///     Draw the copper didassembly
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private unsafe void SpriteViewerForm_Paint(object sender, PaintEventArgs e)
        {
            
            Graphics g = e.Graphics;

            //g.FillRectangle(Brushes.Black, 0, 0, 100, 100);

            byte[] mem = Peek(CurrentAddress, BufferSize, MemBuffer);
            System.Drawing.Imaging.BitmapData data = BackBuffer.LockBits(new Rectangle(0, 0, BackBuffer.Width, BackBuffer.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            UInt32* pData = (UInt32*)data.Scan0;
            int ScanWidth = data.Stride;

            MAX_WIDTH = BackBuffer.Width - 2;
            MAX_HEIGHT = BackBuffer.Height - 2;

            UInt32* pDataStart = pData + (ScanWidth/4) + 1;
            if (DoClear)
            {
                DoClear = false;
                *pData = 0xff000080;
                UInt32* pBuff = pData;
                int i = (BackBuffer.Width*BackBuffer.Height) - 1;
                while (i > 0)
                {
                    pBuff[1] = pBuff[0];
                    pBuff++;
                    i--;
                }
            }

            fixed (byte* pMemPtr = &mem[0])
            {
                byte* pMem = pMemPtr;
                if (Mode == eMemoryMode.SpectrumScreen)
                {
                    DrawSpectrumScreen(pDataStart, pMem, BufferSize);
                }
                else if (Mode == eMemoryMode.LinearBitmap)
                {
                    DrawLinearBitmap(pDataStart, pMem, BufferSize);
                }
                else if (Mode == eMemoryMode.Layer2_256)
                {
                    DrawLayer2_256Bitmap(pDataStart, pMem, BufferSize);
                }
                else if (Mode == eMemoryMode.Layer2_320)
                {
                    DrawLayer2_320Bitmap(pDataStart, pMem, BufferSize);
                }
                else if (Mode == eMemoryMode.SixteenColour_Linear)
                {
                    Draw16_Linear_Bitmap(pDataStart, pMem, BufferSize);
                }
                else if (Mode == eMemoryMode.SixteenColour_Stripped)
                {
                    Draw16_Stripped_Bitmap(pDataStart, pMem, BufferSize);
                }
                else if (Mode == eMemoryMode.Raw_Grayscale)
                {
                    DrawRAWBitmap(pDataStart, pMem, BufferSize);
                }
            }
            
            BackBuffer.UnlockBits(data);

            //WidthCombo.is
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            float dpix = g.DpiX/96.0f;
            float dpiy = g.DpiY/96.0f;
            e.Graphics.DrawImage((Image)BackBuffer, new Rectangle(0,0,(int)(BackBuffer.Width*DrawScale*dpix),(int)(BackBuffer.Height*DrawScale*dpiy)));
            this.AutoScaleMode = AutoScaleMode.Dpi;


            if (MouseActive)
            {
                string tip = GetMemoryDetails(MouseX, MouseY);

                //string tip = "Hello World\r\nThe Quick Brown Fox";
                IWin32Window win = this;
                CurrentTT.Show(tip, win, MouseX, MouseY-10);
                MouseMoved = false;
            }
        }


        // ***************************************************************************************************************************
        /// <summary>
        ///      Get pixel memory details
        /// </summary>
        /// <param name="_x">Mouse X</param>
        /// <param name="_y">Mouse Y</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public unsafe string GetMemoryDetails(int _x, int _y)
        {
            int Scale = DrawScale;
            _x = _x - MemoryPanel.Left + 1 - (Scale / 2);
            _y = _y - (MemoryPanel.Top - 2 - (Scale / 2));
            _x = _x / Scale;
            _y = _y / Scale;

            //CurrentAddress
            byte b = 0;
            int Address = 0;
            if (Mode == eMemoryMode.SpectrumScreen)
            {
                int offset_base = ((((_y >> 3) & 0x18) | (_y & 7)) << 8) | ((_y << 2) & 0xe0);
                int x = _x / 8;
                offset_base += x;

                Address = CurrentAddress + offset_base;
            }
            else if (Mode == eMemoryMode.LinearBitmap)
            {
                int offset_base = (_x / 8) + (_y * MemoryWidth);
                Address = CurrentAddress + offset_base;
            }
            else if (Mode == eMemoryMode.Layer2_256)
            {
                int offset_base = _x + (_y * MemoryWidth);
                Address = CurrentAddress + offset_base;
            }
            else if (Mode == eMemoryMode.Layer2_320)
            {
                int offset_base = _y + (_x * MemoryWidth);
                Address = CurrentAddress + offset_base;
            }
            else if (Mode == eMemoryMode.SixteenColour_Linear)
            {
                int offset_base = (_x / 2) + (_y * MemoryWidth);
                Address = CurrentAddress + offset_base;
            }
            else if (Mode == eMemoryMode.SixteenColour_Stripped)
            {
                int offset_base = _y + ((_x / 2) * MemoryWidth);
                Address = CurrentAddress + offset_base;
            }
            else if (Mode == eMemoryMode.Raw_Grayscale)
            {
                int offset_base = _x + (_y * MemoryWidth);
                Address = CurrentAddress + offset_base;
            }

            byte[] data = Peek(Address, 1);
            b = data[0];

            if (Address >= (2 * 1024 * 1024)) Address = 0;
            SMemWrite mwrite = CSpect.GetMemoryAccess(Address, ULAEnabledCheckbox.Checked);
            LastPhysicalPC = mwrite.PhysicalAddress;
            LastLogicalPC = mwrite.PC;

            int bank = mwrite.PhysicalAddress >> 13;
            int offs = mwrite.PhysicalAddress & 0x1fff;
            string txt = "Byte:     $" + b.ToString("X")+"\r\n"+
                         "PC:       $"+ mwrite.PC.ToString("X") + "\r\n" +
                         "Physical: $"+bank.ToString("X")+":$"+offs.ToString("X");

            return txt;
        }

        private void MemoryPanel_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (LastPhysicalPC != -1)
            {
                CSpect.Debugger( eDebugCommand.SetPhysicalBreakpoint, LastPhysicalPC );
                LastPhysicalPC = -1;
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw the spectrum screen
        /// </summary>
        /// <param name="pData"></param>
        /// <param name="pMem"></param>
        // ******************************************************************************************
        private unsafe void DrawSpectrumScreen(uint* pData, byte* pMem, int MemSize)
        {
            int index = 0;
            for (int y = 0; y < 192; y++)
            {
                int x = 0;
                while (x < 256)
                {
                    if (index >= MemSize) return;
                    int b = pMem[index++];
                    int offset_base = ((((y >> 3) & 0x18) | (y & 7)) << 8) | ((y << 2) & 0xe0);
                    offset_base = offset_base >> 5;
                    for (int xx = 0; xx < 8; xx++)
                    {
                        if ((b & 128) != 0)
                        {
                            pData[x + (offset_base * BackBuffer.Width)] = 0xffffffff;
                        }
                        else
                        {
                            pData[x + (offset_base * BackBuffer.Width)] = 0xff000000;
                        }
                        b <<= 1;
                        x++;
                    }
                }
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw a linear 1 bit memory dump
        /// </summary>
        /// <param name="pData"></param>
        /// <param name="pMem"></param>
        // ******************************************************************************************
        private unsafe void DrawLinearBitmap(uint* pData, byte* pMem, int MemSize)
        {
            int xpixels = MemoryWidth*8;
            if (xpixels > MAX_WIDTH) xpixels = MAX_WIDTH;

            int index = 0;
            for (int y = 0; y < MAX_HEIGHT; y++)
            {
                int baseoffset = (y * BackBuffer.Width);
                int x = 0;
                while (x < xpixels)
                {
                    if (index >= MemSize) return;
                    int b = pMem[index++];
                    for (int xx = 0; xx < 8; xx++)
                    {
                        if ((b & 128) != 0)
                        {
                            pData[x + baseoffset] = 0xffffffff;
                        }
                        else
                        {
                            pData[x + baseoffset] = 0xff000000;
                        }
                        b <<= 1;
                        x++;
                    }
                }
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw a linear 1 bit memory dump
        /// </summary>
        /// <param name="pData">Bitmap to draw into</param>
        /// <param name="pMem">Pointer to memory block</param>
        // ******************************************************************************************
        private unsafe void DrawLayer2_256Bitmap(uint* pData, byte* pMem, int MemSize)
        {
            int index = 0;
            for (int y = 0; y < MAX_HEIGHT; y++)
            {
                int x = 0;
                int CurrIndex = index;
                int WidthToRender = MemoryWidth;
                if (WidthToRender > MAX_WIDTH) WidthToRender = MAX_WIDTH;

                // Render a single space - maxing out at the buffer width
                while (x < WidthToRender)
                {
                    if (index >= MemSize) return;
                    int b = pMem[index++];

                    UInt32 col = ZXPalette.Get((b+PaletteOffset16) &0xff);
                    pData[x + (y * BackBuffer.Width)] = col;
                    x++;
                }
                index = CurrIndex + MemoryWidth;
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw a linear 1 bit memory dump
        /// </summary>
        /// <param name="pData">Bitmap to draw into</param>
        /// <param name="pMem">Pointer to memory block</param>
        // ******************************************************************************************
        private unsafe void DrawLayer2_320Bitmap(uint* pData, byte* pMem, int MemSize)
        {
            int index = 0;
            int size = MemSize;

            int WidthToRender = MemoryWidth;
            if (WidthToRender > MAX_HEIGHT) WidthToRender = MAX_HEIGHT;

            for (int x = 0; x < MAX_WIDTH; x++)
            {
                int y = 0;
                while (y < WidthToRender)
                {
                    if (index >= MemSize) return;           // Don't go past out buffer size
                    int b = pMem[index++];

                    UInt32 col = ZXPalette.Get((b+PaletteOffset16) & 0xff);
                    pData[x + (y*BackBuffer.Width)] = col;
                    y++;
                }
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw a linear 1 bit memory dump
        /// </summary>
        /// <param name="pData">Bitmap to draw into</param>
        /// <param name="pMem">Pointer to memory block</param>
        // ******************************************************************************************
        private unsafe void Draw16_Linear_Bitmap(uint* pData, byte* pMem, int MemSize)
        {
            int index = 0;
            for (int y = 0; y < MAX_HEIGHT; y++)
            {
                int x = 0;
                int CurrIndex = index;
                int WidthToRender = MemoryWidth*2;
                if (WidthToRender > MAX_WIDTH) WidthToRender = MAX_WIDTH;

                // Render a single space - maxing out at the buffer width
                while (x < WidthToRender)
                {
                    if (index >= MemSize) return;
                    int b = pMem[index++];

                    UInt32 col1 = ZXPalette.Get(((b >> 4) & 15) + PaletteOffset16);
                    UInt32 col2 = ZXPalette.Get((b & 15) + PaletteOffset16);
                    pData[x + (y * BackBuffer.Width)] = col1;
                    pData[x + 1 + (y * BackBuffer.Width)] = col2;
                    x += 2;
                }
                index = CurrIndex + MemoryWidth;
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw a linear 1 bit memory dump
        /// </summary>
        /// <param name="pData">Bitmap to draw into</param>
        /// <param name="pMem">Pointer to memory block</param>
        // ******************************************************************************************
        private unsafe void Draw16_Stripped_Bitmap(uint* pData, byte* pMem, int MemSize)
        {
            int index = 0;
            int size = MemSize;

            int WidthToRender = MemoryWidth;
            if (WidthToRender > MAX_HEIGHT) WidthToRender = MAX_HEIGHT;

            for (int x = 0; x < MAX_WIDTH; x+=2)
            {
                int y = 0;
                while (y < WidthToRender)
                {
                    if (index >= MemSize) return;           // Don't go past out buffer size
                    int b = pMem[index++];

                    UInt32 col1 = ZXPalette.Get(((b>>4)&15) + PaletteOffset16);
                    UInt32 col2 = ZXPalette.Get((b&15) + PaletteOffset16);
                    pData[x + (y * BackBuffer.Width)] = col1;
                    pData[x + 1 + (y * BackBuffer.Width)] = col2;
                    y++;
                }
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw a linear 1 bit memory dump
        /// </summary>
        /// <param name="pData">Bitmap to draw into</param>
        /// <param name="pMem">Pointer to memory block</param>
        // ******************************************************************************************
        private unsafe void DrawRAWBitmap(uint* pData, byte* pMem, int MemSize)
        {
            int index = 0;
            for (int y = 0; y < MAX_HEIGHT; y++)
            {
                int x = 0;
                int CurrIndex = index;
                int WidthToRender = MemoryWidth;
                if (WidthToRender > MAX_WIDTH) WidthToRender = MAX_WIDTH;

                // Render a single space - maxing out at the buffer width
                while (x < WidthToRender)
                {
                    if (index >= MemSize) return;
                    uint b = (UInt32) pMem[index++];

                    UInt32 col = 0xff000000 | b | (b << 8) | (b << 16);
                    pData[x + (y * BackBuffer.Width)] = col;
                    x++;
                }
                index = CurrIndex + MemoryWidth;
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
            MemoryViewerPlugin.Active = false;
            MemoryViewerPlugin.form = null;
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
        }


        // ******************************************************************************************
        /// <summary>
        ///     256 or 16 colour mode selection
        /// </summary>
        // ******************************************************************************************
        private void SpriteModeCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            ComboBox b = sender as ComboBox;
            switch (b.SelectedIndex)
            {
                case 0: 
                    Mode = eMemoryMode.SpectrumScreen; break;
                case 1: 
                    Mode = eMemoryMode.LinearBitmap; break;
                case 2:
                    Mode = eMemoryMode.Layer2_256; break;
                case 3:
                    Mode = eMemoryMode.Layer2_320; break;
                case 4:
                    Mode = eMemoryMode.SixteenColour_Linear; break;
                case 5:
                    Mode = eMemoryMode.SixteenColour_Stripped; break;
                case 6:
                    Mode = eMemoryMode.Raw_Grayscale; break;
                default:
                    Mode = eMemoryMode.LinearBitmap; break;
            }

            DoClear = true;
            this.Invalidate();
        }

        private void MemoryViewerForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Plugin.OpenMemViewerWindow = false;
            MemoryViewerPlugin.Active = false;
        }

        // ***********************************************************************************************
        /// <summary>
        ///     Number of bytes per line changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ***********************************************************************************************
        private void WidthCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            string value = WidthCombo.Text;
            long v = ConvertNumber(value);
            if (v >= 0)
            {
                MemoryWidth = (int)v;
                DoClear = true;
            }
        }


        // ***********************************************************************************************
        /// <summary>
        ///     Memory width changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ***********************************************************************************************
        private void WidthCombo_SelectionChangeCommitted(object sender, EventArgs e)
        {
            string value = WidthCombo.Text;
            long v = ConvertNumber(value);
            if (v >= 0)
            {
                MemoryWidth = (int)v;
                DoClear = true;
            }
        }

        // ***********************************************************************************************
        /// <summary>
        ///     Memory Width typing changed value
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ***********************************************************************************************
        private void WidthCombo_KeyUp(object sender, KeyEventArgs e)
        {
            string value = WidthCombo.Text;
            long v = ConvertNumber(value);
            if (v >= 0)
            {
                MemoryWidth = (int)v;
                DoClear = true;
            }
        }

        // ***********************************************************************************************
        /// <summary>
        ///     Physical address changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ***********************************************************************************************
        private void BankOffsetBox_TextChanged(object sender, EventArgs e)
        {
            string text = BankOffsetBox.Text;

            // Bank:offset?
            long value = -1;
            int colonindex = text.IndexOf(':');
            if (colonindex >= 0)
            {
                string bank = text.Substring(0, colonindex);
                string offset = text.Substring(colonindex + 1);

                long b = ConvertNumber(bank);
                long o = ConvertNumber(offset);
                if (b >= 0 && o >= 0)
                {
                    value = (b * 8192) + o;
                }
                else
                {
                    // invalid address
                    return;
                }
            }
            else
            {
                // FULL physical address
                long b = ConvertNumber(text);
                if(b >= 0)
                {
                    if (b > (2 * 1024 * 1024)) b = (2 * 1024 * 1024)-1;
                }
                value = b;
            }
            CurrentAddress = (int)value;
        }

        // ***********************************************************************************************
        /// <summary>
        ///     Set Drawing Scale
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ***********************************************************************************************
        private void ScaleCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            ComboBox b = sender as ComboBox;
            DrawScale = b.SelectedIndex+1;
            if (DrawScale < 1) DrawScale = 1;
            if (DrawScale > 9) DrawScale = 9;
        }


        // ***********************************************************************************************
        /// <summary>
        ///     Memory buffer size changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ***********************************************************************************************
        private void BuffSizeBox_TextChanged(object sender, EventArgs e)
        {
            long value = ConvertNumber(BuffSizeBox.Text);
            if (value > 0)
            {
                if (value > (2 * 1024 * 1024)) value = (2 * 1024 * 1024);
                BufferSize = (int)value;
                DoClear = true;

                // reallocate buffer
                MemBuffer = new byte[BufferSize];
            }
        }


        // ***********************************************************************************************
        /// <summary>
        ///     Convert a string to a long (decimal or hex)
        /// </summary>
        /// <param name="text">string to convert</param>
        /// <returns>
        ///     LONG converted number
        /// </returns>
        // ***********************************************************************************************
        public long ConvertNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            text = text.ToLower();

            long value = 0;
            bool ishex = false;
            if (text[0]=='$') ishex = true;
            if (text.Length >= 2)
            {
                if (text[0] == '0' && text[1] == 'x') ishex = true;
            }

            // Read HEX number
            int index = 0;
            if (ishex)
            {
                // $ or 0x?
                if (text[0] == '$') index = 1;
                else index = 1;

                while (index < text.Length)
                {
                    char c = text[index++];
                    if((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))
                    {
                        for (int i = 0; i < HEX.Length; i++)
                        {
                            if(c == HEX[i])
                            {
                                value *= 16;
                                value += i;
                            }
                        }
                    }
                    else
                    {
                        // invalid character
                        return value;
                    }
                }
            }
            else
            {
                while (index<text.Length)
                {
                    char c = text[index++];
                    if (c >= '0' && c <= '9')
                    {
                        value *= 10;
                        value += (int) (c - '0');
                    }
                    else
                    {
                        // invalid character
                        return value;
                    }
                }
            }
            return value;
        }


        // ************************************************************************************************************************
        /// <summary>
        ///     Select a palette
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ************************************************************************************************************************
        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            int sel = PaletteCombo.SelectedIndex;
            switch (sel)
            {
                case 0: Plugin.PaletteNumber = 0; break;
                case 1: Plugin.PaletteNumber = 4; break;
                case 2: Plugin.PaletteNumber = 1; break;
                case 3: Plugin.PaletteNumber = 5; break;
                case 4: Plugin.PaletteNumber = 2; break;
                case 5: Plugin.PaletteNumber = 6; break;
                case 6: Plugin.PaletteNumber = 3; break;
                case 7: Plugin.PaletteNumber = 7; break;
                default:
                    Plugin.PaletteNumber = 1; 
                    break;
            }
        }


        private void OffsetCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            PaletteOffset = OffsetCombo.SelectedIndex;
            PaletteOffset16 = OffsetCombo.SelectedIndex*16;
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void SnapShotButton_Click(object sender, EventArgs e)
        {
            DoSnapshot = true;
        }

        private void PauseButton_Click(object sender, EventArgs e)
        {
            CSpect.SetGlobal(eGlobal.pause, !((bool)CSpect.GetGlobal(eGlobal.pause)));
        }



        #region Mouse over
        bool MouseActive = false;

        private void SpritePanel_MouseEnter(object sender, EventArgs e)
        {
            MouseActive = true;
        }

        private void SpritePanel_MouseLeave(object sender, EventArgs e)
        {
            MouseActive = false;

            IWin32Window win = this;
            CurrentTT.Hide(win);
        }
        #endregion

        private void SpritePanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (MouseX != e.X || MouseY != e.Y)
            {
                MouseX = e.X + MemoryPanel.Left;
                MouseY = e.Y + MemoryPanel.Top-10;
                MouseMoved = true;

                //this.Invalidate();
                //Application.DoEvents();
            }
        }

        // **************************************************************************************************
        /// <summary>
        ///     Decrease memory by 8k (1 bank)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // **************************************************************************************************
        private void DecreaseMemoryButton_Click(object sender, EventArgs e)
        {
            string text = BankOffsetBox.Text;

            // Bank:offset?
            long value = -1;
            int colonindex = text.IndexOf(':');
            if (colonindex >= 0)
            {
                string bank = text.Substring(0, colonindex);
                string offset = text.Substring(colonindex + 1);

                long b = ConvertNumber(bank);
                if (b >= 0)
                {
                    b--;
                    if (b < 0) b = 254;
                    BankOffsetBox.Text = "$" + b.ToString("X") + ":" + offset;
                }
            }
            else
            {
                // FULL physical address
                long b = ConvertNumber(text);
                if (b >= 0)
                {
                    b -= 8192;
                    if(b<0) b = (2*1024*1024)-8192;
                    BankOffsetBox.Text = "$" + b.ToString("X");
                }
                value = b;
            }
        }

        private void IncreaseMemoryButton_Click(object sender, EventArgs e)
        {
            string text = BankOffsetBox.Text;

            // Bank:offset?
            long value = -1;
            int colonindex = text.IndexOf(':');
            if (colonindex >= 0)
            {
                string bank = text.Substring(0, colonindex);
                string offset = text.Substring(colonindex + 1);

                long b = ConvertNumber(bank);
                if (b >= 0)
                {
                    b++;
                    if (b > 256) b = 0;
                    BankOffsetBox.Text = "$" + b.ToString("X") + ":" + offset;
                }
            }
            else
            {
                // FULL physical address
                long b = ConvertNumber(text);
                if (b >= 0)
                {
                    b += 8192;
                    if (b < (2 * 1024 * 1024)) b = 0;
                    BankOffsetBox.Text = "$" + b.ToString("X");
                }
                value = b;
            }

        }

        /// <summary>
        ///     Step game a SINGLE frame
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StepButton_Click(object sender, EventArgs e)
        {
            Plugin.DoStep = 2;
        }
    }
}

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
using Plugin;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Profiler
{
    public partial class ProfilerForm : Form
    {
        public const int PROF_X_START = 32;
        public const int PROF_X_END = 32;

        /// <summary>A copy of the profile samples</summary>
        public int[] ProfileRead;

        /// <summary>The font used for drawing</summary>
        System.Drawing.Font drawFont;
        /// <summary>A black brush for drawing text</summary>
        System.Drawing.SolidBrush drawBrush;
        /// <summary>A ref brush drawing the text when the copper hasn't been set at this location</summary>
        System.Drawing.SolidBrush drawRedBrush;
        /// <summary>Brush black colour</summary>
        System.Drawing.Pen drawBlackPen;
        /// <summary>A black brush for drawing text</summary>
        System.Drawing.SolidBrush drawBlack;
        /// <summary>A white brush for drawing text</summary>
        System.Drawing.SolidBrush drawWhite;

        /// <summary>The start address of the current sample area</summary>
        int StartAddress = 0;
        /// <summary>The end address of the current sample area</summary>
        int EndAddress = 2*1024*1024;
        /// <summary>The number of cells in the sample area</summary>
        int NumCells = -1;

        /// <summary>First time in for font creation stuff</summary>
        bool first_time = true;
        int MouseZoomX;
        int MouseZoomY;
        int MouseZoomWheel;

        int MouseX;
        int MouseY;

        bool MouseMoving = false;
        int MouseButX_Start;
        int MouseButY_Start;
        int MouseBut_StartAdd;

        /// <summary>Profile count for this cell</summary>
        int[] Blocks;
        /// <summary>Start Address of each cell</summary>
        int[] BlockAdd;
        /// <summary>Number of addresses in each cell</summary>
        double CellSize = 1;
        /// <summary>The largest profile value the currently selected area</summary>
        int max_value = 0;
        /// <summary>The largest profile value in ALL of memory</summary>
        int max_sample = 0;

        bool ShowDisassembly = false;
        int DissStart = -1;

        /// <summary>heat map colours</summary>
        int[] PerfColours ={
            0,
            0xFFF9AF,
            0xFFE693,
            0xFFC9A8,
            0xFFBAC5,
            0xFF6D68
        };

        /// <summary>CSpect callback interface</summary>
        iCSpect CSpect;

        #region Create/Destroy
        // ******************************************************************************************
        /// <summary>
        ///     Create profiler window
        /// </summary>
        // ******************************************************************************************
        public ProfilerForm(iCSpect _CSpect)
        {
            CSpect = _CSpect;
            InitializeComponent();

            this.Refresh();
            this.Invalidate(true);
            ProfileGroup.Invalidate(true);
            ProfileGroup.MouseDown += ProfileGroup_MouseDown;
            ProfileGroup.MouseUp += ProfileGroup_MouseUp;
            ProfileGroup.MouseMove += ProfileGroup_MouseMove;
            Application.DoEvents();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Close and free up
        /// </summary>
        // ******************************************************************************************
        private void CopperDissForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            ProfilerPlugin.Active = false;
            ProfilerPlugin.form = null;
        }
        #endregion

        #region Form events
        // ************************************************************************************************************************
        /// <summary>
        ///     Handle mouse moving - especially while selecting areas
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ************************************************************************************************************************
        private void ProfileGroup_MouseMove(object sender, MouseEventArgs e)
        {
            MouseX = e.X;
            MouseY = e.Y;
            if (MouseMoving)
            {
                ProfileGroup.Invalidate(true);
            }
            else if(ShowDisassembly)
            {
                int AddMouseX = e.X - PROF_X_START;
                if (AddMouseX > BlockAdd.Length) return;
                if (AddMouseX < 0) return;
                DissStart = BlockAdd[AddMouseX];
                DisGroup.Invalidate(true);
            }
        }

        // ************************************************************************************************************************
        /// <summary>
        ///     START profiling
        /// </summary>
        // ************************************************************************************************************************
        private void StartButton_Click(object sender, EventArgs e)
        {
            // clear the
            ProfileRead = (int[])CSpect.GetGlobal(eGlobal.profile_read);
            for (int i = 0; i < ProfileRead.Length; i++)
            {
                ProfileRead[i] = 0;
            }
            StartAddress = 0;
            EndAddress = 2 * 1024 * 1024;
            DoInvalidate();

            Start_Button.Enabled = false;
            Stop_Button.Enabled = true;
        }

        // ************************************************************************************************************************
        /// <summary>
        ///     Stop profiling - copy the profile data, and set the profile window rendering
        /// </summary>
        // ************************************************************************************************************************
        private void Stop_Button_Click(object sender, EventArgs e)
        {
            Start_Button.Enabled = true;
            Stop_Button.Enabled = false;

            StartAddress = 0;
            EndAddress = 2 * 1024 * 1024;
            max_sample = 0;

            // we need to copy the array, as the emulator will continue to accumulate values into the provided array
            int[] localProfileRead = (int[])CSpect.GetGlobal(eGlobal.profile_read);
            ProfileRead = new int[localProfileRead.Length];
            for (int i = 0; i < ProfileRead.Length; i++)
            {
                ProfileRead[i] = localProfileRead[i];
                if (max_sample < ProfileRead[i]) max_sample = ProfileRead[i];
            }
            DoInvalidate();
        }


        // ******************************************************************************************
        /// <summary>
        ///     Reset the "zoom" on the profile window
        /// </summary>
        // ******************************************************************************************
        private void ResetButton_Click(object sender, EventArgs e)
        {
            DissStart = -1;
            StartAddress = 0;
            EndAddress = 2 * 1024 * 1024;
            DoInvalidate();
        }

        #endregion

        // ******************************************************************************************
        /// <summary>
        ///     Mouse button up
        /// </summary>
        // ******************************************************************************************
        private void ProfileGroup_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (!MouseMoving) return;

                int x1 = MouseButX_Start - PROF_X_START;
                int x2 = e.X - PROF_X_START;
                if (x1 > x2)
                {
                    int t = x1;
                    x1 = x2;
                    x2 = t;
                }
                if (x1 < 0) x1 = 0;
                if (x2 < 0) x1 = 0;
                if (x1 == 0 && x2 == 0) return;


                DissStart = -1;
                EndAddress = BlockAdd[x2];
                StartAddress = BlockAdd[x1];
                MouseMoving = false;
                DoInvalidate();
            }
            if(e.Button == MouseButtons.Left)
            {
                ShowDisassembly = false;
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Mouse button down
        /// </summary>
        // ******************************************************************************************
        private void ProfileGroup_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (BlockAdd == null) return;
                MouseX = MouseButX_Start = e.X;
                MouseY = MouseButY_Start = e.Y;
                if (MouseX < 0) return;

                //double BlockSize;
                //int NumCells;
                //tProfileScalers(out NumCells, out BlockSize);

                MouseBut_StartAdd = StartAddress + (int)(MouseButX_Start * CellSize);
                MouseMoving = true;
            }else if( e.Button == MouseButtons.Left && BlockAdd!=null)
            {
                int AddMouseX = e.X - PROF_X_START;
                if (AddMouseX > BlockAdd.Length) return;
                if (AddMouseX < 0) AddMouseX = 0;
                if (AddMouseX >= BlockAdd.Length) AddMouseX = BlockAdd.Length - 1;
                DissStart = BlockAdd[AddMouseX];
                DoInvalidate();
                ShowDisassembly = true;
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
            MouseWheel += ProfilerForm_MouseWheel;
        }


        // ******************************************************************************************
        /// <summary>
        ///     Mouse wheel moving
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        // ******************************************************************************************
        private void ProfilerForm_MouseWheel(object sender, MouseEventArgs e)
        {
            MouseZoomX = e.X - PROF_X_START;
            MouseZoomY = e.Y;
            MouseZoomWheel = e.Delta;
            ProfileGroup.Invalidate();
            Application.DoEvents();
        }


        // ************************************************************************************************************************
        /// <summary>
        ///     Invalidate the profile window
        /// </summary>
        // ************************************************************************************************************************
        public void DoInvalidate()
        {
            ProfileGroup.Invalidate(true);
            DisGroup.Invalidate(true);
            Invalidate(true);
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

            drawFont = new System.Drawing.Font("Arial", 12);
            drawBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Black);
            drawRedBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Red);

            drawBlack = new SolidBrush(Color.FromArgb(255, 0, 0, 0));
            drawWhite = new SolidBrush(Color.FromArgb(255, 255, 255, 255));
            drawBlackPen = new Pen(drawBlack);

            first_time = false;
        }


        // *******************************************************************************************************************
        /// <summary>
        ///     Fill the cells by scaling the memory range into the cells.
        /// </summary>
        /// <param name="_StartAddress"></param>
        /// <param name="_EndAddress"></param>
        // *******************************************************************************************************************
        private void FillCells(int _StartAddress, int _EndAddress)
        {
            NumCells = (Width - 64);
            Blocks = new int[NumCells + 1];
            BlockAdd = new int[NumCells + 1];

            max_value = 0;
            CellSize = ((EndAddress - StartAddress) / (double)NumCells);
            double Add = StartAddress;
            for (int i = 0; i < NumCells; i++)
            {
                int v;
                if (CellSize > 1)
                {
                    v = 0;
                    for(int b = (int)Add; b < ((int)Add + (int)CellSize); b++)
                    {
                        v += ProfileRead[b];
                    }
                    v = (int) ((double)v / CellSize);   // average cell data
                }
                else
                {
                    v = ProfileRead[(int)Add];
                }
                if (max_value < v) max_value = v;
                Blocks[i] = v;
                BlockAdd[i] = (int)Add;
                Add += CellSize;
            }
        }


        // *******************************************************************************************************************
        /// <summary>
        ///     Render the copper disassembly
        /// </summary>
        /// <param name="_g">graphics rendering interface</param>
        // *******************************************************************************************************************
        public void DrawProfile(Graphics _g)
        {
            CreateFont();
            if (ProfileRead == null) return;

            int h = drawFont.Height;
            float x = 32.0F;
            float y = 220.0F;
            //SizeF size = _g.MeasureString("MMMMMM", drawFont);
            //int w = (int) size.Width;
            //int rows = this.Height / h;

            // Print selection
            if (MouseMoving)
            {
                int x1 = MouseButX_Start;
                int x2 = MouseX;
                if(x1 > x2)
                {
                    x1 = MouseX;
                    x2 = MouseButX_Start;
                }

                int w = x2 - x1;
                _g.FillRectangle(drawRedBrush, x1, y - 200, w, 200);
            }


            FillCells(StartAddress, EndAddress);

            _g.DrawLine(drawBlackPen, x, y+2, x+NumCells, y+2);
            for(int i = (int)x; i < (x+NumCells); i+=50)
            {
                _g.DrawLine(drawBlackPen, i, y + 2, i, y + 10);
            }


            float xx = x;
            for (int i = (int)0; i < NumCells; i += 100)
            {
                string s = String.Format("${0:X6}", BlockAdd[i]);
                _g.DrawString(s, drawFont, drawBlack, xx-4, y+8);
                xx += 100;
            }


            // scale everything to the max size in range
            if (max_value == 0) max_value = 1;
            double scaler = 200.0/max_value;
            for (int i = 0; i < NumCells; i++)
            {
                int v = (int)((double)Blocks[i] * scaler);
                _g.DrawLine(drawBlackPen, x, y, x, y - v);
                x++;
            }
        }


        // ******************************************************************************************
        /// <summary>
        ///     Draw the Profile group
        /// </summary>
        // ******************************************************************************************
        private void ProfilerRender_Paint(object sender, PaintEventArgs e)
        {
            /*
            if (MouseZoomWheel != 0)
            {
            }*/

            Graphics g = e.Graphics;
            DrawProfile(g);
        }


        // ****************************************************************************************************************
        /// <summary>
        ///     Find the address in the blocks, and colour it based on hits
        /// </summary>
        /// <param name="_address">Address to get profile colour of</param>
        /// <returns></returns>
        // ****************************************************************************************************************
        public SolidBrush GetColour(int _address)
        {
            int index = _address - StartAddress;
            if (index < 0) return null;

            index = (int) Math.Ceiling(index / CellSize);
            if (index >= Blocks.Length) return null;

            if (index < 0) index = 0;
            if (index >= Blocks.Length) index = Blocks.Length - 1;
            int prof = Blocks[index];
            double scaler = 200.0 / max_value;
            double d = prof * scaler;
            double col_scaler = (((double)PerfColours.Length-1)+0.5) / 200.0;
            prof = (int)Math.Round(d * col_scaler);
            if (prof >= PerfColours.Length) prof = PerfColours.Length - 1;
            int col = PerfColours[prof];
            if (col == 0) return null;
            SolidBrush brush = new SolidBrush(Color.FromArgb(255, (col>>16)&0xff, (col >> 8) & 0xff, col& 0xff) );
            return brush;
        }

        // ****************************************************************************************************************
        /// <summary>
        ///     Render the disassembly group
        /// </summary>
        // ****************************************************************************************************************
        private void DisGroup_Paint(object sender, PaintEventArgs e)
        {
            CreateFont();
            if (ProfileRead == null) return;

            Graphics g = e.Graphics;
            int h = drawFont.Height;

            if (DissStart < 0) return;
            int Add = DissStart;
            int gh = DisGroup.Height - 10;

            int yy= 15;
            int xx = 4;
            while((yy+h)<gh)
            {
                SolidBrush b = GetColour(Add);
                if(b!=null) g.FillRectangle(b, xx - 1, yy, DisGroup.Width - 8, h);
                DissassemblyLine l = CSpect.DissasembleMemory(Add, true);
                string label = CSpect.LookUpSymbol(Add);
                if (label != null)
                {
                    int inof = label.IndexOf('@');
                    if( inof >= 0 )
                    {
                        label = label.Substring(inof);
                    }
                    if (label.Length > 20)
                    {
                        label = label.Substring(0, 20);
                    }
                    g.DrawString(label, drawFont, drawBlack, xx, yy);
                }
                g.DrawString(l.line, drawFont, drawBlack, xx+220, yy);
                yy += h;
                Add += l.bytes;
            }
        }
    }
}

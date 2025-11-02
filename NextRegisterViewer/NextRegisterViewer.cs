using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Plugin;

namespace NextRegisterViewer
{
    class WindowWrapper : IWin32Window
    {
        private IntPtr mWindowHandle;
        public IntPtr Handle
        {
            get { return mWindowHandle; }
        }

        public WindowWrapper(IntPtr _handle)
        {
            mWindowHandle = _handle;
        }
    }

    // *********************************************************************************************************
    /// <summary>
    ///     The copper disassembler
    /// </summary>
    // *********************************************************************************************************
    class NextRegisterViewerPlugin : iPlugin
    {
        public const int DELAY_COUNTER = 120;

        /// <summary>CSpect emulator interface</summary>
        public iCSpect CSpect;
        public static bool Active;
        public static NextRegistersForm form;

        public RegDetails Regs;

        bool doInvalidate = false;
        bool OpenNextRegWindow = false;

        WindowWrapper hwndWrapper;
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
            Debug.WriteLine("Next Register Viewer added");

            Regs = new RegDetails();
            CSpect = _CSpect;
            IntPtr handle = (IntPtr)CSpect.GetGlobal(eGlobal.window_handle);
            hwndWrapper = new WindowWrapper(handle);

            // Detect keypress for starting disassembler
            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO("<ctrl><shift>r", eAccess.KeyPress, 0));                   // Key press callback

            for (int i = 0; i < 256; i++)
            {
                ports.Add(new sIO(i, eAccess.NextReg_Write));
            }
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
                OpenNextRegWindow = true;
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
        }

        // ******************************************************************************************
        /// <summary>
        ///     Read access type - not used
        /// </summary>
        /// <param name="_type"></param>
        /// <param name="_address"></param>
        /// <param name="_isvalid"></param>
        /// <returns></returns>
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
        ///     Called once an emulator frame - update copper if "Active"
        /// </summary>
        // ******************************************************************************************
        public void Tick()
        {
            if (!Active) return;

            for (int i=0;i<256;i++)
            {
                if (Regs.RegisterIsWritten[i] > 0)
                {
                    Regs.RegisterIsWritten[i]--;
                    if (Regs.RegisterIsWritten[i] == 0)
                    {
                        doInvalidate = true;
                    }
                }
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Called once an OS emulator frame - do all UI rendering, opening windows etc here.
        /// </summary>
        // ******************************************************************************************
        public void OSTick()
        {
            if (OpenNextRegWindow)
            {
                if (!Active)
                {
                    Active = true;
                    form = new NextRegistersForm(Regs, CSpect);
                    form.Show();
                }
                OpenNextRegWindow = false;
            }


            if (doInvalidate)
            {
                if (form != null)
                {
                    form.Invalidate();     // refresh IF it's changed
                    Application.DoEvents();
                }
                doInvalidate = false;
            }

            GetWindows();
        }

        public void GetWindows()
        {
            // Clip Window L2
            Regs.ClipWindowLayer2[0] = CSpect.GetNextRegister(0x18, 0);
            Regs.ClipWindowLayer2[1] = CSpect.GetNextRegister(0x18, 1);
            Regs.ClipWindowLayer2[2] = CSpect.GetNextRegister(0x18, 2);
            Regs.ClipWindowLayer2[3] = CSpect.GetNextRegister(0x18, 3);

            // Clip Window Sprites
            Regs.ClipWindowSprites[0] = CSpect.GetNextRegister(0x19, 0);
            Regs.ClipWindowSprites[1] = CSpect.GetNextRegister(0x19, 1);
            Regs.ClipWindowSprites[2] = CSpect.GetNextRegister(0x19, 2);
            Regs.ClipWindowSprites[3] = CSpect.GetNextRegister(0x19, 3);

            // Clip Window ULA
            Regs.ClipWindowULA[0] = CSpect.GetNextRegister(0x1A, 0);
            Regs.ClipWindowULA[1] = CSpect.GetNextRegister(0x1A, 1);
            Regs.ClipWindowULA[2] = CSpect.GetNextRegister(0x1A, 2);
            Regs.ClipWindowULA[3] = CSpect.GetNextRegister(0x1A, 3);

            // Clip Window Tiles
            Regs.ClipWindowTilemap[0] = CSpect.GetNextRegister(0x1B, 0);
            Regs.ClipWindowTilemap[1] = CSpect.GetNextRegister(0x1B, 1);
            Regs.ClipWindowTilemap[2] = CSpect.GetNextRegister(0x1B, 2);
            Regs.ClipWindowTilemap[3] = CSpect.GetNextRegister(0x1B, 3);
        }

        // ******************************************************************************************
        /// <summary>
        ///     Write access type - not used
        /// </summary>
        /// <param name="_type"></param>
        /// <param name="_port"></param>
        /// <param name="_value"></param>
        /// <returns></returns>
        // ******************************************************************************************
        public bool Write(eAccess _type, int _port, int _id, byte _value)
        {
            if(Regs.NextRegisters[_port] != _value)
            {
                Regs.NextRegisters[_port] = _value;
                Regs.RegisterIsWritten[_port] = DELAY_COUNTER;
                doInvalidate = true;

                if( _port == 0x18)
                {
                    // Clip Window L2
                    Regs.ClipWindowLayer2[0] = CSpect.GetNextRegister(0x18, 0);
                    Regs.ClipWindowLayer2[1] = CSpect.GetNextRegister(0x18, 1);
                    Regs.ClipWindowLayer2[2] = CSpect.GetNextRegister(0x18, 2);
                    Regs.ClipWindowLayer2[3] = CSpect.GetNextRegister(0x18, 3);
                }
                else if (_port == 0x19)
                {
                    // Clip Window Sprites
                    Regs.ClipWindowSprites[0] = CSpect.GetNextRegister(0x19, 0);
                    Regs.ClipWindowSprites[1] = CSpect.GetNextRegister(0x19, 1);
                    Regs.ClipWindowSprites[2] = CSpect.GetNextRegister(0x19, 2);
                    Regs.ClipWindowSprites[3] = CSpect.GetNextRegister(0x19, 3);
                }
                else if (_port == 0x1A)
                {
                    // Clip Window ULA
                    Regs.ClipWindowULA[0] = CSpect.GetNextRegister(0x1A, 0);
                    Regs.ClipWindowULA[1] = CSpect.GetNextRegister(0x1A, 1);
                    Regs.ClipWindowULA[2] = CSpect.GetNextRegister(0x1A, 2);
                    Regs.ClipWindowULA[3] = CSpect.GetNextRegister(0x1A, 3);
                }
                else if (_port == 0x1B)
                {
                    // Clip Window Tiles
                    Regs.ClipWindowTilemap[0] = CSpect.GetNextRegister(0x1B, 0);
                    Regs.ClipWindowTilemap[1] = CSpect.GetNextRegister(0x1B, 1);
                    Regs.ClipWindowTilemap[2] = CSpect.GetNextRegister(0x1B, 2);
                    Regs.ClipWindowTilemap[3] = CSpect.GetNextRegister(0x1B, 3);
                }
                else if (_port == 0x1C)
                {
                    // reset clip window index
                    if ((_value & 1) != 0) Regs.ClipWindowLayer2[4] = 0;
                    if ((_value & 2) != 0) Regs.ClipWindowSprites[4] = 0;
                    if ((_value & 4) != 0) Regs.ClipWindowULA[4] = 0;
                    if ((_value & 8) != 0) Regs.ClipWindowTilemap[4] = 0;
                }
            }

            return false;
        }
    }
}

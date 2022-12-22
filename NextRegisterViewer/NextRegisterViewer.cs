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
            ports.Add(new sIO("<ctrl><alt>r", eAccess.KeyPress, 0));                   // Key press callback

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
                if (Active) return true;
                Active = true;
                form = new NextRegistersForm(Regs, CSpect);
                form.Show();
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

            for(int i=0;i<256;i++)
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

            if (doInvalidate) form.Invalidate();     // refresh IF it's changed
            Application.DoEvents();

            doInvalidate = false;
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
                    int index = Regs.ClipWindowLayer2[4];
                    Regs.ClipWindowLayer2[index] = _value;
                    index = (index + 1) & 0x3;
                    Regs.ClipWindowLayer2[4] = index;
                }
                else if (_port == 0x19)
                {
                    // Clip Window Sprites
                    int index = Regs.ClipWindowSprites[4];
                    Regs.ClipWindowSprites[index] = _value;
                    index = (index + 1) & 0x3;
                    Regs.ClipWindowSprites[4] = index;
                }
                else if (_port == 0x1A)
                {
                    // Clip Window ULA
                    int index = Regs.ClipWindowULA[4];
                    Regs.ClipWindowULA[index] = _value;
                    index = (index + 1) & 0x3;
                    Regs.ClipWindowULA[4] = index;
                }
                else if (_port == 0x1B)
                {
                    // Clip Window Tiles
                    int index = Regs.ClipWindowTilemap[4];
                    Regs.ClipWindowTilemap[index] = _value;
                    index = (index + 1) & 0x3;
                    Regs.ClipWindowTilemap[4] = index;
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

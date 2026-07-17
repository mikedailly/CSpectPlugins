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

        bool FirstRun = true;
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
            Console.WriteLine(" Next Register Viewer added");

            Regs = new RegDetails();
            CSpect = _CSpect;
            IntPtr handle = (IntPtr)CSpect.GetGlobal(eGlobal.window_handle);
            hwndWrapper = new WindowWrapper(handle);

            // Detect keypress for starting disassembler
            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO("<ctrl><shift>r", eAccess.KeyPress, 0));                   // Key press callback
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
        ///     Read all registers
        /// </summary>
        // ******************************************************************************************
        public void ReadRegisters()
        {
            int i,r, r1, r2, r3, r4;
            for (i = 0; i < 256; i++)
            {
                switch (i)
                {
                    case 0x18:
                        {
                            // Clip Window L2
                            r1 = CSpect.GetNextRegister(0x18, 0);
                            r2 = CSpect.GetNextRegister(0x18, 1);
                            r3 = CSpect.GetNextRegister(0x18, 2);
                            r4 = CSpect.GetNextRegister(0x18, 3);
                            if (r1 != Regs.ClipWindowLayer2[0] || r2 != Regs.ClipWindowLayer2[1] | r3 != Regs.ClipWindowLayer2[2] || r4 != Regs.ClipWindowLayer2[3])
                            {
                                Regs.ClipWindowLayer2[0] = r1;
                                Regs.ClipWindowLayer2[1] = r2;
                                Regs.ClipWindowLayer2[2] = r3;
                                Regs.ClipWindowLayer2[3] = r4;
                                Regs.RegisterIsWritten[0x18] = DELAY_COUNTER;
                                doInvalidate = true;
                            }
                            break;
                        }
                    case 0x19:
                        {
                            // Clip Window L2
                            r1 = CSpect.GetNextRegister(0x19, 0);
                            r2 = CSpect.GetNextRegister(0x19, 1);
                            r3 = CSpect.GetNextRegister(0x19, 2);
                            r4 = CSpect.GetNextRegister(0x19, 3);
                            if (r1 != Regs.ClipWindowSprites[0] || r2 != Regs.ClipWindowSprites[1] | r3 != Regs.ClipWindowSprites[2] || r4 != Regs.ClipWindowSprites[3])
                            {
                                Regs.ClipWindowSprites[0] = r1;
                                Regs.ClipWindowSprites[1] = r2;
                                Regs.ClipWindowSprites[2] = r3;
                                Regs.ClipWindowSprites[3] = r4;
                                Regs.RegisterIsWritten[0x19] = DELAY_COUNTER;
                                doInvalidate = true;
                            }
                            break;
                        }
                    case 0x1A:
                        {
                            // Clip Window L2
                            r1 = CSpect.GetNextRegister(0x1A, 0);
                            r2 = CSpect.GetNextRegister(0x1A, 1);
                            r3 = CSpect.GetNextRegister(0x1A, 2);
                            r4 = CSpect.GetNextRegister(0x1A, 3);
                            if (r1 != Regs.ClipWindowULA[0] || r2 != Regs.ClipWindowULA[1] | r3 != Regs.ClipWindowULA[2] || r4 != Regs.ClipWindowULA[3])
                            {
                                Regs.ClipWindowULA[0] = r1;
                                Regs.ClipWindowULA[1] = r2;
                                Regs.ClipWindowULA[2] = r3;
                                Regs.ClipWindowULA[3] = r4;
                                Regs.RegisterIsWritten[0x1A] = DELAY_COUNTER;
                                doInvalidate = true;
                            }
                            break;
                        }
                    case 0x1B:
                        {
                            // Clip Window L2
                            r1 = CSpect.GetNextRegister(0x1B, 0);
                            r2 = CSpect.GetNextRegister(0x1B, 1);
                            r3 = CSpect.GetNextRegister(0x1B, 2);
                            r4 = CSpect.GetNextRegister(0x1B, 3);
                            if (r1 != Regs.ClipWindowTilemap[0] || r2 != Regs.ClipWindowTilemap[1] | r3 != Regs.ClipWindowTilemap[2] || r4 != Regs.ClipWindowTilemap[3])
                            {
                                Regs.ClipWindowTilemap[0] = r1;
                                Regs.ClipWindowTilemap[1] = r2;
                                Regs.ClipWindowTilemap[2] = r3;
                                Regs.ClipWindowTilemap[3] = r4;
                                Regs.RegisterIsWritten[0x1B] = DELAY_COUNTER;
                                doInvalidate = true;
                            }
                            break;
                        }
                    case 0x1C:
                        {
                            r1 = CSpect.GetNextRegister(0x1C, 0);
                            if (r1 != Regs.NextRegisters[0x1C])
                            {
                                Regs.NextRegisters[0x1C] = (byte)r1;
                                Regs.RegisterIsWritten[0x1C] = DELAY_COUNTER;

                                // reset clip window index
                                if ((r1 & 1) != 0) Regs.ClipWindowLayer2[4] = 0;
                                if ((r1 & 2) != 0) Regs.ClipWindowSprites[4] = 0;
                                if ((r1 & 4) != 0) Regs.ClipWindowULA[4] = 0;
                                if ((r1 & 8) != 0) Regs.ClipWindowTilemap[4] = 0;
                            }
                            break;
                        }
                    default:
                        {
                            r = CSpect.GetNextRegister((byte)i, 0);
                            if (Regs.NextRegisters[i] != r)
                            {
                                Regs.RegisterIsWritten[i] = DELAY_COUNTER;
                                Regs.NextRegisters[i] = (byte)r;
                                doInvalidate = true;
                            }
                            break;
                        }
                }


                // Count down modified "highlight" counter
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
        ///     Called once an emulator frame - update copper if "Active"
        /// </summary>
        // ******************************************************************************************
        public void Tick()
        {
            if (!Active) return;

            // Read all next registers every tick....
            ReadRegisters();
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
            return false;
        }
    }
}

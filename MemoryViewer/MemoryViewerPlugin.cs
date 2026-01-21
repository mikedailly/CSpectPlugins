using Plugin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;

namespace MemoryViewer
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
    ///     The Sprite Viewer
    /// </summary>
    // *********************************************************************************************************
    public class MemoryViewerPlugin : iPlugin
    {
        /// <summary>CSpect emulator interface</summary>
        public iCSpect CSpect;
        public static bool Active;
        public static MemoryViewerForm form;

        bool doinvalidate = false;
        public bool OpenMemViewerWindow = false;
        public int PaletteNumber = 0;

        public int DoStep = 0;

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
            Console.WriteLine("Memory Viewer added");

            CSpect = _CSpect;
            IntPtr handle = (IntPtr)CSpect.GetGlobal(eGlobal.window_handle);
            hwndWrapper = new WindowWrapper(handle);

            PaletteNumber = 1;
            ZXPalette.Init();

            // Detect keypress for starting disassembler
            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO("<ctrl><alt>d", eAccess.KeyPress, 0));                   // Key press callback
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
                OpenMemViewerWindow = true;
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
        ///     Called once an emulator frame - update sprite data if "Active"
        /// </summary>
        // ******************************************************************************************
        public void Tick()
        {
            if (!Active) return;

            for (int i = -0; i < 256; i++)
            {
                uint col = CSpect.GetColour(PaletteNumber, i);
                ZXPalette.Palette1[i] = col;
            }


            /*
            for (int i = 0; i < 128; i++)
            {
                SpriteData[i] = CSpect.GetSprite(i);
            }

            SpriteMemory = CSpect.PeekSprite(0, 16384, SpriteMemory);

            bool isEqual = Enumerable.SequenceEqual(SpriteMemory, LastSpriteMemory);
            if (!isEqual)
            {
                doinvalidate = true;
                update_sprite_shapes = true;

                for (int i = -0; i < 256; i++) {
                    uint col = CSpect.GetColour(2, i);
                    ZXPalette.SpritePalette1[i] = col;
                }
            }

            // remember last set
            Array.Copy(SpriteMemory,LastSpriteMemory,16384);            
            */
        }


        // ******************************************************************************************
        /// <summary>
        ///     Called once an OS emulator frame - do all UI rendering, opening windows etc here.
        /// </summary>
        // ******************************************************************************************
        public void OSTick()
        {
            if (OpenMemViewerWindow)
            {
                if (!Active)
                {
                    Active = true;
                    doinvalidate = true;
                    form = new MemoryViewerForm(this);
                    form.Show();
                }
                OpenMemViewerWindow = false;
            }

            if (DoStep == 2)
            {
                CSpect.SetGlobal(eGlobal.pause, false);
                DoStep = 1;
            }
            else if (DoStep == 1)
            {
                CSpect.SetGlobal(eGlobal.pause, true);
                DoStep = 0;
            }

            if (form != null)
            {
                // Set Pause/Result button text
                if ((bool)CSpect.GetGlobal(eGlobal.pause))
                {
                    if (form.PauseButton.Text != "Resume")
                    {
                        form.PauseButton.Text = "Resume";
                    }
                }
                else
                {
                    if (form.PauseButton.Text != "Pause")
                    {
                        form.PauseButton.Text = "Pause";
                    }
                }


                // Enable/Disable "Step" button
                if ((bool)CSpect.GetGlobal(eGlobal.pause) == true)
                {
                    if (form.StepButton.Enabled == false)
                    {
                        form.StepButton.Enabled = true;
                    }
                }
                else
                {
                    if (form.StepButton.Enabled == true)
                    {
                        form.StepButton.Enabled = false;
                    }
                }
            }


            //doinvalidate = true;
            if (form != null && (form.RealtimeCheckbox.Checked || form.DoSnapshot))
            {
                form.DoSnapshot = false;
                form.Invalidate();              // refresh IF it's changed
                doinvalidate = false;
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


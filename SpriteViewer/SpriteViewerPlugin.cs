using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Plugin;

namespace SpriteViewer
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
    public class SpriteViewerPlugin : iPlugin
    {
        /// <summary>CSpect emulator interface</summary>
        iCSpect CSpect;
        public static bool Active;
        public static SpriteViewerForm form;
        byte[] SpriteMemory = new byte[16384];
        byte[] LastSpriteMemory = new byte[16384];
        SSprite[] SpriteData = new SSprite[128];

        int[] Palette = new int[256];

        bool doinvalidate = false;
        bool update_sprite_shapes = true;
        public bool OpenSpriteWindow = false;

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
            Debug.WriteLine("Sprite Viewer added");

            CSpect = _CSpect;
            IntPtr handle = (IntPtr)CSpect.GetGlobal(eGlobal.window_handle);
            hwndWrapper = new WindowWrapper(handle);

            ZXPalette.Init();

            // Detect keypress for starting disassembler
            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO("<ctrl><alt>s", eAccess.KeyPress, 0));                   // Key press callback
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
                OpenSpriteWindow = true;
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
        }


        // ******************************************************************************************
        /// <summary>
        ///     Called once an OS emulator frame - do all UI rendering, opening windows etc here.
        /// </summary>
        // ******************************************************************************************
        public void OSTick()
        {
            if (OpenSpriteWindow)
            {
                if (!Active)
                {
                    Active = true;
                    doinvalidate = true;
                    update_sprite_shapes = true;
                    form = new SpriteViewerForm(SpriteMemory, this);
                    form.Show();
                }
                OpenSpriteWindow = false;
            }

            if (doinvalidate && form!=null)
            {
                if (update_sprite_shapes) form.SpriteBuffer = SpriteMemory;

                form.Invalidate();     // refresh IF it's changed
                Application.DoEvents();
                doinvalidate = false;
                update_sprite_shapes = false;
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


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

        iWindow Window;
        List<UInt32[]> SpriteBitmap;

        int PaletteOffset
        {
            get
            {
                if (form != null)
                {
                    return form.PaletteOffset;
                }
                return 0;

            }
        }

        bool Is16Bit { 
            get
            {
                if (form != null)
                {
                    return form.Is16Bit;
                }
                return false;
            } 
        }

        int SpriteSize
        {
            get
            {
                if (form != null)
                {
                    return form.SpriteSize;
                }
                return 16;
            }
        }

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
            Console.WriteLine(" Sprite Viewer added");

            CSpect = _CSpect;
            IntPtr handle = (IntPtr)CSpect.GetGlobal(eGlobal.window_handle);
            hwndWrapper = new WindowWrapper(handle);

            ZXPalette.Init();

            SpriteBitmap = new List<uint[]>();
            for (int i = 0; i < 128; i++)
            {
                SpriteBitmap.Add(new UInt32[16 * 16]);
            }

            // Detect keypress for starting disassembler
            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO("<ctrl><alt>s", eAccess.KeyPress, 0));                   // Key press callback
            ports.Add(new sIO("<ctrl><alt>i", eAccess.KeyPress, 1));                   // Key press callback
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
            else if (_id == 1)
            {
                //UInt32[,] buffer = (UInt32[,])CSpect.GetGlobal(eGlobal.last_frame);
                CSpect.LoadNex(@"C:\source\ZXSpectrum\Demo\Beast\beast.nex");
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
        public void UpdateSprites()
        {
            int cnt = 128;
            if (!Is16Bit) cnt = 64;
            for (int i = 0; i < cnt; i++)
            {
                ZXSprite.DrawSprite(SpriteBitmap[i], Is16Bit, i, PaletteOffset, SpriteMemory);
            }
        }


        public void DrawSprites()
        {
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
                        Window.DrawImage(SpriteBitmap[cnt++], (x*70),y*70, 16,16, 32,32);
                        Window.DrawImage(SpriteBitmap[cnt++], (x*70)+31,y*70, 16,16, 32,32);
                        Window.DrawImage(SpriteBitmap[cnt++], (x*70),(y*70)+31, 16,16, 32,32);
                        Window.DrawImage(SpriteBitmap[cnt++], (x*70)+31,(y*70)+31, 16,16, 32,32);
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
                        Window.DrawImage(SpriteBitmap[cnt++], x*40,y*40, 16,16, 32,32);
                    }
                }

            }
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
                UpdateSprites();
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

                    //Window = CSpect.OpenWindow("TestWindow", 653, 741);
                    //Window.OnClosed += Window_OnClosed;
                }
                OpenSpriteWindow = false;
            }

            if (doinvalidate && form!=null)
            {
                if (update_sprite_shapes) form.SpriteBuffer = SpriteMemory;

                form.Invalidate();     // refresh IF it's changed
                //Application.DoEvents();
                doinvalidate = false;
                update_sprite_shapes = false;
            }

            /*if (Window != null)
            {
                UInt32[] screen = Window.Screen;
                screen[0] = col;
                col++;
                for (int i = 0; i < (screen.Length - 1); i++)
                {
                    screen[i + 1] = screen[i];
                }
                DrawSprites();
                Window.IsDirty = true;
            }*/
        }

        private void Window_OnClosed(object sender, EventArgs e)
        {
            SpriteViewerPlugin.form.Close();
            SpriteViewerPlugin.Active = false;
            //SpriteViewerPlugin.form = null;
        }

        UInt32 col = 0xff000000;

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


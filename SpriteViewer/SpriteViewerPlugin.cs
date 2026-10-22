// ********************************************************************************************************************************************
//      CSpect Sprite Viewer extension, shows the sprite patterns in realtime
//      Written by:
//                  Mike Dailly
//
//      contributions by:
//
//      Released under the GNU 3 license - please see license file for more details
//
//      <ctrl><alt>s opens the viewer in its own OS window (iCSpect.OpenWindow) drawn with Dear ImGui
//      (iWindow.SetImGuiDraw) - no WinForms.
//
// ********************************************************************************************************************************************
using System;
using System.Collections.Generic;
using Plugin;

namespace SpriteViewer
{
    // *********************************************************************************************************
    /// <summary>
    ///     The Sprite Viewer
    /// </summary>
    // *********************************************************************************************************
    public class SpriteViewerPlugin : iPlugin
    {
        /// <summary>CSpect emulator interface</summary>
        iCSpect CSpect;
        /// <summary>Is the viewer window open?</summary>
        public static volatile bool Active;

        /// <summary>Sprite pattern memory, read on the emulator thread</summary>
        byte[] SpriteMemory = new byte[16384];
        byte[] LastSpriteMemory = new byte[16384];
        /// <summary>Sprite palette (9 bit RRRGGGBBB colours), read on the emulator thread</summary>
        uint[] SpritePalette = new uint[256];
        uint[] LastSpritePalette = new uint[256];

        /// <summary>Snapshot handed to the UI (main thread). Guarded by SnapshotLock</summary>
        internal readonly object SnapshotLock = new object();
        internal byte[] SnapshotMemory = new byte[16384];
        internal uint[] SnapshotPalette = new uint[256];
        /// <summary>Bumped each time the snapshot changes</summary>
        internal volatile int SnapshotVersion = 0;

        /// <summary>Which sprite palette to read: 0 = first, 1 = second (set by the UI)</summary>
        internal volatile int PaletteSelect = 0;
        int LastPaletteSelect = -1;

        volatile bool OpenSpriteWindow = false;

        /// <summary>Our window (null when closed)</summary>
        iWindow Window;
        /// <summary>The viewer UI (kept between openings so settings persist)</summary>
        SpriteViewerView View;

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
            ZXPalette.Init();

            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO("<ctrl><alt>s", eAccess.KeyPress, 0));                   // Open the viewer
            ports.Add(new sIO("<ctrl><alt>i", eAccess.KeyPress, 1));                   // Load test NEX
            return ports;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Key pressed callback.
        /// </summary>
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
            Active = false;
            if (Window != null)
            {
                iWindow w = Window;
                Window = null;
                w.Close();
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Read access type - not used
        /// </summary>
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
        ///     Called once an emulator frame - read sprite memory and palette if "Active",
        ///     and hand a copy to the UI when either changes
        /// </summary>
        // ******************************************************************************************
        public void Tick()
        {
            if (!Active) return;

            SpriteMemory = CSpect.PeekSprite(0, 16384, SpriteMemory);
            int pal = (PaletteSelect == 0) ? 2 : 6;            // %010 = sprite first palette, %110 = sprite second
            for (int i = 0; i < 256; i++)
            {
                SpritePalette[i] = CSpect.GetColour(pal, i);
            }

            bool changed = (LastPaletteSelect != PaletteSelect);
            if (!changed)
            {
                for (int i = 0; i < 16384; i++) { if (SpriteMemory[i] != LastSpriteMemory[i]) { changed = true; break; } }
            }
            if (!changed)
            {
                for (int i = 0; i < 256; i++) { if (SpritePalette[i] != LastSpritePalette[i]) { changed = true; break; } }
            }
            if (!changed) return;

            Array.Copy(SpriteMemory, LastSpriteMemory, 16384);
            Array.Copy(SpritePalette, LastSpritePalette, 256);
            LastPaletteSelect = PaletteSelect;
            lock (SnapshotLock)
            {
                Array.Copy(SpriteMemory, SnapshotMemory, 16384);
                Array.Copy(SpritePalette, SnapshotPalette, 256);
                SnapshotVersion++;
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Called once an OS frame - open the window. (ImGui windows redraw every frame.)
        /// </summary>
        // ******************************************************************************************
        public void OSTick()
        {
            if (OpenSpriteWindow)
            {
                OpenSpriteWindow = false;
                if (Window == null) OpenWindow();
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Open our window and hook up the ImGui view
        /// </summary>
        // ******************************************************************************************
        void OpenWindow()
        {
            iWindow w = CSpect.OpenWindow("Sprite Viewer", 600, 720);
            if (w == null) return;
            if (View == null) View = new SpriteViewerView(this);
            if (!w.SetImGuiDraw(View.Draw))
            {
                Console.WriteLine(" Sprite Viewer: Dear ImGui not available on this system");
                w.Close();
                return;
            }
            View.Attach(w);
            w.OnClosed += Window_OnClosed;
            Window = w;
            LastPaletteSelect = -1;                 // force a fresh snapshot on the next emulator frame
            Active = true;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Our window was closed (by the user, or Quit) - its texture went with it
        /// </summary>
        // ******************************************************************************************
        private void Window_OnClosed(object sender, EventArgs e)
        {
            Active = false;
            Window = null;
            if (View != null) View.Detach();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Write access type - not used
        /// </summary>
        // ******************************************************************************************
        public bool Write(eAccess _type, int _port, int _id, byte _value)
        {
            return false;
        }
    }
}

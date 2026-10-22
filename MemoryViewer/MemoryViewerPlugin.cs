// ********************************************************************************************************************************************
//      CSpect Memory Viewer extension
//      Written by:
//                  Mike Dailly
//
//      contributions by:
//
//      Released under the GNU 3 license - please see license file for more details
//
//      <ctrl><alt>d opens the memory viewer in its own OS window (iCSpect.OpenWindow) drawn with Dear ImGui
//      (iWindow.SetImGuiDraw) - no WinForms.
//
// ********************************************************************************************************************************************
using Plugin;
using System;
using System.Collections.Generic;

namespace MemoryViewer
{
    // *********************************************************************************************************
    /// <summary>
    ///     The Memory Viewer
    /// </summary>
    // *********************************************************************************************************
    public class MemoryViewerPlugin : iPlugin
    {
        /// <summary>CSpect emulator interface</summary>
        public iCSpect CSpect;
        /// <summary>Is the viewer window open?</summary>
        public static volatile bool Active;

        public volatile bool OpenMemViewerWindow = false;
        /// <summary>Palette the viewer draws with (copied in Tick)</summary>
        public volatile int PaletteNumber = 0;

        /// <summary>Single frame step state machine: 2 = unpause, 1 = pause again, 0 = idle</summary>
        public int DoStep = 0;

        /// <summary>Our window (null when closed)</summary>
        iWindow Window;
        /// <summary>The viewer UI (kept between openings so settings persist)</summary>
        MemoryViewerView View;


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
            Console.WriteLine(" Memory Viewer added");

            CSpect = _CSpect;

            PaletteNumber = 1;
            ZXPalette.Init();

            // Detect keypress for starting the viewer
            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO("<ctrl><alt>d", eAccess.KeyPress, 0));                   // Key press callback
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
        ///     Called once an emulator frame - copy the selected palette if "Active"
        /// </summary>
        // ******************************************************************************************
        public void Tick()
        {
            if (!Active) return;

            int pal = PaletteNumber;
            for (int i = 0; i < 256; i++)
            {
                ZXPalette.Palette1[i] = CSpect.GetColour(pal, i);
            }
        }


        // ******************************************************************************************
        /// <summary>
        ///     Called once an OS frame - open the window, and run the single frame "step"
        /// </summary>
        // ******************************************************************************************
        public void OSTick()
        {
            if (OpenMemViewerWindow)
            {
                OpenMemViewerWindow = false;
                if (Window == null) OpenWindow();
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
        }

        // ******************************************************************************************
        /// <summary>
        ///     Open our window and hook up the ImGui view
        /// </summary>
        // ******************************************************************************************
        void OpenWindow()
        {
            iWindow w = CSpect.OpenWindow("Memory Viewer", 990, 680);
            if (w == null) return;
            if (View == null) View = new MemoryViewerView(this);
            if (!w.SetImGuiDraw(View.Draw))
            {
                Console.WriteLine(" Memory Viewer: Dear ImGui not available on this system");
                w.Close();
                return;
            }
            View.Attach(w);
            w.OnClosed += Window_OnClosed;
            Window = w;
            Active = true;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Our window was closed (by the user, or Quit) - its textures went with it
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

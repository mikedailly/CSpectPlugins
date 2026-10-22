// ********************************************************************************************************************************************
//      CSpect Profiler extension
//      Written by:
//                  Mike Dailly
//
//      contributions by:
//
//      Released under the GNU 3 license - please see license file for more details
//
//      <ctrl><alt>p opens the profiler in its own OS window (iCSpect.OpenWindow) drawn with Dear ImGui
//      (iWindow.SetImGuiDraw) - no WinForms.
//
// ********************************************************************************************************************************************
using System;
using System.Collections.Generic;
using Plugin;

namespace Profiler
{
    // *********************************************************************************************************
    /// <summary>
    ///     A Basic Profiler
    /// </summary>
    // *********************************************************************************************************
    class ProfilerPlugin : iPlugin
    {
        /// <summary>CSpect emulator interface</summary>
        iCSpect CSpect;
        /// <summary>Is the profiler window open?</summary>
        public static volatile bool Active;

        volatile bool OpenProfiler = false;
        /// <summary>Our window (null when closed)</summary>
        iWindow Window;
        /// <summary>The profiler UI</summary>
        ProfilerView View;

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
            Console.WriteLine(" Profiler added");

            CSpect = _CSpect;

            // Detect keypress for starting the profiler
            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO("<ctrl><alt>p", eAccess.KeyPress, 0));                   // Key press callback
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
                OpenProfiler = true;
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
        ///     Called once an emulator frame - the emulator accumulates the profile itself
        /// </summary>
        // ******************************************************************************************
        public void Tick()
        {
        }

        // ******************************************************************************************
        /// <summary>
        ///     Called once an OS frame - open the window when asked
        /// </summary>
        // ******************************************************************************************
        public void OSTick()
        {
            if (!OpenProfiler) return;
            OpenProfiler = false;
            if (Window != null) return;                     // already open

            iWindow w = CSpect.OpenWindow("Profiler", 1314, 698);
            if (w == null) return;
            if (View == null) View = new ProfilerView(CSpect);  // keep the last profile between openings
            if (!w.SetImGuiDraw(View.Draw))
            {
                Console.WriteLine(" Profiler: Dear ImGui not available on this system");
                w.Close();
                return;
            }
            w.OnClosed += Window_OnClosed;
            Window = w;
            Active = true;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Our window was closed (by the user, or Quit)
        /// </summary>
        // ******************************************************************************************
        private void Window_OnClosed(object sender, EventArgs e)
        {
            Active = false;
            Window = null;
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

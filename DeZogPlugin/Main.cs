using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plugin;


namespace DeZogPlugin
{
    // **************************************************************************************************************************************
    /// <summary>
    ///     The plugin implements a socket to communicate with[DeZog](https://github.com/maziac/DeZog).
    ///     The received commands are executed and control the CSpect debugger.
    /// </summary>
    // **************************************************************************************************************************************
    public class Main : iPlugin
    {

        public static string ProgramName;
        public static iCSpect CSpect;
        public static Settings Settings;


        // ******************************************************************************************
        /// <summary>
        ///     Initialization.Called by CSpect.
        ///     Returns a list with the ports to be registered.
        /// </summary>
        /// <param name="_CSpect"></param>
        /// <returns>
        ///     List of IO interface commands
        /// </returns>
        // ******************************************************************************************
        public List<sIO> Init(iCSpect _CSpect)
        {
            string version = typeof(Main).Assembly.GetName().Version.ToString();
            ProgramName = typeof(Main).Assembly.GetName().Name;
            ProgramName += " v" + version;
            string dzrpVersion = Commands.GetDzrpVersion();
            Log.WriteLine("v{0} started. DZRP v{1}.", version, dzrpVersion);

            CSpect = _CSpect;

            // Read settings file (port)
            Settings = Settings.Load();
            Log.Enabled = Settings.LogEnabled;

 
            //Server.Listen(Settings.Port);
            CSpectSocket.Port = Settings.Port;
            CSpectSocket.StartListening();

            // No ports
            List<sIO> ports = new List<sIO>();
            return ports;
        }


        // ******************************************************************************************
        /// <summary>
        ///     Called by CSpect to quit the plugin.
        /// </summary>
        // ******************************************************************************************
        public void Quit()
        {
            // If the program is stopped the socket is closed anyway.
            Log.WriteLine("Terminated.");
        }


        // ******************************************************************************************
        /// <summary>
        ///     Called every frame. I.e. interrupt.
        /// </summary>
        // ******************************************************************************************
        public void Tick()
        {
            Commands.Tick();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Called once an OS emulator frame - do all UI rendering, opening windows etc here.
        /// </summary>
        // ******************************************************************************************
        public void OSTick()
        {
        }

        // ******************************************************************************************
        /// <summary>
        ///     Unused.
        /// </summary>
        /// <param name="_type"></param>
        /// <param name="_port"></param>
        /// <param name="_id"></param>
        /// <param name="_value"></param>
        /// <returns></returns>
        // ******************************************************************************************
        public bool Write(eAccess _type, int _port, int _id, byte _value)
        {
            return true;
        }


        // ******************************************************************************************
        /// <summary>
        ///     Unused
        /// </summary>
        /// <param name="_type"></param>
        /// <param name="_port"></param>
        /// <param name="_id"></param>
        /// <param name="_isvalid"></param>
        /// <returns></returns>
        // ******************************************************************************************
        public byte Read(eAccess _type, int _port, int _id, out bool _isvalid)
        {
            _isvalid = false;
            return 0;
        }


        // ******************************************************************************************
        /// <summary>
        ///     Called whenever a key is pressed.
        /// </summary>
        /// <param name="_id">The key id.</param>
        /// <returns>true if the plugin handled the key.</returns>
        // ******************************************************************************************
        public bool KeyPressed(int _id)
        {
            // Not used
            return false;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Called when the machine is reset.
        /// </summary>
        // ******************************************************************************************
        public void Reset()
        {
            // Not used
        }

    }
}



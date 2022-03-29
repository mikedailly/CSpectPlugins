using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plugin;


namespace DeZogPlugin
{
    /**
     * The plugin implements a socket to communicate with [DeZog](https://github.com/maziac/DeZog).
     * The received commands are executed and control the CSpect debugger.
     */
    public class Main : iPlugin
    {

        public static string ProgramName;
        public static iCSpect CSpect;
        public static Settings Settings;


        /**
         * Initialization. Called by CSpect.
         * Returns a list with the ports to be registered.
         */
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


        /**
         * Called by CSpect to quit the plugin.
         */
        public void Quit()
        {
            // If the program is stopped the socket is closed anyway.
            Log.WriteLine("Terminated.");
        }


        /**
         * Called every frame. I.e. interrupt.
         */
        public void Tick()
        {
            Commands.Tick();
        }
        

        /**
         * Writes a TX byte (_value).
         * Unused.
         */
        public bool Write(eAccess _type, int _port, byte _value)
        {
            return true;
        }


        /**
         * Reads the state or reads a byte from the receive fifo.
         * _isvalid is set to true if the returned value could be provided.
         * Unused.
         */
        public byte Read(eAccess _type, int _port, out bool _isvalid)
        {
            _isvalid = false;
            return 0;
        }


        /**
         * Called whenever a key is pressed.
         * @param _id The key id.
         * @returns true if the plugin handled the key.
         */
        public bool KeyPressed(int _id)
        {
            // Not used
            return false;
        }


        /**
         * Called when the machine is reset.
         */
        public void Reset()
        {
            // Not used
        }

    }
}



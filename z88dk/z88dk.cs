using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plugin;

namespace Pasta80Symbols
{

    // **********************************************************************
    /// <summary>
    ///     A simple, empty i2C device
    /// </summary>
    // **********************************************************************
    public class Pasta80ListFiles : iSymbol
    {
        public iCSpect CSpect;
        public Pasta80_ListFile Pasta80;

        // **********************************************************************
        /// <summary>
        ///     Get the command line option to look for
        /// </summary>
        /// <returns>Command line option</returns>
        // **********************************************************************
        public string GetCommandLineOption()
        {
            return "pasta80";
        }

        // **********************************************************************
        /// <summary>
        ///     Get symbol descrption (for commandline)
        /// </summary>
        /// <returns>Description to put on command line</returns>
        // **********************************************************************
        public string GetDescription()
        {
            return "Load Pasta/80 .lst file and get symbols.";
        }

        // **********************************************************************
        /// <summary>
        ///     Return name of plugin
        /// </summary>
        /// <returns></returns>
        // **********************************************************************
        public string GetName()
        {
            return "Pasta80";
        }

        // **********************************************************************
        /// <summary>
        ///     Init the device
        /// </summary>
        /// <returns>
        ///     List of ports we're registering
        /// </returns>
        // **********************************************************************
        public bool Init(iCSpect _CSpect)
        {
            Console.WriteLine(" Pasta80 .LST file loader");
            CSpect = _CSpect;
            Pasta80 = new Pasta80_ListFile(CSpect);
            return true;
        }
        // **********************************************************************
        /// <summary>
        ///     Quit the device - free up anything we need to
        /// </summary>
        // **********************************************************************
        public void Quit()
        {
        }


        public bool LoadSymbols(string _path)
        {
            string[] pBuffer;
            try
            {
                pBuffer = File.ReadAllLines(_path);
            }
            catch
            {
                // error loading symbols
                return false;
            }

            bool okay = Pasta80.LoadPasta80File(pBuffer);
            return okay;
        }
    }
}


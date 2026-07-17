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
        public z88dk_MapFile z88dk;

        // **********************************************************************
        /// <summary>
        ///     Get the command line option to look for
        /// </summary>
        /// <returns>Command line option "-z88dk=name"</returns>
        // **********************************************************************
        public string GetCommandLineOption()
        {
            return "z88dk";
        }

        // **********************************************************************
        /// <summary>
        ///     Get symbol descrption (for commandline)
        /// </summary>
        /// <returns>Description to put on command line</returns>
        // **********************************************************************
        public string GetDescription()
        {
            return "Load z88dk .map file and get symbols.";
        }

        // **********************************************************************
        /// <summary>
        ///     Return name of plugin
        /// </summary>
        /// <returns></returns>
        // **********************************************************************
        public string GetName()
        {
            return "z88DK";
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
            Console.WriteLine(" z88DK .MAP file loader");
            CSpect = _CSpect;
            z88dk = new z88dk_MapFile(CSpect);
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


        // **********************************************************************
        /// <summary>
        ///     Load the requested symbol file
        /// </summary>
        /// <param name="_path"></param>
        /// <returns>
        ///     TRUE for okay, FALSE for error
        /// </returns>
        // **********************************************************************
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

            bool okay = z88dk.LoadMapFile(pBuffer);
            return okay;
        }
    }
}


using Plugin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Text;
using System.Threading.Tasks;

namespace BorielSymbols
{

    // **********************************************************************
    /// <summary>
    ///     A simple, empty i2C device
    /// </summary>
    // **********************************************************************
    public class BorialMapFiles : iSymbol
    {
        public iCSpect CSpect;
        public Boriel_MapFile BorielMap;

        // **********************************************************************
        /// <summary>
        ///     Get the command line option to look for
        /// </summary>
        /// <returns>Command line option</returns>
        // **********************************************************************
        public string GetCommandLineOption()
        {
            return "boriel";
        }

        // **********************************************************************
        /// <summary>
        ///     Get symbol descrption (for commandline)
        /// </summary>
        /// <returns>Description to put on command line</returns>
        // **********************************************************************
        public string GetDescription()
        {
            return "Load Boriel .map file and get symbols.";
        }

        // **********************************************************************
        /// <summary>
        ///     Return name of plugin
        /// </summary>
        /// <returns></returns>
        // **********************************************************************
        public string GetName()
        {
            return "Boriel";
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
            Console.WriteLine(" Boriel .MAP file loader");
            CSpect = _CSpect;
            BorielMap = new Boriel_MapFile(CSpect);
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
        ///     Load Boriel symbol file, with optional parsing of the BASIC file
        /// </summary>
        /// <param name="_path">"Path\filename.map[:Basicfile.bas]"</param>
        /// <returns>
        ///     TRUE for loaded
        ///     FALSE for not loaded
        /// </returns>
        // **********************************************************************
        public bool LoadSymbols(string _path)
        {
            Console.WriteLine("Boriel MAP file loader - active");
            string MapName = _path;
            string BasicName = String.Empty;

            // still allow "Z:\....." etc
            int index = _path.LastIndexOf(":");
            if (index != 2)
            {
                MapName = _path.Substring(0, index);
                BasicName = _path.Substring(index + 1);
            }


            string[] pBuffer;
            try
            {
                pBuffer = File.ReadAllLines(MapName);
            }
            catch
            {
                // error loading symbols
                return false;
            }
            if (!string.IsNullOrEmpty(BasicName)) ParseBasicFile(BasicName);

            bool okay = BorielMap.LoadBorielFile(pBuffer);

            // Add predefined symbols
            //CSpect.AddSymbol("CHAN_OPEN", (int)0x1601, (int)0x1601, eLabelType.Address);
            //CSpect.AddSymbol("CHAN_OP_1", (int)0x1610, (int)0x1601, eLabelType.Address);
            //CSpect.AddSymbol("INDEXER_1", (int)0x16db, (int)0x1601, eLabelType.Address);
            //CSpect.AddSymbol("INDEXER", (int)0x16dc, (int)0x1601, eLabelType.Address);

            return okay;
        }





        #region BASIC parsing

        // **********************************************************************
        /// <summary>
        ///     Found '!codebankpages so read all codebanks
        /// </summary>
        /// <example>
        ///     '!codebankpages=44,45,46    ' CODEBANK 1 -> 44, 2 -> 45, 3 -> 46
        ///     '!codebankpages=44
        ///     '!codebankpages=44          ' COMMENT
        ///     '!codebankpages = 44,45
        ///     '!codebankpages = 44  ,   45    ' comment
        /// </example>
        /// <param name="_line">code bank line</param>
        // **********************************************************************
        void GetCodeBanks(string _line)
        {
            int index = _line.IndexOf("=");
            if (index < 0) return;
            index++;
            string bankstring = _line.Substring(index);
            int lindex = bankstring.IndexOf("'");                   // look for comments
            if (lindex >= 0)
            {
                bankstring = bankstring.Substring(0,lindex);
            }
            bankstring = bankstring.Trim();
            string[] banks = bankstring.Split(',');

            // Now loop through al the banks
            for(int i = 0;i<banks.Length;i++)
            {
                string bank_text = banks[i].Trim();
                int bank = int.Parse(bank_text);
                BorielMap.AddBank("B" + (i+1).ToString(), bank);
            }
        }


        // **********************************************************************
        /// <summary>
        ///     Func and include file, and if local... load it
        /// </summary>
        /// <param name="_line">include line</param>
        // **********************************************************************
        void IncludeFile(string _line)
        {
            _line = _line.Trim();
            if (_line.Contains("<")) return;            // don't include library functions
            int index1 = _line.IndexOf('"');
            int index2 = _line.IndexOf('"', index1 + 1);
            if(index1<0 || index2<0) return;

            // get filename
            string file = _line.Substring(index1+1,(index2-1)-index1);
            ParseBasicFile(file);
        }

        // **********************************************************************
        /// <summary>
        ///     Parse a single BASIC like
        /// </summary>
        /// <param name="_line">line to parse/check</param>
        // **********************************************************************
        void ParseBasicLine(string _line)
        {
            if (_line.IndexOf("'!codebankpages") >= 0) GetCodeBanks(_line);
            else if (_line.IndexOf("#include") >= 0) IncludeFile(_line);
        }

        // **********************************************************************
        /// <summary>
        ///     Recursively load and parse basic files
        /// </summary>
        /// <param name="BasicFileName"></param>
        // **********************************************************************
        public void ParseBasicFile(string BasicFileName)
        {
            Console.WriteLine("  Scanning - \"" + BasicFileName+"\"");
            string[] pBuffer;
            try
            {
                if (File.Exists(BasicFileName))
                {
                    pBuffer = File.ReadAllLines(BasicFileName);
                    for (int line = 0; line < pBuffer.Length; line++)
                    {
                        ParseBasicLine(pBuffer[line]);
                    }
                }
            }
            catch
            {
            }
        }



        #endregion


    }
}


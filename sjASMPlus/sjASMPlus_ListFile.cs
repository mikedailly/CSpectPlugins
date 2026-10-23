using Microsoft.Win32;
using Plugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection.Emit;
using System.Runtime.Remoting.Channels;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace BorielSymbols
{
    /// <summary>
    /// <example>
    ///     <source file>|<src line>|<definition file>|<def line>|<page>|<value>|<type>|<data>
    ///     Field Meaning
    ///     source file     Top-level source file that emitted the instruction, label or device.
    ///     src line        line[:colBegin[:colEnd]]. All values start at 1. Columns count bytes, so a tab is +1. colEnd points just past the segment.
    ///     definition      file File where the item was actually defined, for example a macro body. Empty means the same as source file.
    ///     def line	    0 if there’s no separate definition. Otherwise it uses the same format as src line, relative to definition file.
    ///     page            Memory page the address is in, or -1 if the value isn’t an address.
    ///     value           Any 32-bit value (for example an EQU). For addresses it’s the 16-bit Z80 address.The bits above the page size give the slot number.
    ///     type            One uppercase letter (see below).
    ///     data            Contents depend on the type.
    /// </example>
    /// </summary>
    public class sjASMPlus_MapFile
    {
        /// <summary>Read HEX/Dec return error if no hex values found</summary>
        const Int64 ERROR_VAL = -123456789;
        /// <summary>Text used to determain the end of the line</summary>
        const string END_TXT = "[[END]]";

        /// <summary>Boriel banks</summary>
        Dictionary<string, int> BankLookup = new Dictionary<string, int>();

        /// <summary>Global labels to ignore </summary>
        List<string> Ignore = new List<string>()
        {
            "P","I","R"
        };

        /// <summary>The CSpect interface</summary>
        iCSpect CSpect;

        // *************************************************************************************************************************************************
        /// <summary>
        ///     Create new Boriel parser
        /// </summary>
        /// <param name="_CSpect">pointer to iCSpect interface</param>
        // *************************************************************************************************************************************************
        public sjASMPlus_MapFile(iCSpect _CSpect)
        {
            CSpect=_CSpect;
        }


        // *************************************************************************************************************************************************
        /// <summary>
        ///     Add a bank lookup
        /// </summary>
        /// <param name="_bank">Bank name</param>
        /// <param name="_banknumber"></param>
        // *************************************************************************************************************************************************
        public void AddBank(string _bank, int _banknumber)
        {
            if (BankLookup.TryGetValue(_bank, out int vlaue)) return;      // already there
            BankLookup.Add(_bank,_banknumber);
        }

        // *************************************************************************************************************************************************
        /// <summary>
        ///     Skip all whitespace in the line
        /// </summary>
        /// <param name="line">line of text</param>
        /// <param name="line_index">current index</param>
        /// <returns>new index after whitespace</returns>
        // *************************************************************************************************************************************************
        public int SkipWhiteSpace(string line, int line_index)
        {
            while (line_index < line.Length)
            {
                if (line[line_index] != ' ' && line[line_index] != '\t' && line[line_index] != '+') return line_index;
                line_index++;
            }
            return line_index;
        }

        // *************************************************************************************************************************************************
        /// <summary>
        ///     Get next ID string
        /// </summary>
        /// <param name="line">whole line</param>
        /// <param name="index">current index</param>
        /// <param name="oLine">next position</param>
        /// <returns>
        ///     The next string in the line sequence
        /// </returns>
        // *************************************************************************************************************************************************
        public string GetNext(string line, int index, out int oLine)
        {
            index = SkipWhiteSpace(line, index);

            // Now read all text until we hit whitespace
            int eindex = index;
            while (index < line.Length)
            {
                if (line[index] == ' ' || line[index] == '\t' || line[index] == '+') break;
                index++;
            }
            oLine = index;

            if (index == eindex) return END_TXT;
            string txt = line.Substring(eindex, (index - eindex));
            return txt;
        }

        // ****************************************************************************************************************
        /// <summary>
        ///     Read a decimal number, and return it or an error value
        /// </summary>
        /// <param name="s">decimal number to scan</param>
        /// <returns>
        ///     The int, or ERROR_VAL
        /// </returns>
        // ****************************************************************************************************************
        public Int64 ReadDec(string s)
        {
            Int64 v;
            if (!Int64.TryParse(s, out v))
            {
                v = ERROR_VAL;
            }
            return v;
        }


        // ****************************************************************************************************************
        /// <summary>
        ///     Read a HEX number, and return an error if not a HEX number
        /// </summary>
        /// <param name="s"></param>
        /// <returns>The hex value or ERROR_VAL</returns>
        // ****************************************************************************************************************
        public Int64 ReadHex(string s)
        {
            s = s.ToUpper();
            Int64 v = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9')
                {
                    Int64 b = (int)c - '0';
                    v = (v << 4) | b;
                }
                else if (c >= 'A' && c <= 'F')
                {
                    Int64 b = ((int)c - 'A') + 10;
                    v = (v << 4) | b;
                }
                else
                {
                    if (i == 0) return ERROR_VAL;
                    break;   // error
                }
            }
            return v;
        }

        // ****************************************************************************************************************
        /// <summary>
        ///     Make sure the label we have is valid
        /// </summary>
        /// <param name="label">Label to check</param>
        /// <returns>
        ///     TRUE for valid, FALSE for invalid
        /// </returns>
        // ****************************************************************************************************************
        public bool ValidateLabel(string label)
        {
            label = label.ToUpper();
            string chars = "0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ:";

            foreach (char c in label)
            {
                if (chars.IndexOf(c) < 0) return false;
            }
            return true;
        }



        // ****************************************************************************************************************
        /// <summary>
        ///     Scan a single line of the .LST file, and pick out labels where we can
        /// </summary>
        /// <example>
        ///     <source file>|<src line>|<definition file>|<def line>|<page>|<value>|<type>|<data>
        /// </example>
        /// <param name="line">Single line of text</param>
        // ****************************************************************************************************************
        public void ScanLine(string line)
        {
            int bank_value = -1;
            int index = 0;
            string s = GetNext(line, index, out index);
            // remove trailing ":"
            if (s.EndsWith(":")) s = s.Substring(0, s.Length - 1);
            if (s == END_TXT) return;

            // Banked?
            if (s.StartsWith("B"))
            {
                int lindex = s.IndexOf(':');
                if (lindex >= 0)
                {
                    string bankstring = s.Substring(0, lindex);          // "B???:0000:
                    if(!BankLookup.TryGetValue(bankstring, out bank_value)) bank_value = -1;
                } 
                s = s.Substring(lindex + 1);
            }


            // read line number
            Int64 address = ReadHex(s);
            if (address == ERROR_VAL) return;                 // line doesn't start with a number, so not interested in it
            if (address < 0) return;

            string label = GetNext(line, index, out index);
            if (label.StartsWith(".")) label = label.Substring(1);


            long physical = 0;
            int bank = (int)(address / 8192);
            int offset = (int)(address & 0x1fff);
            if (bank_value >= 0)
            {
                physical = (bank_value * 8192) + (address & 0x1fff);
            }
            else
            {
                switch (bank)
                {
                    case 0:
                        physical = 0;
                        break;
                    case 1:
                        physical = 0;
                        break;
                    case 2:
                        physical = (10 * 8192) + offset;
                        break;
                    case 3:
                        physical = (11 * 8192) + offset;
                        break;
                    case 4:
                        physical = (4 * 8192) + offset;
                        break;
                    case 5:
                        physical = (5 * 8192) + offset;
                        break;
                    case 6:
                        physical = offset;
                        break;
                    case 7:
                        physical = (1 * 8192) + offset;
                        break;
                }
            }

            if (label.StartsWith("_")) label = label.Substring(1);
            label = label.Replace(".__far", "_far");
            label = label.Replace("__leave", "_leave");
            label = label.Replace("LABEL._", "_");
            if (!label.Contains("core."))
            {
                label = label.Replace(".__", "_");
            }
            label = label.Replace("__", "_");


            Symbol pSym = CSpect.AddSymbol(label, (int)address, (int)physical, eLabelType.Address);
        }


        // *************************************************************************************************************************************************
        /// <summary>
        ///     Load the Boriel map file
        /// </summary>
        /// <param name="pBuffer"></param>
        /// <returns>TRUE for okay, FALSE for error</returns>
        // *************************************************************************************************************************************************
        public bool LoadsjASMPlusFile(string[] pBuffer)
        {
            foreach (string line in pBuffer)
            {
                // debug
                /*if (line.Contains("SetUpIRQsSys"))
                {
                    int ototo = 12;
                }*/
                ScanLine(line);
            }
            return true;
        }
    }
}


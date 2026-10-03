using Plugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Threading.Tasks;

namespace Pasta80Symbols
{
    public class Pasta80_ListFile
    {
        /// <summary>Read HEX/Dec return error if no hex values found</summary>
        const Int64 ERROR_VAL = -123456789;
        /// <summary>Text used to determain the end of the line</summary>
        const string END_TXT = "[[END]]";

        /// <summary>Global labels to ignore </summary>
        List<string> Ignore = new List<string>()
        {
            "P","I","R"
        };

        /// <summary>The CSpect interface</summary>
        iCSpect CSpect;

        // *************************************************************************************************************************************************
        /// <summary>
        ///     Create new Pasta80 parser
        /// </summary>
        /// <param name="_CSpect">pointer to iCSpect interface</param>
        // *************************************************************************************************************************************************
        public Pasta80_ListFile(iCSpect _CSpect)
        {
            CSpect=_CSpect;
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
        ///     example lines:
        /// 
        ///     math48.asm(564): warning[opkeyword]: Label collides with one of the operator keywords, try capitalizing it or other name: MOD
        ///     tetris.z80(80): warning[fwdref]: forward reference of symbol: if      __USE__MemAvail7
        ///     # file opened: system.asm
        ///     # file closed: helpers.lua
        ///     system.asm(250): warning[fake]: Fake instruction: ld de, hl
        /// 
        ///     58+ 80CE AF           __int16_lt:     xor a
        ///     59+ 80CF ED 52                        sbc hl, de
        ///     7768 98EF CD 76 98                     call l2_set_pixel
        ///     60+ 80D1                              ; add hl, de
        ///     7790  9943              __USE__SetMemPage366:equ     0
        ///     7644  9835                              if      __USE__SetCpuSpeed362
        ///     7646  9835 ~__SetCpuSpeed362:                       ; Prologue
        ///     7647  9835 ~                           ld      hl,(display+2)
        ///     7609  9835 ~            ; var GetCpuSpeed(@RESULT)
        ///     7610  9835 ~__GetCpuSpeed360:                       ; Prologue
        /// </example>
        /// <param name="line">Single line of text</param>
        // ****************************************************************************************************************
        public void ScanLine(string line)
        {
            int index = 0;
            string s = GetNext(line, index, out index);
            if (s == END_TXT) return;

            // read line number
            if (s.EndsWith("+")) s = s.Substring(0, s.Length - 1);
            if (s.EndsWith("+")) s = s.Substring(0, s.Length - 1);
            Int64 v = ReadDec(s);
            if (v == ERROR_VAL) return;                 // line doesn't start with a number, so not interested in it
            if (v < 0) return;

            // Read addreess
            s = GetNext(line, index, out index);
            if (s == END_TXT) return;
            // read line number
            Int64 address = ReadHex(s);
            if (v == ERROR_VAL) return;                 // line doesn't start with a number, so not interested in it
            if (v < 0) return;

            // Now look for label
            int pos1 = line.IndexOf(';', index);
            if (pos1 < 0) pos1 = 0x7fff0000;
            int pos2 = line.IndexOf(':', index);
            if (pos2 < 0 || pos1 < pos2) return;                    // comment before :

            // Now scan backwards for whitespace.
            int start = pos2;
            while (start >= 0)
            {
                if (line[start] == ' ' || line[start] == '\t') break;
                start--;
            }
            string label = line.Substring(start, pos2 - start);
            label = label.Trim();
            if (string.IsNullOrEmpty(label)) return;
            if (!ValidateLabel(label)) return;

            if (label.StartsWith("__"))
            {
                label = label.Substring(2);
            }

            //  "7764 9991 00           global377:      ds      1,0             ; Global gBlockX"
            // Check to see if this is a global and has a mapping.
            int GlobPos = line.IndexOf("; Global ");
            string mappedlabel = "";
            if (GlobPos > 0)
            {
                GlobPos += "; Global ".Length;
                mappedlabel = line.Substring(GlobPos).Trim();
                foreach (string st in Ignore)
                {
                    if (st == mappedlabel)
                    {
                        mappedlabel = "";
                        break;
                    }
                }
            }

            int physical = 0;
            int bank = (int)(address / 8192);
            int offset = (int)(address & 0x1fff);
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

            if (string.IsNullOrEmpty(mappedlabel))
            {
                Symbol pSym = CSpect.AddSymbol(label, (int)address, (int)physical, eLabelType.Address);
            }
            else
            {
                Symbol pSym = CSpect.AddSymbol(mappedlabel, (int)address, (int)physical, eLabelType.Address);
            }
        }


        // *************************************************************************************************************************************************
        /// <summary>
        ///     Load the Pasta/80 map file
        /// </summary>
        /// <param name="pBuffer"></param>
        /// <returns>TRUE for okay, FALSE for error</returns>
        // *************************************************************************************************************************************************
        public bool LoadPasta80File(string[] pBuffer)
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


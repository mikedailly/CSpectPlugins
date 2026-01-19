// ********************************************************************************************************************************************
//      CSpect esxDOS extension, allowing access to the RST $08 function in the CSpect emulator
//      Written by:
//                  Mike Dailly
//
//      Released under the GNU 3 license - please see license file for more details
//
//      This extension uses the EXE extension method and traps trying to execute an instruction at RST $08,
//      and the Read/Write on IO ports for file streaming
//
// ********************************************************************************************************************************************
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SqlTypes;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Plugin;
using System.Runtime.InteropServices;
using System.Windows.Forms;


namespace DebugOut
{

    // **********************************************************************
    /// <summary>
    ///     A simple, empty i2C device
    /// </summary>
    // **********************************************************************
    public class DBGOut : iPlugin
    {
        #region Plugin interface

        /// <summary>Which address are we tagging on to?</summary>
        public const int PC_Address = 0x18;

        /// <summary>Z80 CPU flag bits</summary>
        [Flags]
        public enum eCPUFlags : UInt16
        {
            None = 0x00,
            C = 0x01,
            N = 0x02,
            PV = 0x04,
            b3 = 0x08,
            H = 0x10,
            b5 = 0x20,
            Z = 0x40,
            S = 0x80
        };

        /// <summary>The current registers at RST $08 call time - updated upon return</summary>
        Z80Regs regs;
        public iCSpect CSpect;

        // **********************************************************************
        /// <summary>
        ///     Init the RST 0x08 interface
        /// </summary>
        /// <returns>
        ///     List of addresses we're monitoring
        /// </returns>
        // **********************************************************************
        public List<sIO> Init(iCSpect _CSpect)
        {
            CSpect = _CSpect;
            bool active = (bool)CSpect.GetGlobal(eGlobal.esxDOS);
            if (!active) return null;

            Console.WriteLine("DebugOut added");

            // create a list of the ports we're interested in
            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO(PC_Address, eAccess.Memory_EXE));           // trap execution of RST $18

            return ports;
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
        ///     Called when machine is reset
        /// </summary>
        // **********************************************************************
        public void Reset()
        {
        }

        // **********************************************************************
        /// <summary>
        ///     Called once an emulation FRAME
        /// </summary>
        // **********************************************************************
        public void Tick()
        {
        }


        // ******************************************************************************************
        /// <summary>
        ///     Called once an OS emulator frame - do all UI rendering, opening windows etc here.
        /// </summary>
        // ******************************************************************************************
        public void OSTick()
        {
        }

        // **********************************************************************
        /// <summary>
        ///     Key press callback
        /// </summary>
        /// <param name="_id">The registered key ID</param>
        /// <returns>
        ///     True indicates the plugin handled the keey
        ///     False indicates someone else can handle it
        /// </returns>
        // **********************************************************************
        public bool KeyPressed(int _id)
        {
            return true;
        }
        // **********************************************************************
        /// <summary>
        ///     
        /// </summary>
        /// <param name="_port">Port/Address</param>
        /// <param name="_isvalid"></param>
        /// <returns></returns>
        // **********************************************************************
        public byte Read(eAccess _type, int _port, int _id, out bool _isvalid)
        {
            _isvalid = false;

            // esxDOS active? (when full NEXT ROM active, we're disabled)
            bool active = (bool)CSpect.GetGlobal(eGlobal.esxDOS);
            if (!active) return 0;

            if (_type == eAccess.Memory_EXE && _port == PC_Address)
            {
                // ROM paged out?
                int b = CSpect.GetNextRegister(0x50);
                if (b != 255) return 0;

                _isvalid = true;
                DoDebugPrint();
                return (byte)0;
            }
            return 0;
        }
        // **********************************************************************
        /// <summary>
        ///     Write a value to one of the registered ports
        /// </summary>
        /// <param name="_port">the port being written to</param>
        /// <param name="_value">the value to write</param>
        // **********************************************************************
        public bool Write(eAccess _type, int _port, int _id, byte _value)
        {
            return false;
        }

        #endregion



        //****************************************************************************
        /// <summary>
        ///     Pop off the stack
        /// </summary>
        /// <returns>popped value</returns>
        //****************************************************************************
        int Pop()
        {
            int sp = regs.SP;
            int t = CSpect.Peek((ushort)sp++);
            t |= ((int)CSpect.Peek((ushort)(sp & 0xffff))) << 8;
            regs.SP = (ushort)(++sp & 0xffff);
            return t;
        }


        //****************************************************************************
        /// <summary>
        ///     Do open/read/write/close ops - pretend to be an MMC card 
        /// </summary>
        //****************************************************************************
        public void DoDebugPrint()
        {
            bool NoRegs = false;
            string DebugString = "";
            int add = 0x0000;
            UInt32[] digitmask = { 0, 0xf, 0xff, 0xfff, 0xffff, 0xfffff, 0xffffff, 0xfffffff, 0xffffffff };

            regs = CSpect.GetRegs();
            regs.PC = (UInt16) Pop();
            
            // if the next byte is $ff (RST $38), then assume no register usage, and message is directly after.
            byte NextOp = CSpect.Peek((UInt16)regs.PC);

            if (NextOp != 0xff)
            {
                add = regs.HL;
            }
            else
            {
                regs.PC++;
                add = CSpect.Peek((UInt16)regs.PC++);
                add |= ((int)CSpect.Peek((UInt16)regs.PC++)<<8);
            }


            // 1k MAX size for the string - just in case
            while (DebugString.Length < 1024)
            {
                byte c = CSpect.Peek((UInt16)add++);
                if (c == 0x00) break;
                DebugString += (char)c;
            }

            // ADD now points to the first argument

            string final_string = "";
            // Now loop through string and do a "printf()" argument replacing
            int index = 0;
            while(index<DebugString.Length)
            {
                char c = (char)DebugString[index++];
                
                // literal
                if (c == '\\')
                {
                    c = (char)DebugString[index++];
                    if (c == 'r') c = (char)13;
                    else if (c == 'n') c = (char)10;

                    final_string += c;
                }
                else if( c== '%')
                {
                    int digits = 32;
                    c = (char)DebugString[index++];
                    if(c>='0' && c <= '9')
                    {
                        digits = 0;
                        index--;
                        while(DebugString[index]>='0' && DebugString[index]<'9')
                        {
                            c = (char)DebugString[index++];
                            if (c == 0x00) break;
                            digits *= 10;
                            digits = (int)c - (int)'0';
                        }
                        if (digits == 0) digits = 1;

                        c = (char)DebugString[index++];
                    }

                    switch (c)
                    {
                        // hex output
                        case 'x':
                        case 'X':
                            {
                                UInt32 v = CSpect.Peek((UInt16)add++);
                                v |= (((UInt32)CSpect.Peek((UInt16)add++))<<8);
                                v |= (((UInt32)CSpect.Peek((UInt16)add++)) << 16);
                                v |= (((UInt32)CSpect.Peek((UInt16)add++)) << 24);
                                if (digits < 0) digits = 0;
                                if (digits > 8) digits = 8;
                                v &= digitmask[digits];
                                string hex = string.Format("{0:"+c + digits.ToString() + "}", v);
                                final_string += hex;
                                break;
                            }
                        case 'd':
                            {
                                Int32 v = CSpect.Peek((UInt16)add++);
                                v |= (((Int32)CSpect.Peek((UInt16)add++)) << 8);
                                v |= (((Int32)CSpect.Peek((UInt16)add++)) << 16);
                                v |= (((Int32)CSpect.Peek((UInt16)add++)) << 24);
                                final_string += v.ToString();
                                break;
                            }
                        case 'b':
                            {
                                Int32 v = CSpect.Peek((UInt16)add++);
                                v |= (((Int32)CSpect.Peek((UInt16)add++)) << 8);
                                v |= (((Int32)CSpect.Peek((UInt16)add++)) << 16);
                                v |= (((Int32)CSpect.Peek((UInt16)add++)) << 24);

                                if (digits < 0) digits = 1;
                                if (digits > 32) digits = 32;

                                string bin = "";
                                for(int i = (digits-1); i >= 0; i--)
                                {
                                    if( (v&(1<<i))==0) bin += "0"; else bin += "1";
                                }
                                final_string += bin;
                                break;
                            }
                        case 's':
                            {
                                UInt32 subAdd = CSpect.Peek((UInt16)add++);
                                subAdd |= (((UInt32)CSpect.Peek((UInt16)add++)) << 8);

                                // Insert substring.
                                string SubString = "";
                                while (SubString.Length < 1024)
                                {
                                    byte ch = CSpect.Peek((UInt16)subAdd++);
                                    if (ch == 0x00) break;
                                    SubString += (char)ch;
                                }

                                final_string += SubString;
                                break;
                            }

                        // output register
                        case 'r':
                        case 'R':
                            {
                                string _case = "x";
                                if (c == 'R') _case = "X";

                                string rg = ""+DebugString[index++];
                                rg += DebugString[index++];

                                switch (rg)
                                {
                                    case "af": final_string += string.Format("{0:" + _case + "4}", regs.AF); break;
                                    case "bc": final_string += string.Format("{0:" + _case + "4}", regs.BC); break;
                                    case "de": final_string += string.Format("{0:" + _case + "4}", regs.DE); break;
                                    case "hl": final_string += string.Format("{0:" + _case + "4}", regs.HL); break;
                                    case "AF": final_string += string.Format("{0:" + _case + "4}", regs._AF); break;
                                    case "BC": final_string += string.Format("{0:" + _case + "4}", regs._BC); break;
                                    case "DE": final_string += string.Format("{0:" + _case + "4}", regs._DE); break;
                                    case "HL": final_string += string.Format("{0:" + _case + "4}", regs._HL); break;
                                    case "IX":
                                    case "ix": final_string += string.Format("{0:" + _case + "4}", regs.IX); break;
                                    case "IY":
                                    case "iy": final_string += string.Format("{0:" + _case + "4}", regs.IY); break;
                                    case "SP":
                                    case "sp": final_string += string.Format("{0:" + _case + "4}", regs.SP); break;
                                    case "PC":
                                    case "pc": final_string += string.Format("{0:" + _case + "4}", regs.PC); break;
                                    case "II":
                                    case "ii": final_string += string.Format("{0:" + _case + "2}", regs.I); break;
                                    case "IM":
                                    case "im": final_string += string.Format("{0:" + _case + "2}", regs.IM); break;
                                }
                                break;
                            }
                        default:
                            break;
                    }
                }
                else
                {
                    final_string += c;
                }
            }

            // force a new line - never know
            Console.WriteLine(final_string);            

            // send the registers back...
            CSpect.SetRegs(regs);
        }
    }
}

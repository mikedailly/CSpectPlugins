using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using Microsoft.Win32;
using Plugin;

namespace FileAssociate
{
    // *********************************************************************************************************
    /// <summary>
    ///     The copper disassembler
    /// </summary>
    // *********************************************************************************************************
    class FileAssociationPlugin : iPlugin
    {
        /// <summary>CSpect emulator interface</summary>
        iCSpect CSpect;
        bool DoFileAssociate = false;

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
            Console.WriteLine("Copper Disassembler added");

            CSpect = _CSpect;

            // Detect keypress for starting disassembler
            List<sIO> ports = new List<sIO>();

            // Only on windows
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                ports.Add(new sIO("<ctrl><alt><shift>a", eAccess.KeyPress, 0));     // Add file assosiation
            }
            return ports;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Key pressed callback.
        /// </summary>
        /// <param name="_id"></param>
        /// <returns></returns>
        // ******************************************************************************************
        public bool KeyPressed(int _id)
        {
            if (_id == 0)
            {
                DoFileAssociate = true;
            }
            return true;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Showdown - free any non-managed resources
        /// </summary>
        // ******************************************************************************************
        public void Quit()
        {
        }

        // ******************************************************************************************
        /// <summary>
        ///     Read access type - not used
        /// </summary>
        /// <param name="_type"></param>
        /// <param name="_address"></param>
        /// <param name="_isvalid"></param>
        /// <returns></returns>
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
        ///     Called once an emulator frame - update copper if "Active"
        /// </summary>
        // ******************************************************************************************
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
            if(DoFileAssociate)
            {
                DoFileAssociate = false;
                RegFunctions r = new RegFunctions();
                r.EnsureAssociationsSet();
            }
        }


        // ******************************************************************************************
        /// <summary>
        ///     Write access type - not used
        /// </summary>
        /// <param name="_type"></param>
        /// <param name="_port"></param>
        /// <param name="_value"></param>
        /// <returns></returns>
        // ******************************************************************************************
        public bool Write(eAccess _type, int _port, int _id, byte _value)
        {
            return false;
        }


    }
}

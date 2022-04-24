using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plugin;

namespace CopperDissassembler
{
    // *********************************************************************************************************
    /// <summary>
    ///     The copper disassembler
    /// </summary>
    // *********************************************************************************************************
    class CopperPlugin : iPlugin
    {
        /// <summary>CSpect emulator interface</summary>
        iCSpect CSpect;
        public static bool Active;
        public static CopperDissForm form;
        byte[] CopperMemory = new byte[2048];
        bool[] CopperIsWritten= new bool[1024];

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
            Debug.WriteLine("Copper Disassembler added");

            CSpect = _CSpect;

            // Detect keypress for starting disassembler
            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO("<ctrl><alt>c", eAccess.KeyPress, 0));                   // Key press callback
            ports.Add(new sIO("<ctrl><alt>s", eAccess.KeyPress, 1));                   // toggle copper/irq visualiser
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
                if (Active) return true;
                Active = true;
                form = new CopperDissForm(CopperMemory, CopperIsWritten);
                form.Show();
            }
            else if (_id == 1)
            {
                // Toggle the copper wait and IRQ trigger visualiser
                bool b = (bool)CSpect.GetGlobal(eGlobal.copper_wait);
                bool i = (bool)CSpect.GetGlobal(eGlobal.irq_wait);
                CSpect.SetGlobal(eGlobal.copper_wait, !b);
                CSpect.SetGlobal(eGlobal.irq_wait, !i);
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
            if (!Active) return;

            bool doinvalidate = false;
            for (int i = 0; i < 2048; i++) {
                byte b = CSpect.CopperRead(i);
                if (CopperMemory[i] != b) doinvalidate = true;
                CopperMemory[i] = b;
            }
            for (int i = 0; i < 2048; i+=2)
            {
                bool b = CSpect.CopperIsWritten(i);
                CopperIsWritten[i>>1] = b;
            }
            if (doinvalidate) form.Invalidate();     // refresh IF it's changed
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

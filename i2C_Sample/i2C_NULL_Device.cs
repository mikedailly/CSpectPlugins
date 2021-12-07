using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plugin;

namespace i2C_Sample
{

    // **********************************************************************
    /// <summary>
    ///     A simple, empty i2C device
    /// </summary>
    // **********************************************************************
    public class i2C_NULL_Device : iPlugin
    {        
        public const int PORT_CLOCK = 0x103b;
        public const int PORT_DATA = 0x113b;
        public bool m_Internal = false;

        public iCSpect CSpect;
        // **********************************************************************
        /// <summary>
        ///     Init the device
        /// </summary>
        /// <returns>
        ///     List of ports we're registering
        /// </returns>
        // **********************************************************************
        public List<sIO> Init(iCSpect _CSpect)
        {
            Debug.WriteLine("NULL i2C device Added");

            CSpect = _CSpect;            

            // create a list of the ports we're interested in
            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO(PORT_CLOCK, eAccess.Port_Read));
            ports.Add(new sIO(PORT_DATA, eAccess.Port_Read));
            ports.Add(new sIO(PORT_CLOCK, eAccess.Port_Write));
            ports.Add(new sIO(PORT_DATA, eAccess.Port_Write));
            ports.Add(new sIO(0xfe, eAccess.Port_Write));
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
        ///     Call back when machine is reset
        /// </summary>
        // **********************************************************************
        public void Reset()
        {
        }

        // **********************************************************************
        /// <summary>
        ///     Called once an emulation frame
        /// </summary>
        // **********************************************************************
        public void Tick()
        {/*
            byte bank0 = CSpect.GetNextRegister(0x50);
            byte bank1 = CSpect.GetNextRegister(0x51);
            byte bank2 = CSpect.GetNextRegister(0x52);
            byte bank3 = CSpect.GetNextRegister(0x53);
            byte bank4 = CSpect.GetNextRegister(0x54);
            byte bank5 = CSpect.GetNextRegister(0x55);
            byte bank6 = CSpect.GetNextRegister(0x56);
            byte bank7 = CSpect.GetNextRegister(0x57);

            Z80Regs regs = CSpect.GetRegs();
            byte[] mem = CSpect.PeekPhysical(0x10000, 10);

            if( ( (regs.AF&0xff)==0x12 ) && mem[0]==0x11 ){
                CSpect.Debugger(eDebugCommand.Enter);
            }
            */
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
        ///     Write a value to one of the registered ports
        /// </summary>
        /// <param name="_port">the port being written to</param>
        /// <param name="_value">the value to write</param>
        // **********************************************************************
        public bool Write(eAccess _type, int _port, byte _value)
        {
            //if (_type == eAccess.Port_Write && _port == 0xfe)
            //{
            //    return true;
            //}


            switch (_port)
            {
                case PORT_CLOCK: break;
                case PORT_DATA: break;
                case 0xfe:
                    {
                        //CSpect.Debugger(eDebugCommand.Enter);
                        return false;    // let CSpect process this
                    }
            }
            return true;
        }


        // **********************************************************************
        /// <summary>
        ///     
        /// </summary>
        /// <param name="_port"></param>
        /// <param name="_isvalid"></param>
        /// <returns></returns>
        // **********************************************************************
        public byte Read(eAccess _type, int _port, out bool _isvalid)
        {
            _isvalid = false;
            if ( _type == eAccess.Port_Read && _port==0xfe)
            {
                if (m_Internal) return 0;
                m_Internal = true;
                int i = CSpect.InPort(0xfe);
                m_Internal = false;
                _isvalid = true;
                return (byte)(i&0xff); 
            }

            switch (_port)
            {
                case PORT_CLOCK: _isvalid = true; return 0xff;
                case PORT_DATA: _isvalid = true; return 0xff;
            }
            _isvalid = false;
            return 0;
        }
    }
}

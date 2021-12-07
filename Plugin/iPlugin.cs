using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin
{
    // ********************************************************
    /// <summary>
    ///     Type of access
    /// </summary>
    // ********************************************************
    public enum eAccess
    {
        /// <summary>All READ data comes FROM this port</summary>
        Port_Read = 1,
        /// <summary>All WRITE data goes TO this port</summary>
        Port_Write = 2,
        /// <summary>All reads to this address come from this plugin</summary>
        Memory_Read = 3,
        /// <summary>All writes from this address come from this plugin</summary>
        Memory_Write = 4,
        /// <summary>Next register write</summary>
        NextReg_Write = 5,
        /// <summary>Next register read</summary>
        NextReg_Read = 6,
        /// <summary>CPU Execute (16bit address)</summary>
        Memory_EXE = 7,
        /// <summary>Key has been pressed (PC keyboard format key)</summary>
        KeyPress = 8
    };


    // ********************************************************
    /// <summary>
    ///     IO access structure
    /// </summary>
    // ********************************************************
    public struct sIO
    {
        /// <summary>The port to register</summary>
        public int Port;

        /// <summary>String used in the command</summary>
        public string CMD;

        /// <summary>The type of port access</summary>
        public eAccess Type;

        // ********************************************************
        /// <summary>
        ///     Create a new IO interface
        /// </summary>
        /// <param name="_port">The port/address to attach to</param>
        /// <param name="_type">The type of access</param>
        // ********************************************************
        public sIO(int _port, eAccess _type)
        {
            Port = _port;
            Type = _type;
            CMD = "";
        }
        // ********************************************************
        /// <summary>
        ///     Create a new IO interface
        /// </summary>
        /// <param name="_cmd">The cmd/key to attach to</param>
        /// <param name="_id">value returned on callback to easily ID keys etc</param>
        /// <param name="_type">The type of access</param>
        // ********************************************************
        public sIO(string _cmd, int _id, eAccess _type)
        {
            CMD = _cmd;
            Type = _type;
            Port = _id;
        }
    }

    // ********************************************************
    /// <summary>
    ///     The Plugin interface
    /// </summary>
    // ********************************************************
    public interface iPlugin
    {
        // -------------------------------------------------------
        /// <summary>
        ///     Called once an emulation frame
        /// </summary>
        // -------------------------------------------------------
        void Tick();

        // -------------------------------------------------------
        /// <summary>
        /// Write to one of the registered ports
        /// </summary>
        /// <param name="_address">The port/address top write to</param>
        /// <param name="_value">The value to write</param>
        /// <returns>
        ///     True to indicate if the write has been dealt with
        /// </returns>
        // -------------------------------------------------------
        bool Write(eAccess _type, int _port, byte _value );

        // -------------------------------------------------------
        /// <summary>
        ///     Read from a registered port ( or cpu execute )
        /// </summary>
        /// <param name="_address">The port/address to read from</param>
        /// <param name="_isvalid">Is the data valid? (if false, checks next device)</param>
        /// <returns>
        ///     Byte to return, or ignored if _isvalid == false
        /// </returns>
        // -------------------------------------------------------
        byte Read(eAccess _type, int _address, out bool _isvalid);

        // -------------------------------------------------------
        /// <summary>
        ///     Key press callback. valid controls are "<ctrl>","<shift>" and "<alt>"
        ///     example: "<ctrl><alt>c"
        /// </summary>
        /// <param name="_id">The registered key ID</param>
        /// <returns>
        ///     True indicates the plugin handled the keey
        ///     False indicates someone else can handle it
        /// </returns>
        // -------------------------------------------------------
        bool KeyPressed(int _id);

        // -------------------------------------------------------
        /// <summary>
        ///     Init the plugin
        /// </summary>
        /// <param name="_CSpect">CSpect</param>
        /// <returns>
        ///     A list of IO read/write requests
        /// </returns>
        // -------------------------------------------------------
        List<sIO> Init( iCSpect _CSpect );

        // -------------------------------------------------------
        /// <summary>
        ///     Callback for when the machine is reset
        /// </summary>
        // -------------------------------------------------------
        void Reset();


        // -------------------------------------------------------
        /// <summary>
        ///     Quit the plugin - allowing freeing of resources
        /// </summary>
        // -------------------------------------------------------
        void Quit();
    }
}

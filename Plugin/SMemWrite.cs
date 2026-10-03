using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin
{
    // *******************************************************************************************
    /// <summary>
    ///     Write access flags
    /// </summary>
    // *******************************************************************************************
    [Flags]
    public enum eMemWriteFlags : ushort
    { 
        /// <summary>No flags set</summary>
        none = 0,
        /// <summary>Location has been written</summary>
        written,
    }

    // *******************************************************************************************
    /// <summary>
    ///  For every memory location, we store the PC address of what wrote to it
    /// </summary>
    // *******************************************************************************************
    public struct SMemWrite
    {
        /// <summary>Memory access flags</summary>
        public eMemWriteFlags flags;
        /// <summary>The Z80 address of write access</summary>
        public ushort PC;
        /// <summary>The PHYSICAL address of write access</summary>
        public int PhysicalAddress;
    }
}

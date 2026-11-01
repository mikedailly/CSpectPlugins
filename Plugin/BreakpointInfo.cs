namespace Plugin
{
    // ************************************************************************************************
    /// <summary>
    ///     Breakpoint kinds (iCSpect.ListBreakpoints)
    /// </summary>
    // ************************************************************************************************
    public enum eBreakpointType
    {
        /// <summary>Execute, on a Z80 64K address</summary>
        Execute,
        /// <summary>Execute, on a physical address (bank8k*$2000+offset) - only in that bank</summary>
        PhysicalExecute,
        /// <summary>Memory read, on a Z80 64K address</summary>
        Read,
        /// <summary>Memory write, on a Z80 64K address</summary>
        Write,
        /// <summary>IN from a 16 bit port</summary>
        InPort,
        /// <summary>OUT to a 16 bit port</summary>
        OutPort,
    }

    // ************************************************************************************************
    /// <summary>
    ///     A breakpoint (iCSpect.ListBreakpoints)
    /// </summary>
    // ************************************************************************************************
    public class BreakpointInfo
    {
        /// <summary>What kind</summary>
        public eBreakpointType Type;
        /// <summary>64K address, physical address or port, by Type</summary>
        public int Address;
    }
}

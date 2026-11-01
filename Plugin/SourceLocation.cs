namespace Plugin
{
    // ************************************************************************************************
    /// <summary>
    ///     A line of source, for source level debugging (iCSpect.GetSourceLocation)
    /// </summary>
    // ************************************************************************************************
    public class SourceLocation
    {
        /// <summary>Source file index (as iCSpect.GetSourceFiles)</summary>
        public int File;
        /// <summary>Line number in that file (1 = first line)</summary>
        public int Line;
        /// <summary>Physical address where this line's code starts</summary>
        public int Address;
    }
}

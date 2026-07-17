using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plugin
{
    // ************************************************************************************************
    /// <summary>
    ///     Symbol Definition
    /// </summary>
    // ************************************************************************************************
    public class Symbol
    {
        /// <summary>type of symbol</summary>
        public eLabelType type;

        /// <summary>Symbol Name</summary>
        public string name;

        /// <summary>value</summary>
        public int value;
        /// <summary>extra user value</summary>
        public int PhysicalAddress;

        /// <summary>Name of file this symbol was defined in.</summary>
        public string pFileName;
        /// <summary>Line number symbol was defined at.</summary>
        public int LineNumber;
    }
}

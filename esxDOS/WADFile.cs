using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace esxDOS
{
    /// <summary>
    ///     A single WADFile
    /// </summary>
    internal class WADFile
    {
        // ***********************************************************************************************************************
        /// <summary>
        ///     Get length of the file in bytes
        /// </summary>
        // ***********************************************************************************************************************
        int Length
        {
            get
            {
                if( Data==null) return 0;
                return Data.Length;
            }
        }

        /// <summary>Actual file data</summary>
        public byte[] Data;
        /// <summary>Filename</summary>
        public string Name;
        /// <summary>Is the file "dirty"?</summary>
        public bool Dirty;


        // ***********************************************************************************************************************
        /// <summary>
        ///     Create a new WAD file
        /// </summary>
        /// <param name="data"></param>
        /// <param name="name"></param>
        // ***********************************************************************************************************************
        public WADFile(byte[] data, string name)
        {
            Data = data;
            Name = name;
            Dirty = false;
        }

        // ***********************************************************************************************************************
        /// <summary>
        ///     Create a new WAD file
        /// </summary>
        /// <param name="data"></param>
        /// <param name="name"></param>
        // ***********************************************************************************************************************
        public WADFile(string name, int size = 16384)
        {
            if (size == 0)
            {
                Data = null;
            }
            else 
            {
                Data = new byte[size];
            }            
            Name = name;
            Dirty = true;
        }
    }
}

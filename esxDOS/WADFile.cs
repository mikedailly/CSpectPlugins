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
        int Size
        {
            get
            {
                if( Data==null) return 0;
                return Data.Length;
            }
        }

        /// <summary>Actual file data</summary>
        byte[] Data;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NextRegisterViewer
{
    public class RegDetails
    {
        /// <summary>All next registers</summary>
        public byte[] NextRegisters = new byte[256];
        /// <summary>highlight counter</summary>
        public int[] RegisterIsWritten = new int[256];
        /// <summary>The 4 clip window edges, with [4] being the current index</summary>
        public int[] ClipWindowLayer2 = new int[5];
        /// <summary>The 4 clip window edges, with [4] being the current index</summary>
        public int[] ClipWindowSprites = new int[5];
        /// <summary>The 4 clip window edges, with [4] being the current index</summary>
        public int[] ClipWindowULA = new int[5];
        /// <summary>The 4 clip window edges, with [4] being the current index</summary>
        public int[] ClipWindowTilemap = new int[5];
    }
}

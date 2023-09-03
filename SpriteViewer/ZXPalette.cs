using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpriteViewer
{
    class ZXPalette
    {
        public static UInt32[] SpritePalette1;
        public static UInt32[] SpritePalette2;
        public static UInt32[] DefaultColours_NeverChanges;

        /// <summary>RRR and GGG and BBB colour codes</summary>
        public static int[] ColourBaseRG = { 0, 36, 73, 109, 146, 182, 219, 255 };

        // ****************************************************************************************************************************
        /// <summary>
        ///     Init the ZX Palette we'll use for drawing
        /// </summary>
        // ****************************************************************************************************************************
        public static void Init()
        {
            int i;
            UInt32 col;

            SpritePalette1 = new UInt32[256];
            SpritePalette2 = new UInt32[256];
            DefaultColours_NeverChanges = new uint[512];

            // setup default colours
            for (i = 0; i < 256; i++)
            {
                uint col9;
                col = (uint)((i << 1) & 0x1fe);
                col9 = col | (((col & 2) >> 1) | ((col & 4) >> 2));

                SpritePalette1[i] = col9;
                SpritePalette2[i] = col9;
            }


            // generate the actual 512 colours we'll render. These are FIXED and next palettes look up INTO this 
            for (i = 0; i < 512; i++)
            {
                UInt32 colr = (UInt32)ColourBaseRG[((i & 0x1c0) >> 6)] << 16;
                UInt32 colg = (UInt32)ColourBaseRG[((i & 0x38) >> 3)] << 8;
                UInt32 colb = (UInt32)ColourBaseRG[(i & 7)];
                col = 0xff000000 | colr | colg | colb;
                DefaultColours_NeverChanges[i] = col;
            }
        }

        /// <summary>
        ///     Get a Spectrum colour
        /// </summary>
        /// <param name="_index">the colour to decode</param>
        /// <returns></returns>
        public static UInt32 Get(int _index)
        {
            uint c= SpritePalette1[_index];
            return DefaultColours_NeverChanges[c];
        }


    }
}

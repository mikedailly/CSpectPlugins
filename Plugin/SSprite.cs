using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Plugin
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SSprite
    {
        /// <summary>Sprite X coordinate</summary>
        public byte x;
        /// <summary>Sprite Y coordinate</summary>
        public byte y;
        /// <summary>
        /// <para>P P P P XM YM R X8/PR</para>
        /// Palette offset, mirror, flip, rotate and MSB</summary>
        public byte paloff_mirror_flip_rotate_xmsb;
        /// <summary>
        ///     <para>Sprite visibility and name - V E N5 N4 N3 N2 N1 N0 - E=Enable Attrib4</para>
        ///     <para>V = 1 to make the sprite visible</para>
        ///     <para>E = 1 to enable attribute byte 4</para>
        ///     <para>N = Sprite pattern to use 0-63</para>
        ///     <para>If E = 0, the sprite is fully described by sprite attributes 0-3. The sprite pattern is an 8-bit one identified by pattern N = 0 - 63.The sprite is an anchor and cannot be made relative.The sprite is displayed as if sprite attribute 4 is zero.</para>
        ///     <para>If E = 1, the sprite is further described by sprite attribute 4.</para>
        /// </summary>
        public byte visible_name;

        /// <summary>
        ///     <para>H=1 means this sprite uses 4-bit patterns</para>
        ///     <para>N6 = 0 chooses the top 128 bytes of the 256-byte pattern otherwise the bottom 128 bytes</para>
        ///     <para>T  = 0 if relative sprites are composite type else 1 for unified type</para>
        ///     <para>XX = expand on X (0-3=16,32,64,128)</para>
        ///     <para>YY = expand on Y (0-3=16,32,64,128)</para>
        ///     <para>Y8 = Extra Y coordinate bit</para>
        ///     <para>Relative sprite mode</para>
        ///     <para>7  = 0 means this sprite uses 4-bit patterns</para>
        ///     <para>6  = 1 means this sprite uses 4-bit patterns</para>
        ///     <para>N6 = 1 chooses the top 128 bytes of the 256-byte pattern otherwise the bottom 128 bytes</para>
        ///     <para>XX = expand on X (0-3=16,32,64,128)</para>
        ///     <para>YY = expand on Y (0-3=16,32,64,128)</para>
        ///     <para>P0 = Shape is relative</para>
        /// </summary>
        public byte H_N6_0_XX_YY_Y8;

        public void Clear()
        {
            x = 0;
            y = 0;
            paloff_mirror_flip_rotate_xmsb = 0;
            visible_name = 0;
        }
    };

}

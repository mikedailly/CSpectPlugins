using System;

namespace SpriteViewer
{
    public class ZXSprite
    {
        // ******************************************************************************************
        /// <summary>
        ///     Draw a 16x16 sprite pattern into an ARGB image
        /// </summary>
        /// <param name="_dest">ARGB destination image</param>
        /// <param name="_destStride">destination width in pixels</param>
        /// <param name="_dx">destination X</param>
        /// <param name="_dy">destination Y</param>
        /// <param name="_Is16Col">4 bit (128 byte) patterns? else 8 bit (256 byte)</param>
        /// <param name="_index">pattern number</param>
        /// <param name="_SpritePalette">palette offset (added to every pixel)</param>
        /// <param name="_buffer">sprite pattern memory</param>
        /// <param name="_palette">sprite palette, 9 bit RRRGGGBBB per entry</param>
        // ******************************************************************************************
        public static void DrawSprite(UInt32[] _dest, int _destStride, int _dx, int _dy, bool _Is16Col, int _index, int _SpritePalette, byte[] _buffer, uint[] _palette)
        {
            if (_Is16Col)
            {
                int src = 128 * _index;
                for (int y = 0; y < 16; y++)
                {
                    int dest = ((_dy + y) * _destStride) + _dx;
                    for (int x = 0; x < 8; x++)
                    {
                        byte b = _buffer[src++];
                        _dest[dest++] = Colour(_palette, ((b >> 4) & 0xf) + _SpritePalette);
                        _dest[dest++] = Colour(_palette, (b & 0xf) + _SpritePalette);
                    }
                }
            }
            else
            {
                int src = 256 * _index;
                for (int y = 0; y < 16; y++)
                {
                    int dest = ((_dy + y) * _destStride) + _dx;
                    for (int x = 0; x < 16; x++)
                    {
                        _dest[dest++] = Colour(_palette, _buffer[src++] + _SpritePalette);
                    }
                }
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Palette index -> ARGB
        /// </summary>
        // ******************************************************************************************
        static UInt32 Colour(uint[] _palette, int _index)
        {
            return ZXPalette.DefaultColours_NeverChanges[_palette[_index & 0xff] & 0x1ff];
        }
    }
}

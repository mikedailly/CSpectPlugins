using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpriteViewer
{
    public class ZXSprite
    {
        /// <summary>
        ///     Draw a sprite into a bitmap
        /// </summary>
        /// <param name="_buffer"></param>
        public static unsafe void DrawSprite(Graphics _g, Bitmap _bmp, bool _Is16Col, int _index, int _SpritePalette, byte[] _buffer)
        {
            BitmapData data = _bmp.LockBits(
                        new Rectangle(0, 0, 16, 16),
                        System.Drawing.Imaging.ImageLockMode.ReadWrite,
                        _bmp.PixelFormat);
            byte* pData8 = (byte*)data.Scan0;


            if (_Is16Col)
            {
                _index = 128 * _index;
                for (int y = 0; y < _bmp.Height; y++)
                {
                    int dest = 0;
                    UInt32* pLine = (UInt32*) (pData8 + (y * data.Stride));
                    for (int x = 0; x < (_bmp.Width/2); x++)
                    {
                        int c = (_buffer[_index]>>4)&0xf;
                        UInt32 col = ZXPalette.Get( (c + _SpritePalette)&0xff ); // (14 * 16));
                        pLine[dest++] = col;
                        c = (_buffer[_index++] & 0xf);
                        col = ZXPalette.Get((c + _SpritePalette) & 0xff);
                        pLine[dest++] = col;
                    }
                }
            } 
            else
            {
                _index = 256 * _index;
                for (int y = 0; y < _bmp.Height; y++)
                {
                    UInt32* pLine = (UInt32*)(pData8 + (y * data.Stride));
                    for (int x = 0; x < _bmp.Width; x++)
                    {
                        byte c = _buffer[_index++];
                        UInt32 col = ZXPalette.Get((int)(c+ _SpritePalette)&0xff);
                        pLine[x] = col;
                    }
                }
            }
            _bmp.UnlockBits(data);
        }

    }
}

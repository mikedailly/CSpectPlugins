// ********************************************************************************************************************************************
//      CSpect MCP server - a tiny PNG encoder (System.Drawing needs libgdiplus on Mono; this works everywhere)
//      Written by:
//                  Mike Dailly
//
//      Released under the GNU 3 license - please see license file for more details
// ********************************************************************************************************************************************
using System;
using System.IO;
using System.IO.Compression;

namespace MCP
{
    // *********************************************************************************************************
    /// <summary>
    ///     Encode 32 bit pixels as an RGB PNG
    /// </summary>
    // *********************************************************************************************************
    public static class Png
    {
        static uint[] CrcTable;

        // ****************************************************************************************
        /// <summary>
        ///     Make a PNG
        /// </summary>
        /// <param name="_pixels">0xAARRGGBB pixels, row by row</param>
        /// <param name="_width">Width</param>
        /// <param name="_height">Height</param>
        /// <returns>The PNG file</returns>
        // ****************************************************************************************
        public static byte[] Encode(uint[] _pixels, int _width, int _height)
        {
            // raw scanlines: filter byte 0 + RGB
            int stride = 1 + _width * 3;
            byte[] raw = new byte[stride * _height];
            int o = 0, p = 0;
            for (int y = 0; y < _height; y++)
            {
                raw[o++] = 0;
                for (int x = 0; x < _width; x++)
                {
                    uint c = _pixels[p++];
                    raw[o++] = (byte)(c >> 16);
                    raw[o++] = (byte)(c >> 8);
                    raw[o++] = (byte)c;
                }
            }

            MemoryStream png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

            byte[] ihdr = new byte[13];
            PutBE(ihdr, 0, (uint)_width);
            PutBE(ihdr, 4, (uint)_height);
            ihdr[8] = 8;        // bit depth
            ihdr[9] = 2;        // RGB
            ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
            Chunk(png, "IHDR", ihdr);

            // zlib stream = header + deflate + adler32
            MemoryStream z = new MemoryStream();
            z.WriteByte(0x78);
            z.WriteByte(0x9C);
            using (DeflateStream d = new DeflateStream(z, CompressionMode.Compress, true))
            {
                d.Write(raw, 0, raw.Length);
            }
            uint adler = Adler32(raw);
            byte[] a = new byte[4];
            PutBE(a, 0, adler);
            z.Write(a, 0, 4);
            Chunk(png, "IDAT", z.ToArray());

            Chunk(png, "IEND", new byte[0]);
            return png.ToArray();
        }

        static void PutBE(byte[] _b, int _o, uint _v)
        {
            _b[_o] = (byte)(_v >> 24);
            _b[_o + 1] = (byte)(_v >> 16);
            _b[_o + 2] = (byte)(_v >> 8);
            _b[_o + 3] = (byte)_v;
        }

        static void Chunk(Stream _s, string _type, byte[] _data)
        {
            byte[] len = new byte[4];
            PutBE(len, 0, (uint)_data.Length);
            _s.Write(len, 0, 4);
            byte[] type = System.Text.Encoding.ASCII.GetBytes(_type);
            _s.Write(type, 0, 4);
            _s.Write(_data, 0, _data.Length);
            uint crc = 0xffffffff;
            crc = Crc(crc, type);
            crc = Crc(crc, _data);
            byte[] c = new byte[4];
            PutBE(c, 0, crc ^ 0xffffffff);
            _s.Write(c, 0, 4);
        }

        static uint Crc(uint _crc, byte[] _data)
        {
            if (CrcTable == null)
            {
                uint[] t = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
                    t[n] = c;
                }
                CrcTable = t;
            }
            foreach (byte b in _data) _crc = CrcTable[(_crc ^ b) & 0xff] ^ (_crc >> 8);
            return _crc;
        }

        static uint Adler32(byte[] _data)
        {
            uint a = 1, b = 0;
            foreach (byte v in _data)
            {
                a = (a + v) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }
    }
}

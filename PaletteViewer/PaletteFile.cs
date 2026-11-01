// ****************************************************************************************
//  Palette Viewer - palette file loading and saving
//
//  Formats (all 256 colours, no header):
//      9-bit  512 bytes   2 bytes per colour: RRRGGGBB, 0000000B   (the usual Next .nxp / .npl format)
//      8-bit  256 bytes   1 byte per colour:  RRRGGGBB              (blue LSB = B1 | B0, as the Next does)
//      24-bit 768 bytes   3 bytes per colour: R, G, B (0-255)       (raw RGB .pal, quantised to 3 bits)
//
//  Loading picks the format from the file size.
// ****************************************************************************************
using System;
using System.IO;

namespace PaletteViewer
{
    internal enum ePaletteFormat
    {
        Next9Bit = 0,
        Next8Bit = 1,
        RGB24 = 2,
    }

    internal static class PaletteFile
    {
        public static readonly string[] FormatNames =
        {
            "9-bit Next palette (512 bytes)",
            "8-bit Next palette (256 bytes)",
            "24-bit RGB palette (768 bytes)",
        };

        public static readonly string[] FormatExtensions = { ".nxp", ".pal", ".pal" };

        /// <summary>Extensions shown by the file browser when "All files" is off</summary>
        public static readonly string[] KnownExtensions = { ".nxp", ".npl", ".pal", ".bin" };


        // ------------------------------------------------------------------------------------
        /// <summary>
        ///     Load a palette file and return 256 9-bit RRRGGGBBB values
        /// </summary>
        /// <exception cref="InvalidDataException">The file isn't a size we recognise</exception>
        // ------------------------------------------------------------------------------------
        public static int[] Load(string _path, out ePaletteFormat _format)
        {
            byte[] data = File.ReadAllBytes(_path);
            int[] colours = new int[256];

            if (data.Length == 512)
            {
                _format = ePaletteFormat.Next9Bit;
                for (int i = 0; i < 256; i++)
                {
                    colours[i] = (data[i * 2] << 1) | (data[(i * 2) + 1] & 1);
                }
            }
            else if (data.Length == 256)
            {
                _format = ePaletteFormat.Next8Bit;
                for (int i = 0; i < 256; i++)
                {
                    colours[i] = From8Bit(data[i]);
                }
            }
            else if (data.Length == 768)
            {
                _format = ePaletteFormat.RGB24;
                for (int i = 0; i < 256; i++)
                {
                    colours[i] = FromRGB(data[i * 3], data[(i * 3) + 1], data[(i * 3) + 2]);
                }
            }
            else
            {
                throw new InvalidDataException(string.Format(
                    "{0} is {1} bytes. Expected 512 (9-bit), 256 (8-bit) or 768 (24-bit RGB).",
                    Path.GetFileName(_path), data.Length));
            }
            return colours;
        }

        // ------------------------------------------------------------------------------------
        /// <summary>
        ///     Save 256 9-bit RRRGGGBBB colours
        /// </summary>
        // ------------------------------------------------------------------------------------
        public static void Save(string _path, uint[] _colours, int _offset, ePaletteFormat _format)
        {
            byte[] data;
            switch (_format)
            {
                case ePaletteFormat.Next8Bit:
                    data = new byte[256];
                    for (int i = 0; i < 256; i++)
                    {
                        data[i] = (byte)((_colours[_offset + i] >> 1) & 0xff);
                    }
                    break;

                case ePaletteFormat.RGB24:
                    data = new byte[768];
                    for (int i = 0; i < 256; i++)
                    {
                        uint v = _colours[_offset + i];
                        data[(i * 3) + 0] = (byte)Expand3((int)(v >> 6));
                        data[(i * 3) + 1] = (byte)Expand3((int)(v >> 3));
                        data[(i * 3) + 2] = (byte)Expand3((int)v);
                    }
                    break;

                default:
                    data = new byte[512];
                    for (int i = 0; i < 256; i++)
                    {
                        uint v = _colours[_offset + i];
                        data[i * 2] = (byte)((v >> 1) & 0xff);
                        data[(i * 2) + 1] = (byte)(v & 1);
                    }
                    break;
            }
            File.WriteAllBytes(_path, data);
        }

        // ------------------------------------------------------------------------------------
        private static int From8Bit(int _b)
        {
            // RRRGGGBB -> RRRGGGBBB, the 3rd blue bit is B1 | B0
            return (_b << 1) | (((_b >> 1) | _b) & 1);
        }

        private static int FromRGB(int _r, int _g, int _b)
        {
            int r = ((_r * 7) + 127) / 255;
            int g = ((_g * 7) + 127) / 255;
            int b = ((_b * 7) + 127) / 255;
            return (r << 6) | (g << 3) | b;
        }

        private static int Expand3(int _c)
        {
            _c &= 7;
            return (_c << 5) | (_c << 2) | (_c >> 1);
        }
    }
}

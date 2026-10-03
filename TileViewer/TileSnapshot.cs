using System;
using System.Collections.Generic;
using System.Linq;
using Plugin;

namespace TileViewer
{
    // A frame owns its arrays; the emulator thread never modifies a published frame.
    internal sealed class TileSnapshot
    {
        private static readonly int[] ColourLevels = { 0, 36, 73, 109, 146, 182, 219, 255 };
        internal byte Control, DefaultAttribute, MapBase, DefinitionsBase;
        internal byte TransparentIndex, TransparentColour;
        internal int ScrollX, ScrollY;
        internal byte[] Map, Definitions, Bank5Definitions, Bank7Definitions;
        internal uint[] Palette, AlternatePalette;

        internal bool Enabled { get { return (Control & 0x80) != 0; } }
        internal bool TextMode { get { return (Control & 8) != 0; } }
        internal bool HasAttributes { get { return (Control & 0x20) == 0; } }
        internal int Columns { get { return (Control & 0x40) == 0 ? 40 : 80; } }
        internal int TileCount { get { return (Control & 2) == 0 ? 256 : 512; } }
        internal int BytesPerTile { get { return TextMode ? 8 : 32; } }
        internal int PaletteNumber { get { return (Control & 0x10) == 0 ? 3 : 7; } }

        internal static TileSnapshot Capture(iCSpect cspect)
        {
            var frame = new TileSnapshot
            {
                Control = cspect.GetNextRegister(0x6b),
                DefaultAttribute = cspect.GetNextRegister(0x6c),
                MapBase = cspect.GetNextRegister(0x6e),
                DefinitionsBase = cspect.GetNextRegister(0x6f),
                TransparentIndex = (byte)(cspect.GetNextRegister(0x4c) & 15),
                TransparentColour = cspect.GetNextRegister(0x14),
                ScrollX = ((cspect.GetNextRegister(0x2f) & 3) << 8) | cspect.GetNextRegister(0x30),
                ScrollY = cspect.GetNextRegister(0x31),
                Palette = new uint[256],
                AlternatePalette = new uint[256]
            };

            // Tile hardware sees the ULA BRAM overlay, independently of CPU MMU mappings.
            byte[] bank5 = cspect.PeekPhysicalULA(5 * 16384, 16384);
            byte[] bank7 = cspect.PeekPhysicalULA(7 * 16384, 8192);
            frame.Map = ReadWrapped(frame.MapBase, frame.Columns * 32 * (frame.HasAttributes ? 2 : 1), bank5, bank7);
            // Capture all patterns so the viewer can show 512 even in 256-tile mode.
            frame.Bank5Definitions = ReadWrapped((byte)(frame.DefinitionsBase & 0x3f), 512 * frame.BytesPerTile, bank5, bank7);
            frame.Bank7Definitions = ReadWrapped((byte)((frame.DefinitionsBase & 0x3f) | 0x80), 512 * frame.BytesPerTile, bank5, bank7);
            frame.Definitions = (frame.DefinitionsBase & 0x80) == 0 ? frame.Bank5Definitions : frame.Bank7Definitions;
            for (int i = 0; i < 256; i++)
            {
                frame.Palette[i] = cspect.GetColour(frame.PaletteNumber, i) & 0x1ff;
                frame.AlternatePalette[i] = cspect.GetColour(frame.PaletteNumber == 3 ? 7 : 3, i) & 0x1ff;
            }
            return frame;
        }

        internal static byte[] ReadWrapped(byte baseRegister, int count, byte[] bank5, byte[] bank7)
        {
            bool useBank7 = (baseRegister & 0x80) != 0;
            byte[] bank = useBank7 ? bank7 : bank5;
            int mask = useBank7 ? 0x1fff : 0x3fff;
            int start = (baseRegister & 0x3f) << 8;
            var result = new byte[count];
            for (int i = 0; i < count; i++) result[i] = bank[(start + i) & mask];
            return result;
        }

        // Display conventional Spectrum addresses: bank 5 at $4000 and bank 7 at
        // $C000. Hardware reads still use the physical ULA overlay, regardless of MMU.
        internal int BankAddress(byte baseRegister, int offset)
        {
            bool bank7 = (baseRegister & 0x80) != 0;
            return (bank7 ? 0xc000 : 0x4000) + ((((baseRegister & 0x3f) << 8) + offset) & (bank7 ? 0x1fff : 0x3fff));
        }

        internal bool SameAs(TileSnapshot other)
        {
            return other != null && Control == other.Control && DefaultAttribute == other.DefaultAttribute &&
                MapBase == other.MapBase && DefinitionsBase == other.DefinitionsBase &&
                TransparentIndex == other.TransparentIndex && TransparentColour == other.TransparentColour &&
                ScrollX == other.ScrollX && ScrollY == other.ScrollY &&
                Map.SequenceEqual(other.Map) && Bank5Definitions.SequenceEqual(other.Bank5Definitions) &&
                Bank7Definitions.SequenceEqual(other.Bank7Definitions) &&
                Palette.SequenceEqual(other.Palette) && AlternatePalette.SequenceEqual(other.AlternatePalette);
        }

        internal TileEntry EntryAt(int cell)
        {
            int offset = cell * (HasAttributes ? 2 : 1);
            byte attribute = HasAttributes ? Map[offset + 1] : DefaultAttribute;
            int tile = Map[offset] | ((Control & 2) != 0 ? (attribute & 1) << 8 : 0);
            return new TileEntry(tile, attribute, TextMode ? (attribute & 0xfe) : (attribute & 0xf0));
        }

        // Include every colour variant used by the map, but display the original tile shape.
        internal List<TileUsage> UsedTiles()
        {
            var used = new SortedDictionary<int, TileUsage>();
            if (Enabled)
            {
                for (int cell = 0; cell < Columns * 32; cell++)
                {
                    TileEntry entry = EntryAt(cell);
                    int key = entry.Tile * 256 + entry.PaletteOffset;
                    TileUsage usage;
                    if (!used.TryGetValue(key, out usage))
                    {
                        usage = new TileUsage { Entry = entry, FirstCell = cell };
                        used.Add(key, usage);
                    }
                    usage.Count++;
                }
            }
            return used.Values.ToList();
        }

        internal int Pixel(TileEntry entry, int x, int y, bool transform, uint[] colours = null, byte[] definitions = null)
        {
            colours = colours ?? Palette;
            definitions = definitions ?? Definitions;
            if (TextMode)
            {
                int bit = (definitions[entry.Tile * 8 + y] >> (7 - x)) & 1;
                uint colour = colours[entry.PaletteOffset | bit];
                return (colour >> 1) == TransparentColour ? 0 : Argb(colour);
            }
            if (transform)
            {
                bool rotate = (entry.Attribute & 2) != 0;
                if (((entry.Attribute & 8) != 0) ^ rotate) x = 7 - x;
                if ((entry.Attribute & 4) != 0) y = 7 - y;
                if (rotate) { int swap = x; x = y; y = swap; }
            }
            byte packed = definitions[entry.Tile * 32 + y * 4 + x / 2];
            int index = (x & 1) == 0 ? packed >> 4 : packed & 15;
            return index == TransparentIndex ? 0 : Argb(colours[entry.PaletteOffset | index]);
        }

        internal int[] TilePixels(TileEntry entry, uint[] colours = null, byte[] definitions = null)
        {
            var pixels = new int[64];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++) pixels[y * 8 + x] = Pixel(entry, x, y, false, colours, definitions);
            return pixels;
        }

        internal int[] MapPixels()
        {
            int width = Columns * 8;
            var pixels = new int[width * 256];
            if (!Enabled) return pixels;
            for (int cell = 0; cell < Columns * 32; cell++)
            {
                TileEntry entry = EntryAt(cell);
                int origin = (cell / Columns) * 8 * width + (cell % Columns) * 8;
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++) pixels[origin + y * width + x] = Pixel(entry, x, y, true);
            }
            return pixels;
        }

        internal static int Argb(uint colour)
        {
            return unchecked((int)0xff000000) | (ColourLevels[(colour >> 6) & 7] << 16) |
                (ColourLevels[(colour >> 3) & 7] << 8) | ColourLevels[colour & 7];
        }
    }

    internal struct TileEntry
    {
        internal readonly int Tile, PaletteOffset;
        internal readonly byte Attribute;
        internal TileEntry(int tile, byte attribute, int paletteOffset)
        { Tile = tile; Attribute = attribute; PaletteOffset = paletteOffset; }
    }

    internal sealed class TileUsage
    {
        internal TileEntry Entry;
        internal int Count, FirstCell;
    }
}

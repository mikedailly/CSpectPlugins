using System;
using System.Collections.Generic;
using System.Linq;

namespace TileViewer
{
    internal enum TileTool { Draw, Clear, Pick }
    internal enum TileMouseButton { None, Left, Right, Middle }

    // Everything the viewer window shows and does, without any UI toolkit: the ImGui view
    // (TileViewerView) draws this and feeds it mouse input, and the tests drive it directly.
    // Main (OS) thread only - the emulator thread talks to it through TileSnapshot/TileMapEditor.
    internal sealed class TileViewerModel
    {
        // Tiles tab: 16 columns of 40 pixel cells, each tile drawn 32x32 (4x) at +4,+4
        internal const int GridColumns = 16, GridCell = 40;
        // Map tab: the whole map drawn at 2x, so 16 pixels per cell
        internal const int MapCell = 16;
        // Textures: all 512 tiles at 8x8 (16 across, 32 down), and the map at 1x (80 columns max)
        internal const int AtlasWidth = GridColumns * 8, AtlasHeight = 32 * 8;
        internal const int MapWidth = 80 * 8, MapHeight = 32 * 8;

        internal const string DefaultDetails = "Hover a tile to see its number, palette, and usage.";
        private const uint CheckerLight = 0xfff5f5f5, CheckerDark = 0xffd3d3d3;   // WhiteSmoke / LightGray

        internal event Action<TileMapEdit> EditRequested;

        internal TileSnapshot Frame { get; private set; }
        internal string Summary = "Waiting for an emulator frame...";
        internal string Details = DefaultDetails;

        // ---- Tiles tab selectors ----
        private int tileCountIndex, paletteIndex, paletteOffsetIndex, bankIndex;
        /// <summary>0 = 256 tiles, 1 = 512</summary>
        internal int TileCountIndex { get { return tileCountIndex; } set { if (tileCountIndex != value) { tileCountIndex = value; RefreshTiles(); } } }
        /// <summary>0 = active, 1 = first tilemap palette, 2 = second</summary>
        internal int PaletteIndex { get { return paletteIndex; } set { if (paletteIndex != value) { paletteIndex = value; RefreshTiles(); } } }
        internal int PaletteOffsetIndex { get { return paletteOffsetIndex; } set { value = Math.Max(0, Math.Min(value, PaletteOffsetCount - 1)); if (paletteOffsetIndex != value) { paletteOffsetIndex = value; RefreshTiles(); } } }
        /// <summary>0 = active, 1 = bank 5, 2 = bank 7</summary>
        internal int BankIndex { get { return bankIndex; } set { if (bankIndex != value) { bankIndex = value; RefreshTiles(); } } }
        /// <summary>16 offsets in graphics mode, 128 ink/paper pairs in text mode</summary>
        internal int PaletteOffsetCount { get; private set; }
        internal int TileCountShown { get { return tileCountIndex == 0 ? 256 : 512; } }

        // ---- images (ARGB; transparent pixels already filled with a checkerboard) ----
        internal readonly uint[] TileAtlas = new uint[AtlasWidth * AtlasHeight];
        internal int TileAtlasVersion { get; private set; }
        internal readonly uint[] MapImage = new uint[MapWidth * MapHeight];
        internal int MapImageVersion { get; private set; }
        private bool mapDirty = true;

        /// <summary>0 = Tiles, 1 = Tilemap</summary>
        internal int SelectedTab { get; private set; }

        // ---- map editor ----
        internal TileTool Tool = TileTool.Draw;
        private int brushTile, brushPalette, clearTile;
        internal int BrushTile { get { return brushTile; } set { brushTile = Clamp(value, TileMinimum, TileMaximum); } }
        internal int BrushPalette { get { return brushPalette; } set { brushPalette = Clamp(value, 0, BrushPaletteMaximum); } }
        internal int ClearTile { get { return clearTile; } set { clearTile = Clamp(value, TileMinimum, TileMaximum); } }
        internal int TileMinimum { get; private set; }
        internal int TileMaximum { get; private set; }
        internal int BrushPaletteMaximum { get; private set; }
        internal bool MirrorX, MirrorY, Rotate, UlaOver;
        internal bool PaletteEnabled { get; private set; }
        internal bool TransformEnabled { get; private set; }
        internal bool UlaOverEnabled { get; private set; }
        internal bool UndoAvailable { get; private set; }
        internal string EditStatus = "Left-drag uses the selected tool. Right-drag clears; middle-click picks.";

        private int[] usageCounts = new int[512];
        private int hoveredTile = -1, hoveredMapCell = -1;
        private bool painting;
        private TileMouseButton paintButton;
        private int lastPaintCell = -1;

        internal TileViewerModel()
        {
            PaletteOffsetCount = 16;
            TileMaximum = 511;
            BrushPaletteMaximum = 15;
            PaletteEnabled = TransformEnabled = UlaOverEnabled = true;
            ClearCanvas(TileAtlas);
            ClearCanvas(MapImage);
        }

        private static int Clamp(int value, int minimum, int maximum) { return Math.Max(minimum, Math.Min(maximum, value)); }

        // ------------------------------------------------------------------------------------
        // Frames and editor state from the plugin
        // ------------------------------------------------------------------------------------
        internal void UpdateEditorState(bool undoAvailable, string status)
        {
            UndoAvailable = undoAvailable;
            if (status != null) EditStatus = status;
        }

        internal void UpdateFrame(TileSnapshot snapshot)
        {
            Frame = snapshot;
            ConfigureEditor();
            List<TileUsage> used = Frame.UsedTiles();
            usageCounts = new int[512];
            foreach (TileUsage usage in used) usageCounts[usage.Entry.Tile] += usage.Count;
            Summary = string.Format("Tilemap {0} | {1}x32 | {2} | {3} tiles | palette {4}\nMap ${5:X4} | tiles ${6:X4} | scroll {7},{8} | {9} tile IDs in use",
                Frame.Enabled ? "enabled" : "disabled", Frame.Columns, Frame.TextMode ? "text (1 bpp)" : "16 colour (4 bpp)",
                Frame.TileCount, Frame.PaletteNumber == 3 ? 1 : 2,
                Frame.BankAddress(Frame.MapBase, 0), Frame.BankAddress(Frame.DefinitionsBase, 0),
                Frame.ScrollX, Frame.ScrollY, used.Select(u => u.Entry.Tile).Distinct().Count());
            int offsetCount = Frame.TextMode ? 128 : 16;
            if (PaletteOffsetCount != offsetCount)
            {
                PaletteOffsetCount = offsetCount;
                paletteOffsetIndex = Math.Min(paletteOffsetIndex, offsetCount - 1);
            }
            RefreshTiles();
            mapDirty = true;
            if (SelectedTab == 1)
            {
                UpdateMap();
                if (hoveredMapCell >= 0 && hoveredMapCell < Frame.Columns * 32) UpdateMapDetails(hoveredMapCell);
            }
        }

        internal void SelectTab(int tab)
        {
            SelectedTab = tab;
            if (Frame != null && tab == 1) UpdateMap();
        }

        private void ConfigureEditor()
        {
            int minimum = 0, maximum = Frame.TileCount - 1;
            if (!Frame.HasAttributes && Frame.TileCount == 512)
            { minimum = (Frame.DefaultAttribute & 1) * 256; maximum = minimum + 255; }
            TileMinimum = minimum;
            TileMaximum = maximum;
            BrushTile = brushTile;
            ClearTile = clearTile;
            BrushPaletteMaximum = Frame.TextMode ? 127 : 15;
            BrushPalette = brushPalette;
            PaletteEnabled = Frame.HasAttributes;
            TransformEnabled = Frame.HasAttributes && !Frame.TextMode;
            UlaOverEnabled = Frame.HasAttributes && Frame.TileCount == 256;
            if (!Frame.HasAttributes)
            {
                BrushPalette = Frame.TextMode ? Frame.DefaultAttribute >> 1 : Frame.DefaultAttribute >> 4;
                MirrorX = (Frame.DefaultAttribute & 8) != 0;
                MirrorY = (Frame.DefaultAttribute & 4) != 0;
                Rotate = (Frame.DefaultAttribute & 2) != 0;
                UlaOver = Frame.TileCount == 256 && (Frame.DefaultAttribute & 1) != 0;
            }
        }

        private byte BrushAttribute()
        {
            int attribute = Frame.TextMode ? BrushPalette << 1 :
                (BrushPalette << 4) | (MirrorX ? 8 : 0) | (MirrorY ? 4 : 0) | (Rotate ? 2 : 0);
            if (Frame.TileCount == 256 && UlaOver) attribute |= 1;
            return (byte)attribute;
        }

        // ------------------------------------------------------------------------------------
        // Tiles tab
        // ------------------------------------------------------------------------------------
        internal int SelectedPaletteNumber { get { return paletteIndex == 0 ? Frame.PaletteNumber : paletteIndex == 1 ? 3 : 7; } }
        internal int SelectedPaletteOffset { get { return paletteOffsetIndex * (Frame.TextMode ? 2 : 16); } }
        private int ActiveBank { get { return (Frame.DefinitionsBase & 0x80) == 0 ? 5 : 7; } }
        internal int SelectedBank { get { return bankIndex == 0 ? ActiveBank : bankIndex == 1 ? 5 : 7; } }

        /// <summary>Content size of the tile grid in pixels</summary>
        internal int GridWidth { get { return GridColumns * GridCell; } }
        internal int GridHeight { get { return TileCountShown / GridColumns * GridCell; } }

        private void RefreshTiles()
        {
            if (Frame == null) return;
            uint[] colours = SelectedPaletteNumber == Frame.PaletteNumber ? Frame.Palette : Frame.AlternatePalette;
            byte[] definitions = SelectedBank == 5 ? Frame.Bank5Definitions : Frame.Bank7Definitions;
            int count = TileCountShown, offset = SelectedPaletteOffset;
            ClearCanvas(TileAtlas);
            for (int tile = 0; tile < count; tile++)
            {
                int[] pixels = Frame.TilePixels(new TileEntry(tile, 0, offset), colours, definitions);
                int ox = (tile % GridColumns) * 8, oy = (tile / GridColumns) * 8;
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        int p = pixels[y * 8 + x];
                        // the checkerboard is 8 screen pixels at 4x zoom = 2 tile pixels
                        TileAtlas[(oy + y) * AtlasWidth + ox + x] = p != 0 ? (uint)p : Checker(x / 2, y / 2);
                    }
            }
            TileAtlasVersion++;
            if (SelectedTab == 0 && hoveredTile >= 0)
            {
                if (hoveredTile < count) UpdateTileDetails(hoveredTile);
                else { hoveredTile = -1; Details = DefaultDetails; }
            }
        }

        /// <summary>A tile preview pixel as displayed (ARGB)</summary>
        internal int TilePixel(int tile, int x, int y)
        {
            return unchecked((int)TileAtlas[((tile / GridColumns) * 8 + y) * AtlasWidth + (tile % GridColumns) * 8 + x]);
        }

        /// <summary>Tile under a point in the grid (content coordinates), or -1</summary>
        internal int TileAt(int x, int y)
        {
            if (x < 0 || y < 0 || x / GridCell >= GridColumns) return -1;
            int index = (y / GridCell) * GridColumns + x / GridCell;
            return index < TileCountShown ? index : -1;
        }

        internal void TilesMouseMove(int x, int y)
        {
            int tile = TileAt(x, y);
            if (tile < 0 || Frame == null) return;
            hoveredTile = tile;
            UpdateTileDetails(tile);
        }

        /// <summary>Left-click a pattern to make it the map brush</summary>
        internal void TilesMouseDown(int x, int y, TileMouseButton button)
        {
            if (button != TileMouseButton.Left || Frame == null) return;
            int tile = TileAt(x, y);
            if (tile < TileMinimum || tile > TileMaximum)
            { EditStatus = "Choose a tile in the active map's tile range."; return; }
            BrushTile = tile;
            if (PaletteEnabled) BrushPalette = paletteOffsetIndex;
            EditStatus = string.Format("Brush: tile {0}, palette {1}.", tile, BrushPalette);
        }

        internal void UpdateTileDetails(int tile)
        {
            byte previewBase = (byte)((Frame.DefinitionsBase & 0x3f) | (SelectedBank == 7 ? 0x80 : 0));
            Details = string.Format("Tile {0} (${0:X3}) | address ${5:X4} | bank {1} | palette {2}, offset ${3:X2} | {4} map cells",
                tile, SelectedBank, SelectedPaletteNumber == 3 ? 1 : 2, SelectedPaletteOffset,
                SelectedBank == ActiveBank ? usageCounts[tile] : 0,
                Frame.BankAddress(previewBase, tile * Frame.BytesPerTile));
        }

        // ------------------------------------------------------------------------------------
        // Tilemap tab
        // ------------------------------------------------------------------------------------
        /// <summary>Content size of the map view in pixels (2x)</summary>
        internal int MapViewWidth { get { return Frame == null ? 0 : Frame.Columns * MapCell; } }
        internal int MapViewHeight { get { return Frame == null ? 0 : 32 * MapCell; } }

        private void UpdateMap()
        {
            if (!mapDirty) return;
            int width = Frame.Columns * 8;
            int[] pixels = Frame.MapPixels();
            ClearCanvas(MapImage);
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < width; x++)
                {
                    int p = pixels[y * width + x];
                    // 8 screen pixel checks at 2x zoom = 4 map pixels
                    MapImage[y * MapWidth + x] = p != 0 ? (uint)p : Checker(x / 4, y / 4);
                }
            mapDirty = false;
            MapImageVersion++;
        }

        /// <summary>A map pixel as displayed at 1x (ARGB)</summary>
        internal int MapPixel(int x, int y) { return unchecked((int)MapImage[y * MapWidth + x]); }

        /// <summary>Map cell under a point in the map view (content coordinates, 2x), or -1</summary>
        internal int MapCellAt(int x, int y)
        {
            if (Frame == null) return -1;
            if (x < 0 || y < 0 || x >= Frame.Columns * MapCell || y >= 32 * MapCell) return -1;
            return y / MapCell * Frame.Columns + x / MapCell;
        }

        internal void MapMouseDown(int x, int y, TileMouseButton button)
        {
            int cell = MapCellAt(x, y);
            if (cell < 0) return;
            ShowMapDetails(cell);
            if ((Tool == TileTool.Pick && button == TileMouseButton.Left) || button == TileMouseButton.Middle)
            {
                TileEntry entry = Frame.EntryAt(cell);
                BrushTile = entry.Tile;
                BrushPalette = entry.PaletteOffset / (Frame.TextMode ? 2 : 16);
                MirrorX = (entry.Attribute & 8) != 0;
                MirrorY = (entry.Attribute & 4) != 0;
                Rotate = (entry.Attribute & 2) != 0;
                UlaOver = Frame.TileCount == 256 && (entry.Attribute & 1) != 0;
                EditStatus = string.Format("Picked tile {0}, palette {1}.", entry.Tile, BrushPalette);
                return;
            }
            if (button != TileMouseButton.Left && button != TileMouseButton.Right) return;
            painting = true; paintButton = button; lastPaintCell = -1;
            PaintMapCell(cell);
        }

        internal void MapMouseMove(int x, int y)
        {
            int cell = MapCellAt(x, y);
            if (cell >= 0) ShowMapDetails(cell);
            if (painting) PaintMapCell(cell);
        }

        internal void MapMouseUp() { painting = false; lastPaintCell = -1; }
        internal bool Painting { get { return painting; } }

        internal void UndoCell()
        {
            if (EditRequested != null) EditRequested(new TileMapEdit { Undo = true });
        }

        private void PaintMapCell(int cell)
        {
            if (cell < 0 || cell == lastPaintCell || EditRequested == null) return;
            bool clear = paintButton == TileMouseButton.Right || Tool == TileTool.Clear;
            lastPaintCell = cell;
            EditRequested(TileMapEdit.Paint(Frame, cell, clear ? ClearTile : BrushTile, BrushAttribute(), clear));
        }

        private void ShowMapDetails(int cell)
        {
            if (Frame == null) return;
            hoveredMapCell = cell;
            UpdateMapDetails(cell);
        }

        private void UpdateMapDetails(int cell)
        {
            int x = cell % Frame.Columns, y = cell / Frame.Columns;
            TileEntry entry = Frame.EntryAt(cell);
            Details = string.Format("Cell {0},{1} | tile {2} (${2:X3}) | attribute ${3:X2} | palette {4}\nFull map before scrolling, clipping, and composition with the other display layers.", x, y, entry.Tile, entry.Attribute,
                entry.PaletteOffset / (Frame.TextMode ? 2 : 16));
        }

        // ------------------------------------------------------------------------------------
        private static uint Checker(int cx, int cy) { return ((cx + cy) & 1) == 0 ? CheckerLight : CheckerDark; }

        private static void ClearCanvas(uint[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = 0xff202020;
        }
    }
}

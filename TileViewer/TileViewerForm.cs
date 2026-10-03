using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TileViewer
{
    internal sealed class TileViewerForm : Form
    {
        private readonly Label summary;
        private readonly Label details;
        private readonly TileCanvas tiles;
        private readonly MapCanvas map;
        private readonly TabControl tabs;
        private readonly ComboBox tileCount, palette, paletteOffset, bank;
        private readonly RadioButton drawTool, clearTool, pickTool;
        private readonly CheckBox mirrorX, mirrorY, rotate, ulaOver;
        private readonly NumericUpDown brushTile, brushPalette, clearTile;
        private readonly Button undoCell;
        private readonly Label editStatus;
        private bool painting;
        private int lastPaintCell = -1;
        internal event Action<TileMapEdit> EditRequested;
        private int[] usageCounts = new int[512];
        private bool updatingControls;
        private int hoveredTile = -1;
        private int hoveredMapCell = -1;
        private TileSnapshot frame;

        internal TileViewerForm()
        {
            Text = "Tile Viewer - Ctrl+Alt+T";
            ClientSize = new Size(680, 820);
            MinimumSize = new Size(680, 400);
            StartPosition = FormStartPosition.CenterParent;
            summary = new Label { Dock = DockStyle.Top, Height = 62, Padding = new Padding(8), Text = "Waiting for an emulator frame..." };
            details = new Label { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8), Text = "Hover a tile to see its number, palette, and usage." };
            tabs = new TabControl { Dock = DockStyle.Fill };
            var tilePage = new TabPage("Tiles");
            var mapPage = new TabPage("Tilemap (unscrolled)");
            tiles = new TileCanvas { Dock = DockStyle.Fill };
            map = new MapCanvas { Dock = DockStyle.Fill };
            tiles.MouseMove += ShowTileDetails;
            tiles.MouseDown += PickTileBrush;
            map.MouseMove += ShowMapDetails;
            map.MouseDown += BeginMapEdit;
            map.MouseMove += ContinueMapEdit;
            map.MouseUp += delegate { painting = false; lastPaintCell = -1; map.Capture = false; };
            tilePage.Controls.Add(tiles);
            var controls = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8), WrapContents = false };
            tileCount = CreateSelector("TileCount", 75, "256", "512");
            palette = CreateSelector("Palette", 95, "Active", "1", "2");
            paletteOffset = CreateSelector("PaletteOffset", 75, Enumerable.Range(0, 16).Select(i => i.ToString()).ToArray());
            bank = CreateSelector("Bank", 75, "Active", "5", "7");
            AddSelector(controls, "Tiles", tileCount);
            AddSelector(controls, "Palette", palette);
            AddSelector(controls, "Palette Offset", paletteOffset);
            AddSelector(controls, "Bank", bank);
            tileCount.SelectedIndexChanged += delegate { RefreshTiles(); };
            palette.SelectedIndexChanged += delegate { RefreshTiles(); };
            paletteOffset.SelectedIndexChanged += delegate { RefreshTiles(); };
            bank.SelectedIndexChanged += delegate { RefreshTiles(); };
            tilePage.Controls.Add(controls);
            mapPage.Controls.Add(map);
            var editControls = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(6), WrapContents = true };
            drawTool = CreateTool("DrawTool", "Draw");
            clearTool = CreateTool("ClearTool", "Clear");
            pickTool = CreateTool("PickTool", "Pick");
            drawTool.Checked = true;
            brushTile = CreateNumber("BrushTile", 511, 62);
            brushPalette = CreateNumber("BrushPalette", 15, 46);
            clearTile = CreateNumber("ClearTile", 511, 62);
            editControls.Controls.AddRange(new Control[] { drawTool, clearTool, pickTool });
            AddNumber(editControls, "Tile", brushTile);
            AddNumber(editControls, "Palette", brushPalette);
            AddNumber(editControls, "Clear tile", clearTile);
            editControls.SetFlowBreak(clearTile, true);
            mirrorX = new CheckBox { Text = "Mirror X", AutoSize = true };
            mirrorY = new CheckBox { Text = "Mirror Y", AutoSize = true };
            rotate = new CheckBox { Text = "Rotate", AutoSize = true };
            ulaOver = new CheckBox { Text = "ULA over", AutoSize = true };
            undoCell = new Button { Name = "UndoCell", Text = "Undo cell", AutoSize = true, Enabled = false };
            editControls.Controls.AddRange(new Control[] { mirrorX, mirrorY, rotate, ulaOver, undoCell });
            editControls.SetFlowBreak(undoCell, true);
            editStatus = new Label { AutoSize = true, Text = "Left-drag uses the selected tool. Right-drag clears; middle-click picks." };
            editControls.Controls.Add(editStatus);
            undoCell.Click += delegate { if (EditRequested != null) EditRequested(new TileMapEdit { Undo = true }); };
            mapPage.Controls.Add(editControls);
            tabs.TabPages.Add(tilePage);
            tabs.TabPages.Add(mapPage);
            tabs.SelectedIndexChanged += delegate { if (frame != null && tabs.SelectedIndex == 1) map.UpdateFrame(frame); };
            Controls.Add(tabs);
            Controls.Add(details);
            Controls.Add(summary);
        }

        private static NumericUpDown CreateNumber(string name, int maximum, int width)
        { return new NumericUpDown { Name = name, Minimum = 0, Maximum = maximum, Width = width }; }

        private static RadioButton CreateTool(string name, string text)
        { return new RadioButton { Name = name, Text = text, Appearance = Appearance.Button, Width = 52, Height = 24, TextAlign = ContentAlignment.MiddleCenter }; }

        private static void AddNumber(FlowLayoutPanel controls, string label, NumericUpDown number)
        {
            controls.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(6, 5, 4, 0) });
            controls.Controls.Add(number);
        }

        internal void UpdateEditorState(bool undoAvailable, string status)
        {
            undoCell.Enabled = undoAvailable;
            if (status != null) editStatus.Text = status;
        }

        private void ConfigureEditor()
        {
            int minimum = 0, maximum = frame.TileCount - 1;
            if (!frame.HasAttributes && frame.TileCount == 512)
            { minimum = (frame.DefaultAttribute & 1) * 256; maximum = minimum + 255; }
            foreach (NumericUpDown number in new[] { brushTile, clearTile })
            {
                number.Minimum = 0;
                number.Maximum = maximum;
                number.Minimum = minimum;
            }
            brushPalette.Maximum = frame.TextMode ? 127 : 15;
            brushPalette.Enabled = frame.HasAttributes;
            mirrorX.Enabled = mirrorY.Enabled = rotate.Enabled = frame.HasAttributes && !frame.TextMode;
            ulaOver.Enabled = frame.HasAttributes && frame.TileCount == 256;
            if (!frame.HasAttributes)
            {
                brushPalette.Value = frame.TextMode ? frame.DefaultAttribute >> 1 : frame.DefaultAttribute >> 4;
                mirrorX.Checked = (frame.DefaultAttribute & 8) != 0;
                mirrorY.Checked = (frame.DefaultAttribute & 4) != 0;
                rotate.Checked = (frame.DefaultAttribute & 2) != 0;
                ulaOver.Checked = frame.TileCount == 256 && (frame.DefaultAttribute & 1) != 0;
            }
        }

        private byte BrushAttribute()
        {
            int attribute = frame.TextMode ? (int)brushPalette.Value << 1 :
                ((int)brushPalette.Value << 4) | (mirrorX.Checked ? 8 : 0) | (mirrorY.Checked ? 4 : 0) | (rotate.Checked ? 2 : 0);
            if (frame.TileCount == 256 && ulaOver.Checked) attribute |= 1;
            return (byte)attribute;
        }

        private void PickTileBrush(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || frame == null) return;
            int tile = tiles.TileAt(e.Location);
            if (tile < brushTile.Minimum || tile > brushTile.Maximum)
            { editStatus.Text = "Choose a tile in the active map's tile range."; return; }
            brushTile.Value = tile;
            if (brushPalette.Enabled) brushPalette.Value = paletteOffset.SelectedIndex;
            editStatus.Text = string.Format("Brush: tile {0}, palette {1}.", tile, brushPalette.Value);
        }

        private int MapCellAt(Point point)
        {
            if (frame == null) return -1;
            int x = point.X - map.AutoScrollPosition.X, y = point.Y - map.AutoScrollPosition.Y;
            if (x < 0 || y < 0 || x >= frame.Columns * 16 || y >= 32 * 16) return -1;
            return y / 16 * frame.Columns + x / 16;
        }

        private void BeginMapEdit(object sender, MouseEventArgs e)
        {
            int cell = MapCellAt(e.Location);
            if (cell < 0) return;
            ShowMapDetails(sender, e);
            if ((pickTool.Checked && e.Button == MouseButtons.Left) || e.Button == MouseButtons.Middle)
            {
                TileEntry entry = frame.EntryAt(cell);
                brushTile.Value = entry.Tile;
                brushPalette.Value = entry.PaletteOffset / (frame.TextMode ? 2 : 16);
                mirrorX.Checked = (entry.Attribute & 8) != 0;
                mirrorY.Checked = (entry.Attribute & 4) != 0;
                rotate.Checked = (entry.Attribute & 2) != 0;
                ulaOver.Checked = frame.TileCount == 256 && (entry.Attribute & 1) != 0;
                editStatus.Text = string.Format("Picked tile {0}, palette {1}.", entry.Tile, brushPalette.Value);
                return;
            }
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            painting = true; lastPaintCell = -1; map.Capture = true;
            PaintMapCell(e);
        }

        private void ContinueMapEdit(object sender, MouseEventArgs e)
        { if (painting) PaintMapCell(e); }

        private void PaintMapCell(MouseEventArgs e)
        {
            int cell = MapCellAt(e.Location);
            if (cell < 0 || cell == lastPaintCell || EditRequested == null) return;
            bool clear = e.Button == MouseButtons.Right || clearTool.Checked;
            lastPaintCell = cell;
            EditRequested(TileMapEdit.Paint(frame, cell, (int)(clear ? clearTile.Value : brushTile.Value), BrushAttribute(), clear));
        }

        private static ComboBox CreateSelector(string name, int width, params string[] items)
        {
            var selector = new ComboBox { Name = name, Width = width, DropDownStyle = ComboBoxStyle.DropDownList };
            selector.Items.AddRange(items);
            selector.SelectedIndex = 0;
            return selector;
        }

        private static void AddSelector(FlowLayoutPanel controls, string label, ComboBox selector)
        {
            controls.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 5, 6, 0) });
            selector.Margin = new Padding(0, 0, 22, 0);
            controls.Controls.Add(selector);
        }

        private int SelectedPaletteNumber { get { return palette.SelectedIndex == 0 ? frame.PaletteNumber : palette.SelectedIndex == 1 ? 3 : 7; } }
        private int SelectedPaletteOffset { get { return paletteOffset.SelectedIndex * (frame.TextMode ? 2 : 16); } }
        private int ActiveBank { get { return (frame.DefinitionsBase & 0x80) == 0 ? 5 : 7; } }
        private int SelectedBank { get { return bank.SelectedIndex == 0 ? ActiveBank : bank.SelectedIndex == 1 ? 5 : 7; } }

        private void RefreshTiles()
        {
            if (frame == null || updatingControls) return;
            uint[] colours = SelectedPaletteNumber == frame.PaletteNumber ? frame.Palette : frame.AlternatePalette;
            byte[] definitions = SelectedBank == 5 ? frame.Bank5Definitions : frame.Bank7Definitions;
            tiles.UpdateFrame(frame, tileCount.SelectedIndex == 0 ? 256 : 512, SelectedPaletteOffset, colours, definitions);
            Point cursor = tiles.PointToClient(Cursor.Position);
            if (tiles.ClientRectangle.Contains(cursor))
                ShowTileDetails(tiles, new MouseEventArgs(MouseButtons.None, 0, cursor.X, cursor.Y, 0));
            else if (tabs.SelectedIndex == 0 && hoveredTile >= 0)
            {
                if (hoveredTile < (tileCount.SelectedIndex == 0 ? 256 : 512)) UpdateTileDetails(hoveredTile);
                else { hoveredTile = -1; details.Text = "Hover a tile to see its number, palette, and usage."; }
            }
        }

        internal void UpdateFrame(TileSnapshot snapshot)
        {
            frame = snapshot;
            ConfigureEditor();
            List<TileUsage> used = frame.UsedTiles();
            usageCounts = new int[512];
            foreach (TileUsage usage in used) usageCounts[usage.Entry.Tile] += usage.Count;
            summary.Text = string.Format("Tilemap {0} | {1}x32 | {2} | {3} tiles | palette {4}\r\nMap ${5:X4} | tiles ${6:X4} | scroll {7},{8} | {9} tile IDs in use",
                frame.Enabled ? "enabled" : "disabled", frame.Columns, frame.TextMode ? "text (1 bpp)" : "16 colour (4 bpp)",
                frame.TileCount, frame.PaletteNumber == 3 ? 1 : 2,
                frame.BankAddress(frame.MapBase, 0), frame.BankAddress(frame.DefinitionsBase, 0),
                frame.ScrollX, frame.ScrollY, used.Select(u => u.Entry.Tile).Distinct().Count());
            int offsetCount = frame.TextMode ? 128 : 16;
            if (paletteOffset.Items.Count != offsetCount)
            {
                updatingControls = true;
                int previous = paletteOffset.SelectedIndex;
                paletteOffset.Items.Clear();
                paletteOffset.Items.AddRange(Enumerable.Range(0, offsetCount).Select(i => i.ToString()).ToArray());
                paletteOffset.SelectedIndex = Math.Min(previous, offsetCount - 1);
                updatingControls = false;
            }
            RefreshTiles();
            map.MarkDirty();
            if (tabs.SelectedIndex == 1)
            {
                map.UpdateFrame(frame);
                if (hoveredMapCell >= 0 && hoveredMapCell < frame.Columns * 32) UpdateMapDetails(hoveredMapCell);
            }
        }

        private void ShowTileDetails(object sender, MouseEventArgs e)
        {
            int tile = tiles.TileAt(e.Location);
            if (tile < 0 || frame == null) return;
            hoveredTile = tile;
            UpdateTileDetails(tile);
        }

        private void UpdateTileDetails(int tile)
        {
            byte previewBase = (byte)((frame.DefinitionsBase & 0x3f) | (SelectedBank == 7 ? 0x80 : 0));
            details.Text = string.Format("Tile {0} (${0:X3}) | address ${5:X4} | bank {1} | palette {2}, offset ${3:X2} | {4} map cells",
                tile, SelectedBank, SelectedPaletteNumber == 3 ? 1 : 2, SelectedPaletteOffset,
                SelectedBank == ActiveBank ? usageCounts[tile] : 0,
                frame.BankAddress(previewBase, tile * frame.BytesPerTile));
        }

        private void ShowMapDetails(object sender, MouseEventArgs e)
        {
            if (frame == null) return;
            int cell = MapCellAt(e.Location);
            if (cell < 0) return;
            hoveredMapCell = cell;
            UpdateMapDetails(cell);
        }

        private void UpdateMapDetails(int cell)
        {
            int x = cell % frame.Columns, y = cell / frame.Columns;
            TileEntry entry = frame.EntryAt(cell);
            details.Text = string.Format("Cell {0},{1} | tile {2} (${2:X3}) | attribute ${3:X2} | palette {4}\r\nFull map before scrolling, clipping, and composition with the other display layers.", x, y, entry.Tile, entry.Attribute,
                entry.PaletteOffset / (frame.TextMode ? 2 : 16));
        }

        internal void PumpMessages()
        {
            if (IsDisposed || !IsHandleCreated) return;
            IntPtr window = Handle;
            NativeMessage native;
            // DoEvents drains the shared OS thread queue, including CSpect keys.
            // Filter by our HWND (and its child controls) so the host retains its input.
            // Peek without removing first: WM_QUIT bypasses HWND filters and belongs to CSpect.
            for (int count = 0; count < 256 && !IsDisposed && PeekMessage(out native, window, 0, 0, 0); count++)
            {
                if (native.Id == 0x12) return;
                if (!PeekMessage(out native, window, 0, 0, 1)) break;
                Message managed = Message.Create(native.Window, (int)native.Id, native.WParam, native.LParam);
                Control target = Control.FromChildHandle(native.Window);
                bool handled = native.Id >= 0x100 && native.Id <= 0x109 && target != null &&
                    target.PreProcessControlMessage(ref managed) == PreProcessControlState.MessageProcessed;
                if (!handled)
                {
                    TranslateMessage(ref native);
                    DispatchMessage(ref native);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            internal IntPtr Window;
            internal uint Id;
            internal IntPtr WParam, LParam;
            internal uint Time;
            internal Point Position;
            internal uint Private;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint minimum, uint maximum, uint remove);
        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref NativeMessage message);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DispatchMessage(ref NativeMessage message);

        private class ViewerCanvas : Panel
        {
            internal ViewerCanvas()
            {
                DoubleBuffered = true;
                AutoScroll = true;
                BackColor = Color.White;
                ResizeRedraw = true;
            }

            protected static Bitmap CreateBitmap(int width, int height, int[] pixels)
            {
                var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
                finally { bitmap.UnlockBits(data); }
                return bitmap;
            }

            protected static void Checkerboard(Graphics graphics, Rectangle area)
            {
                graphics.FillRectangle(Brushes.LightGray, area);
                for (int y = 0; y < area.Height; y += 8)
                    for (int x = 0; x < area.Width; x += 8)
                        if (((x / 8 + y / 8) & 1) == 0)
                            graphics.FillRectangle(Brushes.WhiteSmoke, area.X + x, area.Y + y, Math.Min(8, area.Width - x), Math.Min(8, area.Height - y));
            }

            protected static void PixelDrawing(Graphics graphics)
            {
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
            }
        }

        private sealed class TileCanvas : ViewerCanvas
        {
            private const int Columns = 16, CellSize = 40;
            private readonly List<Bitmap> images = new List<Bitmap>();

            internal void UpdateFrame(TileSnapshot snapshot, int count, int offset, uint[] colours, byte[] definitions)
            {
                ClearImages();
                for (int tile = 0; tile < count; tile++)
                    images.Add(CreateBitmap(8, 8, snapshot.TilePixels(new TileEntry(tile, 0, offset), colours, definitions)));
                AutoScrollMinSize = new Size(Columns * CellSize, count / Columns * CellSize);
                Invalidate();
            }

            internal int TileAt(Point point)
            {
                int x = point.X - AutoScrollPosition.X, y = point.Y - AutoScrollPosition.Y;
                if (x < 0 || y < 0 || x / CellSize >= Columns) return -1;
                int index = (y / CellSize) * Columns + x / CellSize;
                return index < images.Count ? index : -1;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                PixelDrawing(e.Graphics);
                for (int i = 0; i < images.Count; i++)
                {
                    int x = (i % Columns) * CellSize + AutoScrollPosition.X;
                    int y = (i / Columns) * CellSize + AutoScrollPosition.Y;
                    if (y + CellSize < e.ClipRectangle.Top || y > e.ClipRectangle.Bottom) continue;
                    var area = new Rectangle(x + 4, y + 4, 32, 32);
                    Checkerboard(e.Graphics, area);
                    e.Graphics.DrawImage(images[i], area);
                }
            }

            private void ClearImages() { foreach (Bitmap image in images) image.Dispose(); images.Clear(); }
            protected override void Dispose(bool disposing) { if (disposing) ClearImages(); base.Dispose(disposing); }
        }

        private sealed class MapCanvas : ViewerCanvas
        {
            private Bitmap image;
            private bool dirty = true;
            internal void MarkDirty() { dirty = true; }

            internal void UpdateFrame(TileSnapshot snapshot)
            {
                if (!dirty) return;
                if (image != null) image.Dispose();
                image = CreateBitmap(snapshot.Columns * 8, 256, snapshot.MapPixels());
                AutoScrollMinSize = new Size(image.Width * 2, image.Height * 2);
                dirty = false;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (image == null) return;
                Checkerboard(e.Graphics, e.ClipRectangle);
                PixelDrawing(e.Graphics);
                e.Graphics.DrawImage(image, AutoScrollPosition.X, AutoScrollPosition.Y, image.Width * 2, image.Height * 2);
            }

            protected override void Dispose(bool disposing) { if (disposing && image != null) image.Dispose(); base.Dispose(disposing); }
        }
    }
}

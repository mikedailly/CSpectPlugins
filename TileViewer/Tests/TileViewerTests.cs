using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using Plugin;

namespace TileViewer
{
    internal static class TileViewerTests
    {
        private static int checks;
        private static void Check(bool condition, string name)
        { if (!condition) throw new Exception(name); checks++; }

        private static int Main(string[] args)
        {
            try
            {
                TestMemoryAndCapture();
                TestBankAddresses();
                TestPixelsAndUsage();
                TestTextAndChanges();
                TestMapEditing();
                TestEditingWindow();
                TestWindow();
                Console.WriteLine("PASS: " + checks + " checks (capture, banking, decoding, editing, live updates, and window lifecycle).");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static void TestMemoryAndCapture()
        {
            var fake = new Emulator();
            fake.Registers[0x6b] = 0xc2; // 80 columns, 512 patterns, attributes.
            fake.Registers[0x6e] = 0x3f;
            fake.Registers[0x6f] = 0xbf; // Bank 7 offset wraps at 8K.
            fake.Bank5[0x3f00] = 37; fake.Bank5[0] = 99;
            fake.Bank7[0x1f00] = 0x12; fake.Bank7[0] = 0xab;
            fake.Registers[0x2f] = 2; fake.Registers[0x30] = 0x56; fake.Registers[0x31] = 231;
            TileSnapshot frame = TileSnapshot.Capture(fake.Api);
            Check(frame.Columns == 80 && frame.TileCount == 512, "80-column/512 control");
            Check(frame.Map.Length == 5120 && frame.Definitions.Length == 16384, "capture sizes");
            Check(frame.Map[0] == 37 && frame.Map[256] == 99, "map bank 5 wrapping");
            Check(frame.Definitions[0] == 0x12 && frame.Definitions[256] == 0xab && frame.Definitions[8192] == 0x12, "bank 7 wraps instead of reading its upper half");
            Check(frame.BankAddress(0xbf, 256) == 0xc000, "bank 7 display address wraps at 8 KB");
            Check(frame.ScrollX == 598 && frame.ScrollY == 231, "scroll registers");
            fake.Registers[0x6b] = 0xb2; // Palette 2, default attribute, 512.
            fake.Registers[0x6c] = 0xa1;
            fake.Registers[0x6e] = 0x80; fake.Registers[0x6f] = 0;
            fake.Bank7[0] = 5; fake.Bank5[0] = 0xde;
            frame = TileSnapshot.Capture(fake.Api);
            Check(frame.Map.Length == 1280 && frame.Definitions[0] == 0xde && frame.EntryAt(0).Tile == 261, "independent banks and default ninth tile bit");
            Check(frame.EntryAt(0).PaletteOffset == 0xa0 && frame.PaletteNumber == 7, "secondary palette from control, not palette-write selector");
            Check(fake.MemoryReads == 4, "capture reads ULA overlay only, once per required bank");
        }

        private static void TestBankAddresses()
        {
            var fake = new Emulator();
            fake.Registers[0x6b] = 0x82;
            fake.Registers[0x6e] = 0; // MapBase = $4000.
            fake.Registers[0x6f] = 0x0a; // (TileBase - $4000) / 256 = $0A.
            fake.Bank5[0x0a00] = 0x12;
            TileSnapshot frame = TileSnapshot.Capture(fake.Api);
            Check(frame.BankAddress(frame.MapBase, 0) == 0x4000, "user map base displays $4000");
            Check(frame.BankAddress(frame.DefinitionsBase, 0) == 0x4a00, "user tile base displays $4A00");
            Check(frame.Definitions[0] == 0x12, "display-address change preserves physical ULA reads");
            Check(frame.BankAddress(frame.DefinitionsBase, 341 * 32) == 0x74a0, "tile 341 displays $74A0 instead of $174A0");
            Check(frame.BankAddress(frame.DefinitionsBase, 431 * 32) == 0x7fe0 && frame.BankAddress(frame.DefinitionsBase, 432 * 32) == 0x4000, "bank 5 tile addresses wrap at $8000");
            var view = new TileViewerModel();
            view.UpdateFrame(frame);
            Check(view.Summary.Contains("Map $4000 | tiles $4A00"), "header displays user's map and tile bases");
            view.TileCountIndex = 1;
            view.UpdateTileDetails(341);
            Check(view.Details.Contains("Tile 341") && view.Details.Contains("address $74A0"), "hover footer uses user's Spectrum address");
            fake.Registers[0x6e] = 0x12;
            fake.Registers[0x6f] = 0x94;
            fake.Bank7[0x1400] = 0x56;
            TileSnapshot relocated = TileSnapshot.Capture(fake.Api);
            Check(!frame.SameAs(relocated) && relocated.Definitions[0] == 0x56, "changing NextReg $6F changes the bank and pattern read location");
            view.UpdateFrame(relocated);
            Check(view.Summary.Contains("Map $5200 | tiles $D400"), "header follows live NextReg $6E and $6F values");
            view.UpdateTileDetails(0);
            Check(view.Details.Contains("address $D400") && view.Details.Contains("bank 7"), "tile hover follows register-selected bank and base");
        }

        private static TileSnapshot Frame()
        {
            var frame = new TileSnapshot
            {
                Control = 0x80, Map = new byte[2560], Definitions = new byte[8192],
                Palette = new uint[256], TransparentIndex = 15, TransparentColour = 0xe3
            };
            for (int i = 0; i < 256; i++) frame.Palette[i] = (uint)i;
            return frame;
        }

        private static void TestPixelsAndUsage()
        {
            var frame = Frame();
            frame.Definitions[0] = 0x12;
            var entry = new TileEntry(0, 0xa0, 0xa0);
            Check(frame.Pixel(entry, 0, 0, false) == TileSnapshot.Argb(0xa1) && frame.Pixel(entry, 1, 0, false) == TileSnapshot.Argb(0xa2), "packed nibble order and palette offset");
            frame.TransparentIndex = 2;
            Check(frame.Pixel(entry, 1, 0, false) == 0, "four-bit transparency is before palette lookup");
            Check(TileSnapshot.Argb(0x1ff) == Color.White.ToArgb() && TileSnapshot.Argb(0x1c0) == Color.Red.ToArgb(), "9-bit RGB conversion");

            // Each corner has a unique colour. Expected top-left source corners for
            // attribute flags 0..7 (rotate, Y mirror, X mirror) come from hardware.
            frame.Definitions[0] = 0x10; frame.Definitions[3] = 0x02;
            frame.Definitions[28] = 0x30; frame.Definitions[31] = 0x04;
            frame.TransparentIndex = 15;
            int[] corners = { 1, 3, 3, 4, 2, 1, 4, 2 };
            for (int flags = 0; flags < 8; flags++)
                Check(frame.Pixel(new TileEntry(0, (byte)(flags << 1), 0), 0, 0, true) == TileSnapshot.Argb((uint)corners[flags]), "mirror/rotate combination " + flags);

            frame.Map[0] = 1; frame.Map[1] = 0x20;
            frame.Map[2] = 1; frame.Map[3] = 0x2e;
            frame.Map[4] = 1; frame.Map[5] = 0x30;
            List<TileUsage> used = frame.UsedTiles();
            Check(used.Count == 3 && used[1].Entry.Tile == 1 && used[1].Count == 2, "usage groups by tile and palette, not transform");
            Check(used.Sum(u => u.Count) == 1280, "usage counts cover full map");
            frame.Control = 0x82; frame.Map[1] = 0x21;
            Check(frame.EntryAt(0).Tile == 257, "per-cell ninth tile bit");
            frame.Control = 0x80;
            frame.Map[0] = 0; frame.Map[1] = 2;
            Check(frame.MapPixels()[0] == TileSnapshot.Argb(3), "tilemap rendering applies clockwise rotation");
            frame.Control = 0;
            Check(frame.UsedTiles().Count == 0 && frame.MapPixels().All(p => p == 0), "disabled map reports no active tiles");
        }

        private static void TestTextAndChanges()
        {
            var fake = new Emulator();
            fake.Registers[0x6b] = 0xa8;
            fake.Registers[0x6c] = 0xae;
            fake.Registers[0x6e] = 0x20;
            fake.Bank5[0] = 0x80;
            TileSnapshot frame = TileSnapshot.Capture(fake.Api);
            TileEntry entry = frame.EntryAt(0);
            Check(frame.Definitions.Length == 4096 && entry.PaletteOffset == 0xae, "all 512 text patterns captured with seven-bit palette offset");
            Check(frame.Pixel(entry, 0, 0, true) == TileSnapshot.Argb(0xaf) && frame.Pixel(entry, 1, 0, true) == TileSnapshot.Argb(0xae), "text bit order and attribute bits do not transform text");
            fake.Palette[0xaf] = 0x1c6; fake.Registers[0x14] = 0xe3;
            frame = TileSnapshot.Capture(fake.Api);
            Check(frame.Pixel(frame.EntryAt(0), 0, 0, false) == 0, "text transparency compares high eight bits of final colour");
            Check(frame.SameAs(TileSnapshot.Capture(fake.Api)), "identical frame equality");
            fake.Palette[0xae] = 511;
            Check(!frame.SameAs(TileSnapshot.Capture(fake.Api)), "palette-only changes refresh");
            frame = TileSnapshot.Capture(fake.Api); fake.Bank5[0] ^= 1;
            Check(!frame.SameAs(TileSnapshot.Capture(fake.Api)), "pattern-only changes refresh");
            frame = TileSnapshot.Capture(fake.Api); fake.Bank5[0x2000] = 1;
            Check(!frame.SameAs(TileSnapshot.Capture(fake.Api)), "map-only changes refresh");
            frame = TileSnapshot.Capture(fake.Api); fake.Registers[0x6c] ^= 2;
            Check(!frame.SameAs(TileSnapshot.Capture(fake.Api)), "default attribute changes refresh");
            frame = TileSnapshot.Capture(fake.Api); fake.Palette2[7] = 511;
            Check(!frame.SameAs(TileSnapshot.Capture(fake.Api)), "inactive palette edits refresh selected palette preview");
            fake.Registers[0x6b] = 0x80;
            frame = TileSnapshot.Capture(fake.Api);
            Check(frame.TileCount == 256 && frame.Definitions.Length == 16384, "512 graphics patterns captured even when hardware is in 256 mode");
            fake.Bank5[511 * 32] = 0x12;
            frame = TileSnapshot.Capture(fake.Api);
            Check(frame.TilePixels(new TileEntry(511, 0, 0))[0] == TileSnapshot.Argb(fake.Palette[1]), "tile 511 can be previewed in hardware 256 mode");
            fake.Bank7[0] = 0x23;
            Check(!frame.SameAs(TileSnapshot.Capture(fake.Api)), "inactive bank edits trigger refresh");
            frame = TileSnapshot.Capture(fake.Api);
            Check(frame.TilePixels(new TileEntry(0, 0, 0), null, frame.Bank7Definitions)[0] == TileSnapshot.Argb(fake.Palette[2]), "bank 7 can be previewed independently of active bank 5");
        }

        private static void TestWindow()
        {
            var fake = new Emulator();
            fake.Registers[0x6b] = 0x80; fake.Registers[0x6e] = 0x20; fake.Registers[0x4c] = 0;
            for (int cell = 0; cell < 1280; cell++) { fake.Bank5[0x2000 + cell * 2] = (byte)(cell % 12); fake.Bank5[0x2001 + cell * 2] = (byte)((cell % 4) << 4); }
            for (int tile = 0; tile < 12; tile++)
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 4; x++) fake.Bank5[tile * 32 + y * 4 + x] = (byte)((tile + y + x) % 15 + 1 + (((tile + y) % 15 + 1) << 4));
            for (int i = 0; i < 256; i++) fake.Palette[i] = (uint)((i * 13) & 511);
            fake.Bank7[0] = 0x22; fake.Palette2[18] = 7; fake.Palette2[19] = 511;
            var plugin = new TileViewerPlugin();
            List<sIO> hooks = plugin.Init(fake.Api);
            Check(hooks.Count == 1 && hooks[0].CMD == "<ctrl><alt>t" && hooks[0].Type == eAccess.KeyPress && plugin.KeyPressed(0) && !plugin.KeyPressed(1), "Ctrl+Alt+T registered and handled only for registered ID");
            plugin.OSTick(); plugin.Tick(); plugin.OSTick();
            Check(fake.OpenWindows == 1, "viewer opens its own window");
            FakeWindow window = fake.Windows.Last();
            Check(window.Title.Contains("Tile Viewer"), "viewer title");
            Check(window.Draw != null, "window is drawn with Dear ImGui");
            TileViewerModel view = plugin.Model;
            Check(view.TileCountShown == 256 && view.GridWidth == 640 && view.GridHeight == 640, "default grid displays all 256 patterns in 16 columns");
            Check(view.TileAt(604, 4) == 15 && view.TileAt(4, 44) == 16, "grid wraps after sixteen tiles");
            view.PaletteOffsetIndex = 1;
            Check(view.TilePixel(0, 0, 0) == TileSnapshot.Argb(fake.Palette[17]), "palette offset selector redraws tile previews");
            fake.Palette2[17] = 0x1c0; plugin.Tick(); plugin.OSTick();
            view.PaletteIndex = 2;
            Check(view.TilePixel(0, 0, 0) == Color.Red.ToArgb() && fake.Registers[0x6b] == 0x80, "secondary palette preview does not change emulator palette");
            view.BankIndex = 2;
            Check(view.TilePixel(0, 0, 0) == Color.Blue.ToArgb() && fake.Registers[0x6f] == 0, "bank 7 selector previews alternate bank without changing emulator");
            fake.Bank7[0] = 0x33; plugin.Tick(); plugin.OSTick();
            Check(view.TilePixel(0, 0, 0) == Color.White.ToArgb(), "selected inactive bank updates live");
            view.BankIndex = 0;
            Check(view.TilePixel(0, 0, 0) == Color.Red.ToArgb(), "active bank selector follows bank 5");
            fake.Registers[0x6f] = 0x80; plugin.Tick(); plugin.OSTick();
            Check(view.TilePixel(0, 0, 0) == Color.White.ToArgb(), "active bank selector follows hardware bank switches");
            view.TilesMouseMove(4, 4);
            Check(view.Details.Contains("bank 7") && view.Details.Contains("address $C000") && !view.Details.Contains("pattern"), "hover footer reports bank 7 Spectrum address");
            fake.Registers[0x6f] = 0; plugin.Tick(); plugin.OSTick();
            view.TileCountIndex = 1;
            Check(view.TileCountShown == 512 && view.GridHeight == 1280, "512 selection extends to thirty-two rows");
            fake.Registers[0x6b] = 0; plugin.Tick(); plugin.OSTick();
            Check(view.TileCountShown == 512, "all tiles remain visible while map is disabled");
            fake.Registers[0x6b] = 0x88; plugin.Tick(); plugin.OSTick();
            Check(view.PaletteOffsetCount == 128, "text mode offers all ink/paper palette pairs");
            fake.Registers[0x6b] = 0x80; plugin.Tick(); plugin.OSTick();
            Check(view.PaletteOffsetCount == 16, "graphics mode restores sixteen palette offsets");
            view.TileCountIndex = 0; view.PaletteIndex = 0; view.PaletteOffsetIndex = 0;
            int atlasVersion = view.TileAtlasVersion;
            fake.Bank5[0] ^= 0x11; plugin.Tick(); plugin.OSTick();
            Check(view.TileAtlasVersion != atlasVersion, "pattern changes mark the tile texture for upload");
            plugin.KeyPressed(0); plugin.OSTick();
            Check(fake.OpenWindows == 1 && !window.Closed, "repeat hotkey reuses window");
            view.SelectTab(1);
            int mapVersion = view.MapImageVersion;
            fake.Bank5[0x2001] = 0x40; plugin.Tick(); plugin.OSTick();
            Check(view.MapImageVersion != mapVersion, "map changes mark the map texture for upload");
            view.MapMouseMove(4, 4);
            Check(view.Details.Contains("palette 4") && !view.Details.Contains("palette offset"), "map hover shows decimal palette number 0..15");
            TileSnapshot before = view.Frame;
            fake.Palette[1] ^= 511; plugin.Tick(); plugin.OSTick();
            Check(before.Palette[1] != view.Frame.Palette[1], "palette-only refresh reaches the viewer");
            window.UserClose(); plugin.Tick(); plugin.OSTick();
            Check(window.Closed && plugin.Model == null, "viewer closes");
            plugin.KeyPressed(0); plugin.OSTick(); plugin.Tick(); plugin.OSTick();
            Check(fake.Windows.Count == 2 && fake.OpenWindows == 1 && !fake.Windows.Last().Closed && plugin.Model != null && plugin.Model.Frame != null, "viewer reopens");
            plugin.Reset(); plugin.Tick(); plugin.OSTick();
            bool valid; plugin.Read(eAccess.Memory_Read, 0, 0, out valid);
            Check(!valid && !plugin.Write(eAccess.Memory_Write, 0, 0, 0), "viewer does not intercept memory");
            plugin.Quit();
            Check(fake.Windows.Last().Closed && plugin.Model == null, "quit releases window");
        }

        private static void TestMapEditing()
        {
            var fake = new Emulator();
            fake.Registers[0x6b] = 0x82; fake.Registers[0x6e] = 0x3f;
            fake.Bank5[0x3ffe] = 12; fake.Bank5[0x3fff] = 0x18;
            var editor = new TileMapEditor();
            TileSnapshot frame = TileSnapshot.Capture(fake.Api);
            editor.Enqueue(TileMapEdit.Paint(frame, 127, 341, 0x4e, false));
            Check(editor.ApplyPending(fake.Api) && fake.Bank5[0x3ffe] == 0x55 && fake.Bank5[0x3fff] == 0x4f, "paint writes tile ID, ninth bit, palette and transforms");
            Check(editor.CanUndo && fake.WriteAddresses.Last() == 5 * 16384 + 0x3ffe, "paint writes ULA overlay at active physical map address");
            editor.Enqueue(new TileMapEdit { Undo = true });
            Check(editor.ApplyPending(fake.Api) && fake.Bank5[0x3ffe] == 12 && fake.Bank5[0x3fff] == 0x18, "undo restores both map bytes");
            editor.Enqueue(TileMapEdit.Paint(frame, 128, 300, 0xa4, false)); editor.ApplyPending(fake.Api);
            Check(fake.Bank5[0] == 44 && fake.Bank5[1] == 0xa5, "bank 5 map editing wraps at sixteen KB");
            editor.Enqueue(TileMapEdit.Paint(frame, 128, 2, 0, true)); editor.ApplyPending(fake.Api);
            Check(fake.Bank5[0] == 2 && fake.Bank5[1] == 0xa4, "clear uses chosen blank tile and preserves palette/transforms");
            fake.Bank5[0] = 91;
            editor.Enqueue(new TileMapEdit { Undo = true });
            Check(!editor.ApplyPending(fake.Api) && fake.Bank5[0] == 91 && editor.TakeStatus().Contains("program changed"), "undo does not overwrite a newer program write");

            fake.Registers[0x6e] = 0x98;
            frame = TileSnapshot.Capture(fake.Api);
            editor.Enqueue(TileMapEdit.Paint(frame, 1024, 511, 0xe0, false)); editor.ApplyPending(fake.Api);
            Check(fake.Bank7[0] == 255 && fake.Bank7[1] == 0xe1 && fake.WriteAddresses.Last() == 7 * 16384, "bank 7 editing wraps at eight KB");
            int writes = fake.WriteAddresses.Count;
            fake.Registers[0x6e] = 0;
            editor.Enqueue(TileMapEdit.Paint(frame, 0, 5, 0, false));
            Check(!editor.ApplyPending(fake.Api) && fake.WriteAddresses.Count == writes, "stale map-bank command is rejected");

            fake.Registers[0x6b] = 0xa2; fake.Registers[0x6c] = 0xb1;
            frame = TileSnapshot.Capture(fake.Api); fake.Bank5[1] = 0x99;
            editor.Enqueue(TileMapEdit.Paint(frame, 0, 301, 0x40, false)); editor.ApplyPending(fake.Api);
            Check(fake.Bank5[0] == 45 && fake.Bank5[1] == 0x99 && fake.Registers[0x6c] == 0xb1, "attribute-free editing writes only tile byte and preserves global attributes");
            writes = fake.WriteAddresses.Count;
            editor.Enqueue(TileMapEdit.Paint(frame, 0, 12, 0, false));
            Check(!editor.ApplyPending(fake.Api) && fake.WriteAddresses.Count == writes, "attribute-free map rejects unreachable ninth tile bit");
            fake.Registers[0x6c] = 0;
            editor.Enqueue(TileMapEdit.Paint(frame, 0, 301, 0, false));
            Check(!editor.ApplyPending(fake.Api), "attribute-free edit rejects changed default attributes");

            fake.Registers[0x6b] = 0x8a;
            frame = TileSnapshot.Capture(fake.Api);
            editor.Enqueue(TileMapEdit.Paint(frame, 1, 511, 0xfe, false)); editor.ApplyPending(fake.Api);
            Check(fake.Bank5[2] == 255 && fake.Bank5[3] == 255, "text mode supports all 128 palette pairs and ninth tile bit");
            fake.Registers[0x6b] = 0x80;
            frame = TileSnapshot.Capture(fake.Api);
            editor.Enqueue(TileMapEdit.Paint(frame, 0, 300, 0, false));
            Check(!editor.ApplyPending(fake.Api), "256 tile mode rejects upper tile IDs");
            editor.Enqueue(TileMapEdit.Paint(frame, -1, 1, 0, false));
            Check(!editor.ApplyPending(fake.Api), "out-of-range cells cannot write memory");
            editor.Enqueue(TileMapEdit.Paint(frame, 0, 3, 0, false));
            editor.Reset();
            Check(!editor.HasPending && !editor.CanUndo && !editor.ApplyPending(fake.Api), "reset clears queued edits and undo history");
        }

        private static void TestEditingWindow()
        {
            var fake = new Emulator();
            fake.Registers[0x6b] = 0x82; fake.Registers[0x6e] = 0x20;
            for (int i = 9 * 32; i < 10 * 32; i++) fake.Bank5[i] = 0x11;
            fake.Palette[0x41] = 0x1c0;
            fake.DebuggerState = 1;
            var plugin = new TileViewerPlugin();
            plugin.Init(fake.Api); plugin.KeyPressed(0); plugin.OSTick();
            try
            {
                TileViewerModel view = plugin.Model;
                Check(view.Summary.Contains("Map $6000"), "opening while already paused captures the map without Tick");
                fake.DebuggerState = 0;
                plugin.Tick(); plugin.OSTick();
                view.SelectTab(1);
                Check(view.Tool == TileTool.Draw, "Draw is the default tool");
                view.Tool = TileTool.Pick;
                view.MapMouseDown(4, 4, TileMouseButton.Left); view.MapMouseUp(); plugin.Tick(); plugin.OSTick();
                Check(fake.WriteAddresses.Count == 0, "Pick tool samples without writing memory");
                view.Tool = TileTool.Draw; view.BrushTile = 341; view.BrushPalette = 4;
                view.MapMouseDown(4, 4, TileMouseButton.Left); view.MapMouseUp(); plugin.OSTick();
                Check(fake.WriteAddresses.Count == 0, "running-machine edits are queued for emulator Tick");
                plugin.Tick(); plugin.OSTick();
                Check(fake.Bank5[0x2000] == 0x55 && fake.Bank5[0x2001] == 0x41, "UI painting reaches live emulator map");
                Check(view.UndoAvailable, "Undo cell is enabled after an edit");
                fake.DebuggerState = 1;
                view.BrushTile = 200; view.BrushPalette = 6;
                view.MapMouseDown(20, 4, TileMouseButton.Left); view.MapMouseUp(); plugin.OSTick();
                Check(fake.Bank5[0x2002] == 200 && fake.Bank5[0x2003] == 0x60, "editing works while paused without Tick");
                view.ClearTile = 7;
                view.Tool = TileTool.Clear;
                view.MapMouseDown(20, 4, TileMouseButton.Left); view.MapMouseUp(); plugin.OSTick();
                Check(fake.Bank5[0x2002] == 7 && fake.Bank5[0x2003] == 0x60, "Clear tool makes left-click clear to selected blank tile");
                view.UndoCell(); plugin.OSTick();
                Check(fake.Bank5[0x2002] == 200, "Undo cell restores a clear-tool edit");
                view.Tool = TileTool.Draw;
                view.MapMouseDown(20, 4, TileMouseButton.Right); view.MapMouseUp(); plugin.OSTick();
                Check(fake.Bank5[0x2002] == 7 && fake.Bank5[0x2003] == 0x60, "right-click clears to selected blank tile");
                view.UndoCell(); plugin.OSTick();
                Check(fake.Bank5[0x2002] == 200, "Undo cell restores latest edit while paused");
                view.MapMouseDown(4, 4, TileMouseButton.Middle);
                Check(view.BrushTile == 341 && view.BrushPalette == 4, "middle-click picks tile and palette from map");
                view.BrushTile = 9;
                view.MapMouseDown(36, 4, TileMouseButton.Left);
                view.MapMouseMove(52, 4);
                view.MapMouseUp(); plugin.OSTick();
                Check(fake.Bank5[0x2004] == 9 && fake.Bank5[0x2006] == 9, "drag painting edits successive cells");
                Check(view.Details.Contains("Cell 3,0 | tile 9") && view.Details.Contains("palette 4"), "hover footer refreshes after editing without moving the mouse");
                Check(view.MapPixel(24, 0) == Color.Red.ToArgb(), "edited tile and palette refresh the map image immediately");
                int writes = fake.WriteAddresses.Count;
                view.MapMouseDown(645, 4, TileMouseButton.Left); view.MapMouseUp(); plugin.OSTick();
                Check(fake.WriteAddresses.Count == writes, "clicking outside the map does not write");
                view.MapMouseDown(4, 4, TileMouseButton.Left); view.MapMouseMove(700, 4); view.MapMouseMove(2000, 2000); view.MapMouseUp(); plugin.OSTick();
                Check(fake.WriteAddresses.Count == writes + 1, "dragging off the map stops painting without writing");
                view.BrushTile = 600;
                Check(view.BrushTile == view.TileMaximum, "tile number is limited to the map's tile range");
                view.TileCountIndex = 1;
                view.TilesMouseDown(4, 4, TileMouseButton.Left);
                Check(view.BrushTile == 0 && view.EditStatus.Contains("Brush: tile 0"), "clicking a pattern selects it as the brush");
            }
            finally { plugin.Quit(); }
        }

        // A plugin window: records the ImGui draw callback and textures, fires OnClosed when closed
        private sealed class FakeWindow : RealProxy
        {
            internal string Title;
            internal Action Draw;
            internal bool Closed;
            internal int Textures, Uploads;
            private EventHandler closed;
            internal iWindow Api { get { return (iWindow)GetTransparentProxy(); } }
            internal FakeWindow(string title) : base(typeof(iWindow)) { Title = title; }

            // The user closing the OS window
            internal void UserClose() { if (Closed) return; Closed = true; if (closed != null) closed(Api, EventArgs.Empty); }

            public override IMessage Invoke(IMessage message)
            {
                var call = (IMethodCallMessage)message;
                object result = null;
                switch (call.MethodName)
                {
                    case "SetImGuiDraw": Draw = (Action)call.Args[0]; result = true; break;
                    case "CreateImGuiTexture": result = new IntPtr(++Textures); break;
                    case "UpdateImGuiTexture": Uploads++; break;
                    case "DestroyImGuiTexture": break;
                    case "add_OnClosed": closed += (EventHandler)call.Args[0]; break;
                    case "remove_OnClosed": closed -= (EventHandler)call.Args[0]; break;
                    case "Close": UserClose(); break;
                    case "get_Title": result = Title; break;
                    default: throw new Exception("Unexpected window operation: " + call.MethodName);
                }
                return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
            }
        }

        private sealed class Emulator : RealProxy
        {
            internal readonly byte[] Registers = new byte[256], Bank5 = new byte[16384], Bank7 = new byte[8192];
            internal readonly uint[] Palette = new uint[256], Palette2 = new uint[256];
            internal int LastPalette, MemoryReads;
            internal int DebuggerState;
            internal readonly List<int> WriteAddresses = new List<int>();
            internal readonly List<FakeWindow> Windows = new List<FakeWindow>();
            internal int OpenWindows { get { return Windows.Count(w => !w.Closed); } }
            internal iCSpect Api { get { return (iCSpect)GetTransparentProxy(); } }
            internal Emulator() : base(typeof(iCSpect)) { for (int i = 0; i < 256; i++) Palette[i] = Palette2[i] = (uint)i; }

            public override IMessage Invoke(IMessage message)
            {
                var call = (IMethodCallMessage)message;
                object result;
                switch (call.MethodName)
                {
                    case "GetNextRegister": result = Registers[(byte)call.Args[0]]; break;
                    case "GetColour": LastPalette = (int)call.Args[0]; result = (LastPalette == 3 ? Palette : Palette2)[(int)call.Args[1]]; break;
                    case "PeekPhysicalULA":
                        MemoryReads++;
                        int address = (int)call.Args[0], count = (int)call.Args[1];
                        byte[] bank = address >= 5 * 16384 && address < 6 * 16384 ? Bank5 : address >= 7 * 16384 && address < 7 * 16384 + 8192 ? Bank7 : null;
                        int offset = address - (bank == Bank5 ? 5 : 7) * 16384;
                        if (bank == null || offset + count > bank.Length) throw new Exception("Unexpected ULA capture range");
                        var bytes = new byte[count]; Array.Copy(bank, offset, bytes, 0, count); result = bytes; break;
                    case "PokePhysicalULA":
                        int writeAddress = (int)call.Args[0];
                        byte[] target = writeAddress >= 5 * 16384 && writeAddress < 6 * 16384 ? Bank5 : writeAddress >= 7 * 16384 && writeAddress < 7 * 16384 + 8192 ? Bank7 : null;
                        byte[] values = call.Args[1] as byte[] ?? new[] { (byte)call.Args[1] };
                        int writeOffset = writeAddress - (target == Bank5 ? 5 : 7) * 16384;
                        if (target == null || writeOffset + values.Length > target.Length) throw new Exception("Unexpected ULA write range");
                        Array.Copy(values, 0, target, writeOffset, values.Length); WriteAddresses.Add(writeAddress); result = null; break;
                    case "Debugger":
                        if ((eDebugCommand)call.Args[0] != eDebugCommand.GetState) throw new Exception("Unexpected debugger operation");
                        result = DebuggerState; break;
                    case "OpenWindow":
                        var window = new FakeWindow((string)call.Args[0]);
                        Windows.Add(window); result = window.Api; break;
                    default: throw new Exception("Unexpected emulator operation: " + call.MethodName);
                }
                return new ReturnMessage(result, null, 0, call.LogicalCallContext, call);
            }
        }
    }
}

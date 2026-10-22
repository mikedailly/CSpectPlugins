// ********************************************************************************************************************************************
//      CSpect Sprite Viewer extension, shows the sprite patterns in realtime
//      Written by:
//                  Mike Dailly
//
//      contributions by:
//
//      Released under the GNU 3 license - please see license file for more details
//
//      The viewer UI, drawn with Dear ImGui into the plugin's own OS window (iWindow.SetImGuiDraw).
//      All patterns are drawn into one 128x256 ARGB atlas (8 patterns across, 16 down) that's uploaded to an
//      ImGui texture whenever sprite memory, the palette or a setting changes.
//
// ********************************************************************************************************************************************
using ImGuiNET;
using Plugin;
using System;
using System.Numerics;

namespace SpriteViewer
{
    // *********************************************************************************************************
    /// <summary>
    ///     The Sprite Viewer window contents
    /// </summary>
    // *********************************************************************************************************
    public class SpriteViewerView
    {
        const int ATLAS_W = 128;            // 8 patterns across
        const int ATLAS_H = 256;            // 16 patterns down
        const int SCALE = 2;                // each 16x16 pattern is shown at 32x32

        static readonly string[] ModeNames = { "256 Colour Mode", "16 Colour Mode" };
        static readonly string[] SizeNames = { "16 x 16", "32 x 32" };
        static readonly string[] PaletteNames = { "Sprite 1", "Sprite 2" };
        static readonly string[] OffsetNames = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15" };

        SpriteViewerPlugin Plugin;
        iWindow Window;

        /// <summary>0 = 256 colour (8 bit) patterns, 1 = 16 colour (4 bit)</summary>
        int ModeIndex = 1;
        /// <summary>0 = 16x16, 1 = 32x32 (2x2 groups of patterns)</summary>
        int SizeIndex = 0;
        /// <summary>Palette offset 0-15 (x16)</summary>
        int OffsetIndex = 0;
        /// <summary>0 = first sprite palette, 1 = second</summary>
        int PaletteIndex = 0;

        bool Is16Col { get { return ModeIndex == 1; } }
        /// <summary>Number of patterns in sprite memory for the current mode</summary>
        int PatternCount { get { return Is16Col ? 128 : 64; } }

        // local copy of the plugin's snapshot (so we never hold the lock while drawing)
        byte[] Memory = new byte[16384];
        uint[] Palette = new uint[256];
        int SeenVersion = -1;

        UInt32[] Pixels = new UInt32[ATLAS_W * ATLAS_H];
        IntPtr Texture = IntPtr.Zero;
        bool Rebuild = true;


        // ******************************************************************************************
        /// <summary>
        ///     Create the view
        /// </summary>
        // ******************************************************************************************
        public SpriteViewerView(SpriteViewerPlugin _plugin)
        {
            Plugin = _plugin;
        }

        /// <summary>A window has been opened for us</summary>
        public void Attach(iWindow _window)
        {
            Window = _window;
            Texture = IntPtr.Zero;              // created on first draw
            Rebuild = true;
        }

        /// <summary>Our window has gone (its textures were freed with it)</summary>
        public void Detach()
        {
            Window = null;
            Texture = IntPtr.Zero;
        }


        // ******************************************************************************************
        /// <summary>
        ///     Redraw every pattern into the atlas and upload it
        /// </summary>
        // ******************************************************************************************
        void BuildAtlas()
        {
            Array.Clear(Pixels, 0, Pixels.Length);
            int count = PatternCount;
            int offset = OffsetIndex * 16;
            for (int i = 0; i < count; i++)
            {
                ZXSprite.DrawSprite(Pixels, ATLAS_W, (i & 7) * 16, (i >> 3) * 16, Is16Col, i, offset, Memory, Palette);
            }
            Window.UpdateImGuiTexture(Texture, Pixels);
        }


        #region Drawing
        // ******************************************************************************************
        /// <summary>
        ///     iWindow ImGui draw callback - one panel filling the OS window
        /// </summary>
        // ******************************************************************************************
        public void Draw()
        {
            if (Window == null) return;
            if (Texture == IntPtr.Zero)
            {
                Texture = Window.CreateImGuiTexture(ATLAS_W, ATLAS_H);
                Rebuild = true;
            }

            // New sprite memory/palette from the emulator thread?
            if (Plugin.SnapshotVersion != SeenVersion)
            {
                lock (Plugin.SnapshotLock)
                {
                    Array.Copy(Plugin.SnapshotMemory, Memory, Memory.Length);
                    Array.Copy(Plugin.SnapshotPalette, Palette, Palette.Length);
                    SeenVersion = Plugin.SnapshotVersion;
                }
                Rebuild = true;
            }
            if (Rebuild && Texture != IntPtr.Zero)
            {
                BuildAtlas();
                Rebuild = false;
            }

            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
            ImGuiWindowFlags wflags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                                      ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus;
            bool open = true;
            if (ImGui.Begin("##SpriteViewer", ref open, wflags))
            {
                DrawToolbar();
                ImGui.Separator();
                if (ImGui.BeginChild("##patterns", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.None))
                {
                    if (Texture != IntPtr.Zero)
                    {
                        if (SizeIndex == 0) DrawPatterns16(); else DrawPatterns32();
                    }
                }
                ImGui.EndChild();
            }
            ImGui.End();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Mode / Palette Offset / Size / Palette - labels to the left of each control
        /// </summary>
        // ******************************************************************************************
        void DrawToolbar()
        {
            ImGuiTableFlags tflags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX;
            if (!ImGui.BeginTable("##toolbar", 4, tflags)) return;
            for (int c = 0; c < 4; c++) ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed);

            ImGui.TableNextRow();
            Label("Mode");
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("##mode", ref ModeIndex, ModeNames, ModeNames.Length)) Rebuild = true;

            Label("Palette Offset");
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(70);
            if (ImGui.Combo("##offset", ref OffsetIndex, OffsetNames, OffsetNames.Length)) Rebuild = true;

            ImGui.TableNextRow();
            Label("Size");
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(150);
            ImGui.Combo("##size", ref SizeIndex, SizeNames, SizeNames.Length);

            Label("Palette");
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(100);
            if (ImGui.Combo("##palette", ref PaletteIndex, PaletteNames, PaletteNames.Length)) Plugin.PaletteSelect = PaletteIndex;

            ImGui.EndTable();
        }

        static void Label(string _text)
        {
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(_text);
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw one pattern from the atlas at the cursor, with a tooltip on hover
        /// </summary>
        // ******************************************************************************************
        void PatternImage(int _pattern, Vector2 _pos)
        {
            ImGui.SetCursorPos(_pos);
            Vector2 uv0 = new Vector2(((_pattern & 7) * 16) / (float)ATLAS_W, ((_pattern >> 3) * 16) / (float)ATLAS_H);
            Vector2 uv1 = uv0 + new Vector2(16.0f / ATLAS_W, 16.0f / ATLAS_H);
            ImGui.Image(Texture, new Vector2(16 * SCALE, 16 * SCALE), uv0, uv1);
            if (ImGui.IsItemHovered()) PatternTooltip(_pattern, uv0, uv1);
        }

        // ******************************************************************************************
        /// <summary>
        ///     16x16: 8 patterns across, 40 pixels apart
        /// </summary>
        // ******************************************************************************************
        void DrawPatterns16()
        {
            Vector2 origin = ImGui.GetCursorPos();
            int count = PatternCount;
            for (int i = 0; i < count; i++)
            {
                PatternImage(i, origin + new Vector2((i & 7) * 40, (i >> 3) * 40));
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     32x32: groups of 4 patterns (n, n+1 on top, n+2, n+3 below), 8 groups across
        /// </summary>
        // ******************************************************************************************
        void DrawPatterns32()
        {
            Vector2 origin = ImGui.GetCursorPos();
            int count = PatternCount;
            int cell = 16 * SCALE;
            for (int g = 0; g < count / 4; g++)
            {
                Vector2 p = origin + new Vector2((g & 7) * 72, (g >> 3) * 72);
                PatternImage(g * 4 + 0, p);
                PatternImage(g * 4 + 1, p + new Vector2(cell, 0));
                PatternImage(g * 4 + 2, p + new Vector2(0, cell));
                PatternImage(g * 4 + 3, p + new Vector2(cell, cell));
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Pattern number, where it lives in sprite RAM, and a big preview
        /// </summary>
        // ******************************************************************************************
        void PatternTooltip(int _pattern, Vector2 _uv0, Vector2 _uv1)
        {
            int bytes = Is16Col ? 128 : 256;
            int address = _pattern * bytes;
            ImGui.BeginTooltip();
            if (Is16Col)
            {
                // 4 bit pattern n is the first/second 128 bytes of 8 bit pattern n/2
                ImGui.TextUnformatted(string.Format("Pattern {0} (4 bit)   ${1:X4}-${2:X4}", _pattern, address, address + bytes - 1));
                ImGui.TextDisabled(string.Format("= {0} half of 8 bit pattern {1}", (_pattern & 1) == 0 ? "first" : "second", _pattern >> 1));
            }
            else
            {
                ImGui.TextUnformatted(string.Format("Pattern {0} (8 bit)   ${1:X4}-${2:X4}", _pattern, address, address + bytes - 1));
            }
            ImGui.TextDisabled(string.Format("Palette offset {0}", OffsetIndex));
            ImGui.Image(Texture, new Vector2(128, 128), _uv0, _uv1);
            ImGui.EndTooltip();
        }
        #endregion
    }
}

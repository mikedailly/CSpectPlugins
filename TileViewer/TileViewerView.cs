using System;
using System.Numerics;
using ImGuiNET;
using Plugin;

namespace TileViewer
{
    // Draws TileViewerModel with Dear ImGui into the plugin's own OS window (iWindow.SetImGuiDraw),
    // and turns ImGui mouse input into model calls. Main thread only.
    internal sealed class TileViewerView
    {
        private static readonly string[] TileCounts = { "256", "512" };
        private static readonly string[] Palettes = { "Active", "1", "2" };
        private static readonly string[] Banks = { "Active", "5", "7" };
        private static readonly string[] Offsets16 = Numbers(16), Offsets128 = Numbers(128);
        private static readonly Vector4 ToolActive = new Vector4(0.26f, 0.59f, 0.98f, 1.0f);

        private readonly TileViewerModel model;
        private iWindow window;
        private IntPtr atlasTexture, mapTexture;
        private int atlasVersion = -1, mapVersion = -1;

        internal TileViewerView(TileViewerModel model) { this.model = model; }

        internal void Attach(iWindow window)
        {
            this.window = window;
            atlasTexture = mapTexture = IntPtr.Zero;        // created on first draw
            atlasVersion = mapVersion = -1;
        }

        // The window's textures are freed with it
        internal void Detach() { window = null; atlasTexture = mapTexture = IntPtr.Zero; }

        private static string[] Numbers(int count)
        {
            var names = new string[count];
            for (int i = 0; i < count; i++) names[i] = i.ToString();
            return names;
        }

        // ****************************************************************************************
        /// <summary>
        ///     Update tilemap textures
        /// </summary>
        // ****************************************************************************************
        private void UploadTextures()
        {
            if (atlasTexture == IntPtr.Zero) { atlasTexture = window.CreateImGuiTexture(TileViewerModel.AtlasWidth, TileViewerModel.AtlasHeight); atlasVersion = -1; }
            if (mapTexture == IntPtr.Zero) { mapTexture = window.CreateImGuiTexture(TileViewerModel.MapWidth, TileViewerModel.MapHeight); mapVersion = -1; }
            if (atlasTexture != IntPtr.Zero && atlasVersion != model.TileAtlasVersion)
            { window.UpdateImGuiTexture(atlasTexture, model.TileAtlas); atlasVersion = model.TileAtlasVersion; }
            if (mapTexture != IntPtr.Zero && mapVersion != model.MapImageVersion)
            { window.UpdateImGuiTexture(mapTexture, model.MapImage); mapVersion = model.MapImageVersion; }
        }

        // ****************************************************************************************
        /// <summary>
        ///     iWindow ImGui draw callback - one panel filling the OS window
        /// </summary>
        // ****************************************************************************************
        internal void Draw()
        {
            if (window == null) return;
            UploadTextures();

            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                                     ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus;
            bool open = true;
            if (ImGui.Begin("##TileViewer", ref open, flags))
            {
                ImGui.TextUnformatted(model.Summary);
                ImGui.Separator();

                // leave room for the two line hover footer
                float footer = ImGui.GetTextLineHeightWithSpacing() * 2 + ImGui.GetStyle().ItemSpacing.Y * 2;
                if (ImGui.BeginTabBar("##tabs"))
                {
                    if (ImGui.BeginTabItem("Tiles"))
                    {
                        if (model.SelectedTab != 0) model.SelectTab(0);
                        DrawTilesTab(footer);
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Tilemap (unscrolled)"))
                    {
                        if (model.SelectedTab != 1) model.SelectTab(1);
                        DrawMapTab(footer);
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }
                ImGui.Separator();
                ImGui.TextUnformatted(model.Details);
            }
            ImGui.End();
        }

        private static void Label(string text)
        {
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(text);
        }

        private static bool Combo(string id, int width, ref int value, string[] items)
        {
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(width);
            return ImGui.Combo(id, ref value, items, items.Length);
        }

        // ****************************************************************************************
        // Tiles: Tiles / Palette / Palette Offset / Bank selectors, then the 16 column grid
        // ****************************************************************************************
        private void DrawTilesTab(float footer)
        {
            if (ImGui.BeginTable("##tilebar", 8, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX))
            {
                ImGui.TableNextRow();
                int v = model.TileCountIndex;
                Label("Tiles"); if (Combo("##count", 75, ref v, TileCounts)) model.TileCountIndex = v;
                v = model.PaletteIndex;
                Label("Palette"); if (Combo("##palette", 95, ref v, Palettes)) model.PaletteIndex = v;
                v = model.PaletteOffsetIndex;
                Label("Palette Offset"); if (Combo("##offset", 75, ref v, model.PaletteOffsetCount == 128 ? Offsets128 : Offsets16)) model.PaletteOffsetIndex = v;
                v = model.BankIndex;
                Label("Bank"); if (Combo("##bank", 75, ref v, Banks)) model.BankIndex = v;
                ImGui.EndTable();
            }

            if (ImGui.BeginChild("##tiles", new Vector2(0, -footer), ImGuiChildFlags.Borders, ImGuiWindowFlags.None))
            {
                Vector2 origin = ImGui.GetCursorScreenPos();
                if (model.Frame != null && atlasTexture != IntPtr.Zero)
                {
                    ImDrawListPtr draw = ImGui.GetWindowDrawList();
                    float top = ImGui.GetScrollY(), bottom = top + ImGui.GetWindowHeight();
                    int count = model.TileCountShown;
                    float du = 8.0f / TileViewerModel.AtlasWidth, dv = 8.0f / TileViewerModel.AtlasHeight;
                    for (int tile = 0; tile < count; tile++)
                    {
                        int x = (tile % TileViewerModel.GridColumns) * TileViewerModel.GridCell;
                        int y = (tile / TileViewerModel.GridColumns) * TileViewerModel.GridCell;
                        if (y + TileViewerModel.GridCell < top || y > bottom) continue;
                        Vector2 p = origin + new Vector2(x + 4, y + 4);
                        Vector2 uv = new Vector2((tile % TileViewerModel.GridColumns) * du, (tile / TileViewerModel.GridColumns) * dv);
                        draw.AddImage(atlasTexture, p, p + new Vector2(32, 32), uv, uv + new Vector2(du, dv));
                    }
                }
                ImGui.Dummy(new Vector2(model.GridWidth, model.GridHeight));

                if (ImGui.IsWindowHovered())
                {
                    Vector2 local = ImGui.GetIO().MousePos - origin;
                    model.TilesMouseMove((int)local.X, (int)local.Y);
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) model.TilesMouseDown((int)local.X, (int)local.Y, TileMouseButton.Left);
                }
            }
            ImGui.EndChild();
        }

        // ****************************************************************************************
        // Tilemap: editing tools, then the whole map at 2x
        // ****************************************************************************************
        private void DrawMapTab(float footer)
        {
            DrawEditTools();

            if (ImGui.BeginChild("##map", new Vector2(0, -footer), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar))
            {
                Vector2 origin = ImGui.GetCursorScreenPos();
                if (model.Frame != null && mapTexture != IntPtr.Zero)
                {
                    Vector2 uv1 = new Vector2(model.Frame.Columns * 8.0f / TileViewerModel.MapWidth, 1.0f);
                    ImGui.Image(mapTexture, new Vector2(model.MapViewWidth, model.MapViewHeight), Vector2.Zero, uv1);
                }

                Vector2 local = ImGui.GetIO().MousePos - origin;
                int mx = (int)Math.Floor(local.X), my = (int)Math.Floor(local.Y);
                if (ImGui.IsWindowHovered())
                {
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) model.MapMouseDown(mx, my, TileMouseButton.Left);
                    else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right)) model.MapMouseDown(mx, my, TileMouseButton.Right);
                    else if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle)) model.MapMouseDown(mx, my, TileMouseButton.Middle);
                    else model.MapMouseMove(mx, my);
                }
                else if (model.Painting)
                {
                    model.MapMouseMove(mx, my);         // keep the drag going while the mouse is outside (like a capture)
                }
                if (model.Painting && !ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseDown(ImGuiMouseButton.Right))
                    model.MapMouseUp();
            }
            ImGui.EndChild();
        }

        private void ToolButton(string text, TileTool tool)
        {
            bool active = model.Tool == tool;
            if (active) ImGui.PushStyleColor(ImGuiCol.Button, ToolActive);
            if (ImGui.Button(text, new Vector2(52, 0))) model.Tool = tool;
            if (active) ImGui.PopStyleColor();
            ImGui.SameLine();
        }

        private static bool NumberInput(string label, string id, float width, ref int value)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(label);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(width);
            bool changed = ImGui.InputInt(id, ref value);
            ImGui.SameLine(0, 14);
            return changed;
        }

        private void DrawEditTools()
        {
            ToolButton("Draw", TileTool.Draw);
            ToolButton("Clear", TileTool.Clear);
            ToolButton("Pick", TileTool.Pick);
            ImGui.SameLine(0, 14);

            int v = model.BrushTile;
            if (NumberInput("Tile", "##brushtile", 100, ref v)) model.BrushTile = v;
            ImGui.BeginDisabled(!model.PaletteEnabled);
            v = model.BrushPalette;
            if (NumberInput("Palette", "##brushpalette", 90, ref v)) model.BrushPalette = v;
            ImGui.EndDisabled();
            v = model.ClearTile;
            if (NumberInput("Clear tile", "##cleartile", 100, ref v)) model.ClearTile = v;
            ImGui.NewLine();

            ImGui.BeginDisabled(!model.TransformEnabled);
            ImGui.Checkbox("Mirror X", ref model.MirrorX); ImGui.SameLine();
            ImGui.Checkbox("Mirror Y", ref model.MirrorY); ImGui.SameLine();
            ImGui.Checkbox("Rotate", ref model.Rotate); ImGui.SameLine();
            ImGui.EndDisabled();
            ImGui.BeginDisabled(!model.UlaOverEnabled);
            ImGui.Checkbox("ULA over", ref model.UlaOver); ImGui.SameLine();
            ImGui.EndDisabled();
            ImGui.BeginDisabled(!model.UndoAvailable);
            if (ImGui.Button("Undo cell")) model.UndoCell();
            ImGui.EndDisabled();

            ImGui.TextUnformatted(model.EditStatus);
        }
    }
}

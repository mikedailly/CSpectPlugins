// ****************************************************************************************
//  Palette Viewer - Dear ImGui view
//
//  Everything in here runs on the UI thread inside the iWindow ImGui draw callback.
// ****************************************************************************************
using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using ImGuiNET;

namespace PaletteViewer
{
    internal sealed class PaletteViewerView
    {
        private const string EditPopupName = "Edit Colour###PaletteEdit";

        // Tab order: ULA, Layer 2, Sprites, Tilemap. These are also the CSpect palette numbers
        // (0-3 = first palette, add 4 for the second palette).
        private static readonly string[] LayerNames = { "ULA", "Layer 2", "Sprites", "Tilemap" };
        private static readonly string[] HexDigits = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "A", "B", "C", "D", "E", "F" };

        private readonly PaletteViewerPlugin Plugin;
        private readonly PaletteSnapshot Local = new PaletteSnapshot();
        private int Version = -1;

        // Which palette (0 = first, 1 = second) each tab is looking at, -1 = follow the active one
        private readonly int[] BankSelect = { 0, 0, 0, 0 };
        private readonly bool[] FollowActive = { true, true, true, true };
        private readonly int[] Selected = { 0, 0, 0, 0 };

        // Editor state
        private bool OpenEditorRequest;
        private bool EditorOpen;
        private int EditPalette;
        private int EditIndex;
        private int EditOriginal;
        private int EditR, EditG, EditB;
        private string EditHex = "000";
        private bool EditLive = true;
        private int LastLiveValue = -1;

        // Load / save
        private readonly FileBrowser Browser = new FileBrowser();
        private int ViewedPalette;              // CSpect palette number (0-7) of the tab on screen
        private int FilePalette;                // palette the open Load/Save applies to
        private string LastFile = "";
        private string Status = "";
        private bool StatusIsError;


        // ------------------------------------------------------------------------------------
        public PaletteViewerView(PaletteViewerPlugin _plugin)
        {
            Plugin = _plugin;
        }

        // ------------------------------------------------------------------------------------
        //  Colour helpers
        // ------------------------------------------------------------------------------------
        /// <summary>3 bit to 8 bit, the same expansion the Next uses</summary>
        private static int Expand3(int _c)
        {
            _c &= 7;
            return (_c << 5) | (_c << 2) | (_c >> 1);
        }

        /// <summary>9 bit RRRGGGBBB to ImGui packed ABGR</summary>
        private static uint ToU32(uint _v)
        {
            uint r = (uint)Expand3((int)(_v >> 6));
            uint g = (uint)Expand3((int)(_v >> 3));
            uint b = (uint)Expand3((int)_v);
            return 0xff000000 | (b << 16) | (g << 8) | r;
        }

        /// <summary>9 bit RRRGGGBBB to an ImGui Vector4</summary>
        private static Vector4 ToVec4(int _v)
        {
            return new Vector4(Expand3(_v >> 6) / 255.0f, Expand3(_v >> 3) / 255.0f, Expand3(_v) / 255.0f, 1.0f);
        }

        private static int Make9(int _r, int _g, int _b)
        {
            return ((_r & 7) << 6) | ((_g & 7) << 3) | (_b & 7);
        }

        /// <summary>Which bank (0/1) is the one the hardware is displaying for this layer</summary>
        private int ActiveBank(int _layer)
        {
            switch (_layer)
            {
                case 0: return (Local.Reg43 >> 1) & 1;     // NextReg $43 bit 1 - ULA
                case 1: return (Local.Reg43 >> 2) & 1;     // NextReg $43 bit 2 - Layer 2
                case 2: return (Local.Reg43 >> 3) & 1;     // NextReg $43 bit 3 - Sprites
                default: return (Local.Reg6B >> 4) & 1;    // NextReg $6B bit 4 - Tilemap
            }
        }

        private static string PaletteName(int _palette)
        {
            return LayerNames[_palette & 3] + ((_palette & 4) != 0 ? " (second)" : " (first)");
        }


        // ------------------------------------------------------------------------------------
        /// <summary>
        ///     ImGui draw callback
        /// </summary>
        // ------------------------------------------------------------------------------------
        public void Draw()
        {
            Plugin.CopySnapshot(Local, ref Version);

            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
            bool open = true;
            ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoMove |
                                     ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus |
                                     ImGuiWindowFlags.NoDocking;
            if (ImGui.Begin("##PaletteViewer", ref open, flags))
            {
                DrawMenu();

                if (ImGui.BeginTabBar("##layers"))
                {
                    for (int layer = 0; layer < 4; layer++)
                    {
                        if (ImGui.BeginTabItem(LayerNames[layer]))
                        {
                            DrawLayer(layer);
                            ImGui.EndTabItem();
                        }
                    }
                    ImGui.EndTabBar();
                }

                if (OpenEditorRequest)
                {
                    OpenEditorRequest = false;
                    ImGui.OpenPopup(EditPopupName);
                }
                DrawEditor();

                if (Browser.Draw()) FileChosen(Browser.Result);
            }
            ImGui.End();
        }


        // ------------------------------------------------------------------------------------
        //  Menu bar, load and save
        // ------------------------------------------------------------------------------------
        private void DrawMenu()
        {
            bool busy = EditorOpen || Browser.IsOpen;

            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("File"))
                {
                    ImGui.TextDisabled(PaletteName(ViewedPalette));
                    ImGui.Separator();
                    if (ImGui.MenuItem("Load palette...", "Ctrl+O", false, !busy)) StartLoad();
                    if (ImGui.MenuItem("Save palette...", "Ctrl+S", false, !busy)) StartSave();
                    ImGui.EndMenu();
                }
                ImGui.EndMenuBar();
            }

            // Shortcuts
            if (!busy && ImGui.GetIO().KeyCtrl)
            {
                if (ImGui.IsKeyPressed(ImGuiKey.O, false)) StartLoad();
                else if (ImGui.IsKeyPressed(ImGuiKey.S, false)) StartSave();
            }
        }

        private void StartLoad()
        {
            FilePalette = ViewedPalette;
            Browser.Open(false, "Load " + PaletteName(FilePalette), LastFile);
        }

        private void StartSave()
        {
            FilePalette = ViewedPalette;
            string start = LastFile;
            if (string.IsNullOrEmpty(start))
            {
                start = LayerNames[FilePalette & 3].Replace(" ", "").ToLowerInvariant() + ((FilePalette & 4) != 0 ? "_2" : "_1") + ".nxp";
            }
            Browser.Open(true, "Save " + PaletteName(FilePalette), start);
        }

        private void FileChosen(string _path)
        {
            if (string.IsNullOrEmpty(_path)) return;
            try
            {
                if (Browser.IsSave)
                {
                    PaletteFile.Save(_path, Local.Colours, FilePalette * 256, Browser.SaveFormat);
                    SetStatus("Saved " + PaletteName(FilePalette) + " to " + Path.GetFileName(_path), false);
                }
                else
                {
                    ePaletteFormat format;
                    int[] colours = PaletteFile.Load(_path, out format);
                    for (int i = 0; i < 256; i++)
                    {
                        Plugin.QueueEdit(FilePalette, i, colours[i]);
                    }
                    SetStatus("Loaded " + Path.GetFileName(_path) + " (" + PaletteFile.FormatNames[(int)format] + ") into " + PaletteName(FilePalette), false);
                }
                LastFile = _path;
            }
            catch (Exception ex)
            {
                SetStatus((Browser.IsSave ? "Save failed: " : "Load failed: ") + ex.Message, true);
            }
        }

        private void SetStatus(string _text, bool _error)
        {
            Status = _text;
            StatusIsError = _error;
        }


        // ------------------------------------------------------------------------------------
        /// <summary>One tab - bank selector, 16x16 grid and info line</summary>
        // ------------------------------------------------------------------------------------
        private void DrawLayer(int _layer)
        {
            int active = ActiveBank(_layer);
            if (FollowActive[_layer]) BankSelect[_layer] = active;

            // ---- Bank selection ----
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Palette");
            ImGui.SameLine();
            int bank = BankSelect[_layer];
            if (ImGui.RadioButton("First##bank", ref bank, 0)) FollowActive[_layer] = false;
            ImGui.SameLine();
            if (ImGui.RadioButton("Second##bank", ref bank, 1)) FollowActive[_layer] = false;
            BankSelect[_layer] = bank;
            ImGui.SameLine();
            bool follow = FollowActive[_layer];
            if (ImGui.Checkbox("Follow active", ref follow))
            {
                FollowActive[_layer] = follow;
                if (follow) BankSelect[_layer] = active;
            }

            if (BankSelect[_layer] == active)
                ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), "Showing the active palette");
            else
                ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.3f, 1.0f), "Not displayed - the " + (active == 0 ? "first" : "second") + " palette is active");

            int palette = _layer + (BankSelect[_layer] * 4);
            int baseIndex = palette * 256;
            ViewedPalette = palette;

            // ---- Grid layout ----
            float lineH = ImGui.GetTextLineHeightWithSpacing();
            float labelW = ImGui.CalcTextSize("F0").X + 6.0f;
            float headerH = ImGui.GetTextLineHeight() + 3.0f;
            float infoH = (lineH * 4.0f) + 4.0f;
            Vector2 avail = ImGui.GetContentRegionAvail();
            float cell = (float)Math.Floor(Math.Min((avail.X - labelW) / 16.0f, (avail.Y - headerH - infoH) / 16.0f));
            if (cell < 10.0f) cell = 10.0f;
            const float gap = 1.0f;

            Vector2 origin = ImGui.GetCursorScreenPos();
            Vector2 gridPos = origin + new Vector2(labelW, headerH);
            Vector2 totalSize = new Vector2(labelW + (16.0f * cell), headerH + (16.0f * cell));

            ImGui.InvisibleButton("##grid", totalSize);
            bool itemHovered = ImGui.IsItemHovered();
            bool clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);

            int hover = -1;
            if (itemHovered)
            {
                Vector2 m = ImGui.GetIO().MousePos - gridPos;
                if (m.X >= 0 && m.Y >= 0)
                {
                    int cx = (int)(m.X / cell);
                    int cy = (int)(m.Y / cell);
                    if (cx < 16 && cy < 16) hover = (cy * 16) + cx;
                }
            }

            if (hover >= 0)
            {
                if (clicked) Selected[_layer] = hover;
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    Selected[_layer] = hover;
                    BeginEdit(palette, hover);
                }
            }

            // ---- Draw ----
            ImDrawListPtr dl = ImGui.GetWindowDrawList();
            uint textCol = ImGui.GetColorU32(ImGuiCol.Text);
            uint dimCol = ImGui.GetColorU32(ImGuiCol.TextDisabled);
            float charW = ImGui.CalcTextSize("0").X;

            for (int i = 0; i < 16; i++)
            {
                // Column header 0-F and row labels 00-F0
                dl.AddText(new Vector2(gridPos.X + (i * cell) + ((cell - charW) * 0.5f), origin.Y), dimCol, HexDigits[i]);
                dl.AddText(new Vector2(origin.X, gridPos.Y + (i * cell) + ((cell - ImGui.GetTextLineHeight()) * 0.5f)), dimCol, HexDigits[i] + "0");
            }

            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int idx = (y * 16) + x;
                    Vector2 p0 = gridPos + new Vector2(x * cell, y * cell);
                    Vector2 p1 = p0 + new Vector2(cell - gap, cell - gap);
                    dl.AddRectFilled(p0, p1, ToU32(Local.Colours[baseIndex + idx]));
                }
            }

            // Selected / hovered outlines
            {
                int s = Selected[_layer];
                Vector2 p0 = gridPos + new Vector2((s & 15) * cell, (s >> 4) * cell);
                Vector2 p1 = p0 + new Vector2(cell - gap, cell - gap);
                dl.AddRect(p0 - new Vector2(1, 1), p1 + new Vector2(1, 1), 0xff000000, 0.0f, ImDrawFlags.None, 3.0f);
                dl.AddRect(p0 - new Vector2(1, 1), p1 + new Vector2(1, 1), 0xffffffff, 0.0f, ImDrawFlags.None, 1.5f);
            }
            if (hover >= 0)
            {
                Vector2 p0 = gridPos + new Vector2((hover & 15) * cell, (hover >> 4) * cell);
                Vector2 p1 = p0 + new Vector2(cell - gap, cell - gap);
                dl.AddRect(p0, p1, 0xff00ffff, 0.0f, ImDrawFlags.None, 1.0f);

                if (!EditorOpen && !Browser.IsOpen)
                {
                    int v = (int)Local.Colours[baseIndex + hover];
                    ImGui.BeginTooltip();
                    ImGui.ColorButton("##tip", ToVec4(v), ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new Vector2(32, 32));
                    ImGui.SameLine();
                    ImGui.TextUnformatted(Describe(hover, v) + "\nDouble click to edit");
                    ImGui.EndTooltip();
                }
            }

            // ---- Info about the selected colour ----
            ImGui.Dummy(new Vector2(0, 4));
            int sel = Selected[_layer];
            int sv = (int)Local.Colours[baseIndex + sel];
            ImGui.ColorButton("##selected", ToVec4(sv), ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new Vector2(lineH * 2.0f, lineH * 2.0f));
            ImGui.SameLine();
            ImGui.TextUnformatted(PaletteName(palette) + "\n" + Describe(sel, sv));
            if (Status.Length != 0)
            {
                // TextUnformatted - file names / exception text may contain '%'
                ImGui.PushStyleColor(ImGuiCol.Text, StatusIsError ? new Vector4(1.0f, 0.4f, 0.4f, 1.0f) : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
                ImGui.TextUnformatted(Status);
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.TextDisabled("Double click a colour to edit it. File menu to load/save.");
            }
        }

        // ------------------------------------------------------------------------------------
        private static string Describe(int _index, int _value)
        {
            return string.Format("Index {0} (${0:X2})   Value ${1:X3}\nR:{2} G:{3} B:{4}   8-bit ${5:X2}",
                                 _index, _value, (_value >> 6) & 7, (_value >> 3) & 7, _value & 7, (_value >> 1) & 0xff);
        }


        // ------------------------------------------------------------------------------------
        //  Editor
        // ------------------------------------------------------------------------------------
        private void BeginEdit(int _palette, int _index)
        {
            EditPalette = _palette;
            EditIndex = _index;
            EditOriginal = (int)Local.Colours[(_palette * 256) + _index];
            SetEditValue(EditOriginal);
            LastLiveValue = EditOriginal;
            OpenEditorRequest = true;
            EditorOpen = true;
        }

        private void SetEditValue(int _v)
        {
            EditR = (_v >> 6) & 7;
            EditG = (_v >> 3) & 7;
            EditB = _v & 7;
            EditHex = (_v & 0x1ff).ToString("X3");
        }

        private void Commit(int _value)
        {
            if (_value != LastLiveValue)
            {
                Plugin.QueueEdit(EditPalette, EditIndex, _value);
                LastLiveValue = _value;
            }
        }

        private void DrawEditor()
        {
            bool open = true;
            if (!ImGui.BeginPopupModal(EditPopupName, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
            {
                if (EditorOpen && !open)
                {
                    // Closed with the X - treat as cancel
                    Commit(EditOriginal);
                    EditorOpen = false;
                }
                return;
            }

            ImGui.TextUnformatted(string.Format("{0} - index {1} (${1:X2})", PaletteName(EditPalette), EditIndex));
            ImGui.Separator();

            int value = Make9(EditR, EditG, EditB);

            // Before / after swatches - click "Original" to revert
            ImGui.BeginGroup();
            ImGui.TextUnformatted("Original");
            if (ImGui.ColorButton("##orig", ToVec4(EditOriginal), ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new Vector2(56, 56)))
            {
                SetEditValue(EditOriginal);
                value = EditOriginal;
            }
            ImGui.TextUnformatted(string.Format("${0:X3}", EditOriginal));
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.TextUnformatted("New");
            ImGui.ColorButton("##new", ToVec4(value), ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new Vector2(56, 56));
            ImGui.TextUnformatted(string.Format("${0:X3}", value));
            ImGui.EndGroup();

            ImGui.Spacing();

            // RGB333 sliders
            bool changed = false;
            ImGui.SetNextItemWidth(200);
            changed |= ImGui.SliderInt("Red", ref EditR, 0, 7);
            ImGui.SetNextItemWidth(200);
            changed |= ImGui.SliderInt("Green", ref EditG, 0, 7);
            ImGui.SetNextItemWidth(200);
            changed |= ImGui.SliderInt("Blue", ref EditB, 0, 7);
            if (changed)
            {
                value = Make9(EditR, EditG, EditB);
                EditHex = value.ToString("X3");
            }

            // Direct 9 bit entry
            ImGui.SetNextItemWidth(60);
            if (ImGui.InputText("9-bit value (RRRGGGBBB, hex)", ref EditHex, 3, ImGuiInputTextFlags.CharsHexadecimal | ImGuiInputTextFlags.CharsUppercase))
            {
                int parsed;
                if (int.TryParse(EditHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
                {
                    parsed &= 0x1ff;
                    EditR = (parsed >> 6) & 7;
                    EditG = (parsed >> 3) & 7;
                    EditB = parsed & 7;
                    value = parsed;
                }
            }
            ImGui.TextDisabled(string.Format("8-bit (RRRGGGBB) equivalent: ${0:X2}", (value >> 1) & 0xff));

            ImGui.Checkbox("Live preview in emulator", ref EditLive);
            if (EditLive) Commit(value);

            ImGui.Separator();
            bool apply = ImGui.Button("Apply", new Vector2(90, 0)) || ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter);
            ImGui.SetItemDefaultFocus();
            ImGui.SameLine();
            bool cancel = ImGui.Button("Cancel", new Vector2(90, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape);

            if (apply)
            {
                Commit(value);
                EditorOpen = false;
                ImGui.CloseCurrentPopup();
            }
            else if (cancel)
            {
                Commit(EditOriginal);
                EditorOpen = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }
}

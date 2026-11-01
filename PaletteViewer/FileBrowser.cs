// ****************************************************************************************
//  Palette Viewer - simple Dear ImGui file browser
//
//  A modal popup used for Load and Save, drawn with ImGui so it works the same on every
//  platform CSpect runs on (no WinForms / native dialogs).
//
//  Usage (all on the ImGui thread, inside the same window / ID scope each frame):
//      browser.Open(...)          - when the menu item is picked
//      if (browser.Draw()) ...    - every frame; returns true once when the user accepts,
//                                   the chosen file is then in browser.Result
// ****************************************************************************************
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ImGuiNET;

namespace PaletteViewer
{
    internal sealed class FileBrowser
    {
        private const string PopupName = "###PaletteFileBrowser";

        public bool IsOpen { get; private set; }
        public bool IsSave { get; private set; }
        public string Result { get; private set; }
        public ePaletteFormat SaveFormat { get { return (ePaletteFormat)FormatIndex; } }

        private string Title = "";
        private string Directory = "";
        private string DirectoryEdit = "";
        private string FileName = "";
        private string Error = "";
        private string ConfirmOverwrite;    // path waiting for a second "Save" press
        private bool ShowAll;
        private int FormatIndex;
        private bool RequestOpen;
        private bool ScrollToSelected;

        private readonly List<string> Dirs = new List<string>();
        private readonly List<string> Files = new List<string>();


        // ------------------------------------------------------------------------------------
        public FileBrowser()
        {
            Directory = SafeDirectory(Environment.CurrentDirectory);
        }

        // ------------------------------------------------------------------------------------
        /// <summary>Start a Load or Save. _startFile can be a full path or just a name.</summary>
        // ------------------------------------------------------------------------------------
        public void Open(bool _save, string _title, string _startFile)
        {
            IsSave = _save;
            Title = _title;
            Result = null;
            Error = "";
            ConfirmOverwrite = null;

            if (!string.IsNullOrEmpty(_startFile))
            {
                string dir = null;
                try { dir = Path.GetDirectoryName(_startFile); } catch (Exception) { }
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir)) Directory = dir;
                FileName = Path.GetFileName(_startFile);
            }
            if (!_save && !string.IsNullOrEmpty(FileName) && !File.Exists(Path.Combine(Directory, FileName)))
            {
                FileName = "";
            }

            Refresh();
            RequestOpen = true;
            IsOpen = true;
            ScrollToSelected = true;
        }

        // ------------------------------------------------------------------------------------
        private static string SafeDirectory(string _dir)
        {
            try
            {
                if (!string.IsNullOrEmpty(_dir) && System.IO.Directory.Exists(_dir)) return Path.GetFullPath(_dir);
            }
            catch (Exception) { }
            return Path.GetPathRoot(Environment.CurrentDirectory);
        }

        // ------------------------------------------------------------------------------------
        private void Navigate(string _dir)
        {
            try
            {
                string full = Path.GetFullPath(_dir);
                if (!System.IO.Directory.Exists(full))
                {
                    Error = "Folder not found: " + full;
                    return;
                }
                Directory = full;
                ConfirmOverwrite = null;
                Refresh();
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
        }

        // ------------------------------------------------------------------------------------
        private void Refresh()
        {
            Dirs.Clear();
            Files.Clear();
            DirectoryEdit = Directory;
            try
            {
                foreach (string d in System.IO.Directory.GetDirectories(Directory))
                {
                    string name = Path.GetFileName(d);
                    if (name.StartsWith(".")) continue;
                    Dirs.Add(name);
                }
                foreach (string f in System.IO.Directory.GetFiles(Directory))
                {
                    if (!ShowAll && !IsPaletteFile(f)) continue;
                    Files.Add(Path.GetFileName(f));
                }
                Dirs.Sort(StringComparer.OrdinalIgnoreCase);
                Files.Sort(StringComparer.OrdinalIgnoreCase);
                Error = "";
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
        }

        private static bool IsPaletteFile(string _path)
        {
            string ext = Path.GetExtension(_path).ToLowerInvariant();
            return Array.IndexOf(PaletteFile.KnownExtensions, ext) >= 0;
        }


        // ------------------------------------------------------------------------------------
        /// <summary>Draw the browser. Returns true once, when a file has been chosen (see Result).</summary>
        // ------------------------------------------------------------------------------------
        public bool Draw()
        {
            if (RequestOpen)
            {
                RequestOpen = false;
                ImGui.OpenPopup(Title + PopupName);
            }
            if (!IsOpen) return false;

            Vector2 display = ImGui.GetIO().DisplaySize;
            ImGui.SetNextWindowSize(new Vector2(Math.Min(display.X - 20, 520), Math.Min(display.Y - 20, 440)), ImGuiCond.Appearing);
            ImGui.SetNextWindowPos(display * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

            bool open = true;
            if (!ImGui.BeginPopupModal(Title + PopupName, ref open, ImGuiWindowFlags.NoSavedSettings))
            {
                IsOpen = false;     // closed with the X
                return false;
            }

            bool accepted = false;
            string navigateTo = null;

            // ---- Drive / up / path ----
            if (Path.DirectorySeparatorChar == '\\')
            {
                string root = Path.GetPathRoot(Directory) ?? "";
                ImGui.SetNextItemWidth(60);
                if (ImGui.BeginCombo("##drive", root.TrimEnd('\\')))
                {
                    foreach (DriveInfo d in DriveInfo.GetDrives())
                    {
                        bool ready = false;
                        try { ready = d.IsReady; } catch (Exception) { }
                        if (!ready) continue;
                        if (ImGui.Selectable(d.Name.TrimEnd('\\'), string.Equals(d.Name, root, StringComparison.OrdinalIgnoreCase)))
                        {
                            navigateTo = d.RootDirectory.FullName;
                        }
                    }
                    ImGui.EndCombo();
                }
                ImGui.SameLine();
            }
            if (ImGui.Button("Up"))
            {
                DirectoryInfo parent = System.IO.Directory.GetParent(Directory);
                if (parent != null) navigateTo = parent.FullName;
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##path", ref DirectoryEdit, 1024, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                navigateTo = DirectoryEdit;
            }

            // ---- Listing ----
            float footer = (ImGui.GetFrameHeightWithSpacing() * (IsSave ? 4.0f : 3.0f)) + ImGui.GetTextLineHeightWithSpacing();
            if (ImGui.BeginChild("##files", new Vector2(0, -footer), ImGuiChildFlags.Borders))
            {
                ImGuiSelectableFlags sf = ImGuiSelectableFlags.AllowDoubleClick | ImGuiSelectableFlags.NoAutoClosePopups;
                Vector4 dirCol = new Vector4(1.0f, 0.85f, 0.4f, 1.0f);

                ImGui.PushStyleColor(ImGuiCol.Text, dirCol);
                if (ImGui.Selectable("[..]", false, sf) && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    DirectoryInfo parent = System.IO.Directory.GetParent(Directory);
                    if (parent != null) navigateTo = parent.FullName;
                }
                foreach (string d in Dirs)
                {
                    if (ImGui.Selectable("[" + d + "]", false, sf) && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        navigateTo = Path.Combine(Directory, d);
                    }
                }
                ImGui.PopStyleColor();

                foreach (string f in Files)
                {
                    bool selected = string.Equals(f, FileName, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(f, selected, sf))
                    {
                        FileName = f;
                        ConfirmOverwrite = null;
                        if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) accepted = true;
                    }
                    if (selected && ScrollToSelected) ImGui.SetScrollHereY();
                }
                ScrollToSelected = false;
            }
            ImGui.EndChild();

            // ---- File name / options ----
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("File name");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("##filename", ref FileName, 260, ImGuiInputTextFlags.EnterReturnsTrue)) accepted = true;

            if (IsSave)
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted("Format   ");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(-1);
                int fi = FormatIndex;
                if (ImGui.Combo("##format", ref fi, PaletteFile.FormatNames, PaletteFile.FormatNames.Length))
                {
                    FormatIndex = fi;
                    ConfirmOverwrite = null;
                }
            }

            if (ImGui.Checkbox("Show all files", ref ShowAll)) Refresh();
            if (!IsSave)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(512, 256 or 768 byte files)");
            }

            // TextUnformatted throughout - paths and exception text may contain '%'
            if (Error.Length != 0)
                ColouredText(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), Error);
            else if (ConfirmOverwrite != null)
                ColouredText(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), Path.GetFileName(ConfirmOverwrite) + " already exists. Press Overwrite to replace it.");
            else
                ImGui.TextUnformatted("");

            // ---- Buttons ----
            string okLabel = IsSave ? (ConfirmOverwrite != null ? "Overwrite" : "Save") : "Load";
            if (ImGui.Button(okLabel, new Vector2(90, 0))) accepted = true;
            ImGui.SameLine();
            bool cancel = ImGui.Button("Cancel", new Vector2(90, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape);

            if (navigateTo != null) Navigate(navigateTo);

            bool done = false;
            if (cancel)
            {
                IsOpen = false;
                ImGui.CloseCurrentPopup();
            }
            else if (accepted && navigateTo == null)
            {
                done = TryAccept();
                if (done)
                {
                    IsOpen = false;
                    ImGui.CloseCurrentPopup();
                }
            }

            ImGui.EndPopup();
            return done;
        }

        // ------------------------------------------------------------------------------------
        private static void ColouredText(Vector4 _col, string _text)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, _col);
            ImGui.TextUnformatted(_text);
            ImGui.PopStyleColor();
        }

        // ------------------------------------------------------------------------------------
        private bool TryAccept()
        {
            string name = (FileName ?? "").Trim();
            if (name.Length == 0)
            {
                Error = "Choose or type a file name.";
                return false;
            }

            string path;
            try
            {
                path = Path.GetFullPath(Path.IsPathRooted(name) ? name : Path.Combine(Directory, name));
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                return false;
            }

            // Typed the name of a folder - go into it instead
            if (System.IO.Directory.Exists(path))
            {
                Navigate(path);
                FileName = "";
                return false;
            }

            if (IsSave)
            {
                if (Path.GetExtension(path).Length == 0) path += PaletteFile.FormatExtensions[FormatIndex];
                if (File.Exists(path) && !string.Equals(path, ConfirmOverwrite, StringComparison.OrdinalIgnoreCase))
                {
                    ConfirmOverwrite = path;
                    Error = "";
                    return false;
                }
            }
            else if (!File.Exists(path))
            {
                Error = "File not found: " + Path.GetFileName(path);
                return false;
            }

            Result = path;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory = dir;
            }
            catch (Exception) { }
            FileName = Path.GetFileName(path);
            ConfirmOverwrite = null;
            return true;
        }
    }
}

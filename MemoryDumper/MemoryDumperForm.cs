using Plugin;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace MemoryDumper
{
    /// <summary>
    /// Tiny dialog: symbol dropdown + address + length + optional bank +
    /// filename + Save. Built programmatically -- no .resx / Designer file --
    /// so the whole plugin is just three .cs files.
    ///
    /// Map-file symbol picker: loads a z88dk-style .map (the format used by
    /// soccer-nes / most NextZXOS C projects), parses lines like
    ///     "_g_state    = $B2F3 ; addr, public, , module, section, source"
    /// and exposes them in a searchable combobox. Selecting a symbol fills
    /// in Address (and Bank when the section is BANK_XX / PAGE_XX, and
    /// Length when the next symbol's address gives a sensible upper bound).
    /// </summary>
    public class MemoryDumperForm : Form
    {
        private readonly MemoryDumperPlugin plugin;

        // -- Symbol-table state --
        private class MapSymbol
        {
            public string Name;       // e.g. "_g_state"
            public int    Address;    // Z80 16-bit address (0..0xFFFF)
            public int    Bank;       // 8K bank #, or -1 if non-banked
            public int    Size;       // inferred from next-symbol-in-section gap
            public string Section;    // for tooltip / status display
        }
        private List<MapSymbol> symbols = new List<MapSymbol>();

        // -- Controls --
        private TextBox  txtMapPath;
        private Button   btnMapBrowse;
        private Button   btnMapLoad;
        private ComboBox cboSymbol;
        private TextBox  txtAddress;
        private TextBox  txtLength;
        private TextBox  txtBank;
        private TextBox  txtFile;
        private Button   btnBrowse;
        private Button   btnSave;
        private Button   btnClose;
        private Label    lblStatus;

        public MemoryDumperForm(MemoryDumperPlugin _plugin)
        {
            plugin = _plugin;
            BuildUi();
            // Restore the last map path used in this CSpect session, or
            // auto-detect via CSpect.LoadFile() (same approach as the
            // Profiler plugin -- finds the .map next to the loaded .nex
            // even when CSpect's working dir is the emulator install rather
            // than the project bin/).
            if (!string.IsNullOrEmpty(plugin.LastMapPath) && File.Exists(plugin.LastMapPath))
            {
                txtMapPath.Text = plugin.LastMapPath;
                LoadMap();
            }
            else
            {
                AutoLoadMap();
            }
        }

        // ----------------------------------------------------------------
        //  UI
        // ----------------------------------------------------------------
        private void BuildUi()
        {
            Text = "Memory Dumper";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 310);
            Font = new Font("Segoe UI", 9F);

            int y = 12;
            const int LblX = 12, FieldX = 110, FieldW = 220, RowH = 28;

            // -- Map file ------------------------------------------------
            Controls.Add(new Label {
                Text = "Map file:", Location = new Point(LblX, y + 3), AutoSize = true
            });
            txtMapPath = new TextBox {
                Location = new Point(FieldX, y), Width = 220
            };
            btnMapBrowse = new Button {
                Text = "...", Location = new Point(FieldX + 225, y - 2),
                Width = 30, Height = 23
            };
            btnMapBrowse.Click += BtnMapBrowse_Click;
            btnMapLoad = new Button {
                Text = "Load", Location = new Point(FieldX + 260, y - 2),
                Width = 60, Height = 23
            };
            btnMapLoad.Click += (s, e) => LoadMap();
            Controls.AddRange(new Control[] { txtMapPath, btnMapBrowse, btnMapLoad });
            y += RowH;

            // -- Symbol dropdown ----------------------------------------
            Controls.Add(new Label {
                Text = "Symbol:", Location = new Point(LblX, y + 3), AutoSize = true
            });
            cboSymbol = new ComboBox {
                Location = new Point(FieldX, y),
                Width = 320,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                DropDownStyle = ComboBoxStyle.DropDown,
                MaxDropDownItems = 20
            };
            // Two handlers: dropdown click (SelectedIndexChanged) AND
            // free typing (TextChanged). The text handler also catches
            // autocomplete completions, so the user can just type
            // "g_state" + Enter without ever opening the dropdown.
            cboSymbol.SelectedIndexChanged += (s, e) => TryApplySymbol(cboSymbol.Text);
            cboSymbol.TextChanged          += (s, e) => TryApplySymbol(cboSymbol.Text);
            Controls.Add(cboSymbol);
            y += RowH;

            // -- Address -------------------------------------------------
            Controls.Add(new Label {
                Text = "Address (hex):", Location = new Point(LblX, y + 3), AutoSize = true
            });
            txtAddress = new TextBox {
                Text = "B2F3", Location = new Point(FieldX, y), Width = 100
            };
            Controls.Add(txtAddress);
            Controls.Add(new Label {
                Text = "B2F3, 0xB2F3, $B2F3",
                Location = new Point(FieldX + 110, y + 3),
                AutoSize = true, ForeColor = SystemColors.GrayText
            });
            y += RowH;

            // -- Length --------------------------------------------------
            Controls.Add(new Label {
                Text = "Length (bytes):", Location = new Point(LblX, y + 3), AutoSize = true
            });
            txtLength = new TextBox {
                Text = "1024", Location = new Point(FieldX, y), Width = 100
            };
            Controls.Add(txtLength);
            Controls.Add(new Label {
                Text = "decimal, or 0x400 for hex",
                Location = new Point(FieldX + 110, y + 3),
                AutoSize = true, ForeColor = SystemColors.GrayText
            });
            y += RowH;

            // -- Bank ----------------------------------------------------
            Controls.Add(new Label {
                Text = "Bank (optional):", Location = new Point(LblX, y + 3), AutoSize = true
            });
            txtBank = new TextBox {
                Text = "", Location = new Point(FieldX, y), Width = 100
            };
            Controls.Add(txtBank);
            Controls.Add(new Label {
                Text = "8K bank #; empty = current MMU",
                Location = new Point(FieldX + 110, y + 3),
                AutoSize = true, ForeColor = SystemColors.GrayText
            });
            y += RowH;

            // -- File ----------------------------------------------------
            Controls.Add(new Label {
                Text = "Save to:", Location = new Point(LblX, y + 3), AutoSize = true
            });
            txtFile = new TextBox {
                Text = DefaultFilename(), Location = new Point(FieldX, y), Width = 220
            };
            btnBrowse = new Button {
                Text = "...", Location = new Point(FieldX + 225, y - 2),
                Width = 30, Height = 23
            };
            btnBrowse.Click += BtnBrowse_Click;
            Controls.AddRange(new Control[] { txtFile, btnBrowse });
            y += RowH;

            // -- Buttons -------------------------------------------------
            y += 4;
            btnSave = new Button {
                Text = "Save", Location = new Point(FieldX, y), Width = 100, Height = 32
            };
            btnSave.Click += BtnSave_Click;
            btnClose = new Button {
                Text = "Close", Location = new Point(FieldX + 110, y), Width = 100, Height = 32
            };
            btnClose.Click += (s, e) => Hide();
            Controls.AddRange(new Control[] { btnSave, btnClose });
            y += 38;

            // -- Status --------------------------------------------------
            lblStatus = new Label {
                Text = "Load a .map file to populate the symbol list.",
                Location = new Point(LblX, y),
                Width = ClientSize.Width - 2 * LblX,
                Height = 32,
                ForeColor = SystemColors.ControlText
            };
            Controls.Add(lblStatus);

            AcceptButton = btnSave;
        }

        /// <summary>
        /// Default save directory for dumps -- the directory of the loaded
        /// .nex (so dumps live next to the project's binary, not in CSpect's
        /// install dir). Falls back to current dir if no .nex is loaded.
        /// </summary>
        private string GetNexDirectory()
        {
            if (plugin == null || plugin.CSpect == null)
                return Environment.CurrentDirectory;
            string nexFile = "";
            try
            {
                object fn = plugin.CSpect.GetGlobal(eGlobal.file_name);
                if (fn is string) nexFile = (string)fn;
            }
            catch { }
            if (string.IsNullOrEmpty(nexFile)) return Environment.CurrentDirectory;
            try
            {
                string dir = Path.GetDirectoryName(nexFile);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
            }
            catch { }
            return Environment.CurrentDirectory;
        }

        private string DefaultFilename()
        {
            return Path.Combine(GetNexDirectory(), "memdump.bin");
        }

        // ----------------------------------------------------------------
        //  Map file
        // ----------------------------------------------------------------
        private void BtnMapBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Map files (*.map)|*.map|All files (*.*)|*.*";
                dlg.Title  = "Select z88dk map file";
                if (!string.IsNullOrEmpty(txtMapPath.Text))
                {
                    try { dlg.InitialDirectory = Path.GetDirectoryName(txtMapPath.Text); }
                    catch { }
                }
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    txtMapPath.Text = dlg.FileName;
                    LoadMap();
                }
            }
        }

        /// <summary>
        /// Parse z88dk map file into <see cref="symbols"/>. Format is one
        /// symbol per line, e.g.
        ///     _g_state   = $B2F3 ; addr, public, , module, section, source
        /// We keep ALL "addr" entries (data + code, public + local) so the
        /// user can dump variables, structs, code regions -- all of it. We
        /// skip "const" entries (compile-time constants, no memory).
        ///
        /// Sections we recognize:
        ///   data_*, bss_*, code_*, CODE   -- non-banked, addr is direct Z80
        ///   PAGE_NN                       -- 8K bank NN
        ///   BANK_NN                       -- 16K bank NN (= 8K banks 2N..2N+1)
        ///
        /// After parsing we sort by address within each section to infer
        /// each symbol's Size from the gap to the next neighbour.
        /// </summary>
        /// <summary>
        /// Try to find and load the .map file next to the currently loaded
        /// .nex without any user input. Mirrors Profiler.GetSymbols(): asks
        /// CSpect for the loaded file's name, swaps the extension, and goes
        /// through CSpect.LoadFile() which knows how to resolve paths the
        /// same way the emulator did. Silently fails if no .nex is loaded
        /// or no .map is found -- the user can still browse manually.
        /// </summary>
        private void AutoLoadMap()
        {
            if (plugin == null || plugin.CSpect == null) return;

            string fileName = "";
            try
            {
                object fn = plugin.CSpect.GetGlobal(eGlobal.file_name);
                if (fn is string) fileName = (string)fn;
            }
            catch { }

            if (string.IsNullOrEmpty(fileName)) return;

            string mapName = Path.ChangeExtension(fileName, ".map");
            byte[] mapBytes = null;
            try { mapBytes = plugin.CSpect.LoadFile(mapName); } catch { }

            // Some CSpect versions strip leading "./" -- try the bare name too.
            if (mapBytes == null)
            {
                string trimmed = mapName.TrimStart('.', '\\', '/');
                if (trimmed != mapName)
                {
                    try { mapBytes = plugin.CSpect.LoadFile(trimmed); } catch { }
                }
            }

            if (mapBytes == null) return;

            string[] lines = System.Text.Encoding.ASCII.GetString(mapBytes)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // Show the resolved name in the textbox so the user knows what
            // was loaded, even though we never opened it from a real path.
            txtMapPath.Text = mapName;
            plugin.LastMapPath = mapName;
            ParseMapLines(lines, mapName);
        }

        private void LoadMap()
        {
            string path = txtMapPath.Text.Trim();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                SetStatus("Map file not found: " + path, true);
                return;
            }

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception ex) { SetStatus("Read error: " + ex.Message, true); return; }

            plugin.LastMapPath = path;
            ParseMapLines(lines, path);
        }

        /// <summary>
        /// Parse z88dk map text into <see cref="symbols"/> and refresh the
        /// dropdown. Shared by manual file load and CSpect-LoadFile auto-load.
        /// </summary>
        private void ParseMapLines(string[] lines, string sourceDesc)
        {
            var fresh = new List<MapSymbol>();
            int skippedConst = 0;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                int eq = trimmed.IndexOf('=');
                if (eq < 0) continue;

                string name = trimmed.Substring(0, eq).Trim();
                string rest = trimmed.Substring(eq + 1).Trim();

                int semi = rest.IndexOf(';');
                string addrStr = (semi >= 0 ? rest.Substring(0, semi) : rest).Trim().TrimStart('$');
                string meta    = (semi >= 0 ? rest.Substring(semi + 1) : "").Trim();

                int addr;
                if (!int.TryParse(addrStr, NumberStyles.HexNumber,
                                  CultureInfo.InvariantCulture, out addr))
                    continue;

                if (meta.StartsWith("const")) { skippedConst++; continue; }
                if (!meta.StartsWith("addr")) continue;

                // Metadata: "addr, scope, , module, section, source"
                string[] parts = meta.Split(',');
                string section = parts.Length >= 5 ? parts[4].Trim() : "";

                int bank = -1;
                if (section.StartsWith("PAGE_"))
                {
                    int p;
                    if (int.TryParse(section.Substring(5), out p)) bank = p;
                }
                else if (section.StartsWith("BANK_"))
                {
                    // 16K bank N occupies 8K banks 2N and 2N+1. We use the
                    // first 8K bank as the default; if the symbol's address
                    // is in the second half (>=$2000 within the 16K), use 2N+1.
                    int b;
                    if (int.TryParse(section.Substring(5), out b))
                    {
                        bank = b * 2 + ((addr & 0x2000) != 0 ? 1 : 0);
                    }
                }

                fresh.Add(new MapSymbol {
                    Name = name, Address = addr, Bank = bank,
                    Size = 0, Section = section
                });
            }

            // Infer size from next-neighbour gap WITHIN the same section,
            // capped at 4096 bytes so weird gaps don't suggest absurd dumps.
            // For the last symbol in a section, default to 16 bytes.
            const int SizeCap = 4096;
            const int SizeDefault = 16;
            var bySection = fresh.GroupBy(s => s.Section);
            foreach (var grp in bySection)
            {
                var sorted = grp.OrderBy(s => s.Address).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    int gap = (i + 1 < sorted.Count)
                        ? sorted[i + 1].Address - sorted[i].Address
                        : SizeDefault;
                    if (gap <= 0)        gap = SizeDefault; // duplicate addr
                    else if (gap > SizeCap) gap = SizeCap;
                    sorted[i].Size = gap;
                }
            }

            symbols = fresh.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
            cboSymbol.Items.Clear();
            // Display names without the leading "_" that z88dk's C linkage
            // adds -- users type "g_state", not "_g_state". TryApplySymbol
            // handles both forms when looking up the underlying MapSymbol.
            cboSymbol.Items.AddRange(symbols
                .Select(s => (object)s.Name.TrimStart('_'))
                .Distinct()
                .ToArray());

            SetStatus(
                "Loaded " + symbols.Count + " symbols from " + Path.GetFileName(sourceDesc)
                + (skippedConst > 0 ? " (skipped " + skippedConst + " consts)" : ""),
                false);
        }

        /// <summary>
        /// If the given name (typed or selected) matches a symbol in the
        /// loaded map, populate Address / Length / Bank / Save-to fields.
        /// No-op if there's no exact match -- avoids stomping the fields
        /// while the user is mid-typing a partial name.
        ///
        /// Match is tolerant of leading underscores (z88dk's C name mangling
        /// adds "_" to every symbol; users tend to type the unprefixed name)
        /// and case.
        /// </summary>
        private void TryApplySymbol(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            string trimmed = name.Trim();
            if (trimmed.Length == 0) return;

            MapSymbol sym = symbols.FirstOrDefault(s =>
                string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name.TrimStart('_'), trimmed, StringComparison.OrdinalIgnoreCase));
            if (sym == null) return;

            txtAddress.Text = sym.Address.ToString("X4");
            txtLength.Text  = sym.Size.ToString();
            txtBank.Text    = sym.Bank >= 0 ? sym.Bank.ToString() : "";

            // Default the dump filename to "<symbol>.bin" next to the .nex.
            // Strip the leading "_" that z88dk adds for C linkage so files
            // are named the way the source code refers to them.
            string fileBase = sym.Name.TrimStart('_');
            txtFile.Text = Path.Combine(GetNexDirectory(), fileBase + ".bin");

            SetStatus(
                "Selected " + sym.Name
                + " @ $" + sym.Address.ToString("X4")
                + (sym.Bank >= 0 ? " (8K bank " + sym.Bank + ", section " + sym.Section + ")"
                                 : " (section " + sym.Section + ")")
                + ", size " + sym.Size + " (inferred)",
                false);
        }

        // ----------------------------------------------------------------
        //  Dump
        // ----------------------------------------------------------------
        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "Binary file (*.bin)|*.bin|All files (*.*)|*.*";
                dlg.FileName = Path.GetFileName(txtFile.Text);
                try { dlg.InitialDirectory = Path.GetDirectoryName(txtFile.Text); } catch { }
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    txtFile.Text = dlg.FileName;
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                int addr;
                if (!TryParseHex(txtAddress.Text, out addr) || addr < 0 || addr > 0xFFFF)
                { SetStatus("Address must be hex 0000..FFFF", true); return; }

                int length;
                if (!TryParseInt(txtLength.Text, out length) || length <= 0)
                { SetStatus("Length must be a positive integer (decimal or 0x...)", true); return; }

                string path = txtFile.Text.Trim();
                if (string.IsNullOrEmpty(path))
                { SetStatus("Filename cannot be empty", true); return; }

                byte[] data;
                string source;
                string bankText = txtBank.Text.Trim();
                if (bankText.Length == 0)
                {
                    int max = 0x10000 - addr;
                    if (length > max) length = max;
                    data = plugin.CSpect.Peek((ushort)addr, length, null);
                    source = "Z80 $" + addr.ToString("X4")
                        + "..$" + ((addr + length - 1) & 0xFFFF).ToString("X4");
                }
                else
                {
                    int bank;
                    if (!TryParseInt(bankText, out bank) || bank < 0 || bank > 255)
                    { SetStatus("Bank must be a decimal 8K bank 0..255", true); return; }
                    int offsetIn8K = addr & 0x1FFF;
                    int physical   = bank * 0x2000 + offsetIn8K;
                    data = plugin.CSpect.PeekPhysical(physical, length, null);
                    source = "physical bank " + bank + " ($" + physical.ToString("X6")
                        + "..$" + (physical + length - 1).ToString("X6") + ")";
                }

                File.WriteAllBytes(path, data);
                SetStatus("Wrote " + data.Length + " bytes from " + source + " -> " + path, false);
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message, true);
            }
        }

        // ----------------------------------------------------------------
        //  Helpers
        // ----------------------------------------------------------------
        private void SetStatus(string message, bool isError)
        {
            lblStatus.Text = message;
            lblStatus.ForeColor = isError ? Color.Firebrick : Color.DarkGreen;
        }

        /// Always treat as hex regardless of prefix.
        private static bool TryParseHex(string s, out int value)
        {
            value = 0;
            if (s == null) return false;
            string t = s.Trim();
            if (t.Length == 0) return false;
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            else if (t.StartsWith("$") || t.StartsWith("#")) t = t.Substring(1);
            else if (t.StartsWith("&h", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            return int.TryParse(t, NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out value);
        }

        /// Decimal default; "0x..." / "$..." / "&h..." for hex.
        private static bool TryParseInt(string s, out int value)
        {
            value = 0;
            if (s == null) return false;
            string t = s.Trim();
            if (t.Length == 0) return false;
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(t.Substring(2), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out value);
            if (t.StartsWith("$") || t.StartsWith("#"))
                return int.TryParse(t.Substring(1), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out value);
            if (t.StartsWith("&h", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(t.Substring(2), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out value);
            return int.TryParse(t, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out value);
        }
    }
}

// ********************************************************************************************************************************************
//      CSpect Profiler extension - Dear ImGui view
//      Written by:
//                  Mike Dailly
//
//      contributions by:
//
//      Released under the GNU 3 license - please see license file for more details
//
//      Start/Stop profiling, a "Profile Overview" bar graph of execution counts across memory, and a heat-coloured
//      disassembly. Right drag in the overview to zoom into a region, left click/drag to pick the disassembly start.
//
// ********************************************************************************************************************************************
using System;
using System.Numerics;
using ImGuiNET;
using Plugin;

namespace Profiler
{
    public class ProfilerView
    {
        /// <summary>Left margin of the graph inside the overview</summary>
        public const int PROF_X_START = 32;
        /// <summary>Right margin of the graph inside the overview</summary>
        public const int PROF_X_END = 32;
        /// <summary>Height of the bars area</summary>
        const float GRAPH_HEIGHT = 200.0f;
        /// <summary>Height of the whole overview (bars + axis + labels)</summary>
        const float OVERVIEW_HEIGHT = 240.0f;
        /// <summary>Size of the profiled memory</summary>
        const int MEMORY_SIZE = 2 * 1024 * 1024;

        /// <summary>A copy of the profile samples (the live array while profiling)</summary>
        public int[] ProfileRead;
        /// <summary>Profiling in progress?</summary>
        bool Running = false;

        /// <summary>The start address of the current sample area</summary>
        int StartAddress = 0;
        /// <summary>The end address of the current sample area</summary>
        int EndAddress = MEMORY_SIZE;
        /// <summary>The number of cells in the sample area</summary>
        int NumCells = -1;

        /// <summary>Profile count for this cell</summary>
        int[] Blocks;
        /// <summary>Start Address of each cell</summary>
        int[] BlockAdd;
        /// <summary>Number of addresses in each cell</summary>
        double CellSize = 1;
        /// <summary>The largest profile value the currently selected area</summary>
        int max_value = 0;
        /// <summary>The largest profile value in ALL of memory</summary>
        int max_sample = 0;

        /// <summary>Disassembly start address (-1 = none)</summary>
        int DissStart = -1;

        /// <summary>Right-drag selection in progress</summary>
        bool Selecting = false;
        int SelStartX;

        // cell cache
        int CacheStart = -1, CacheEnd = -1, CacheCells = -1, CacheVersion = -1;
        int DataVersion = 0;
        int FrameCount = 0;

        /// <summary>heat map colours (RGB)</summary>
        static readonly int[] PerfColours ={
            0,
            0xFFF9AF,
            0xFFE693,
            0xFFC9A8,
            0xFFBAC5,
            0xFF6D68
        };

        static readonly uint ColBack = Col(0x101418);
        static readonly uint ColAxis = Col(0x909090);
        static readonly uint ColText = Col(0xC8C8C8);
        static readonly uint ColBar = Col(0x6888B0);
        static readonly uint ColSelect = Col(0xFF3030, 90);
        static readonly uint ColDiss = Col(0xFFE000);
        static readonly Vector4 ColBlackText = new Vector4(0, 0, 0, 1);
        static readonly Vector4 ColRunning = new Vector4(0.4f, 1.0f, 0.4f, 1.0f);

        /// <summary>CSpect callback interface</summary>
        iCSpect CSpect;

        // ******************************************************************************************
        /// <summary>
        ///     Create profiler view
        /// </summary>
        // ******************************************************************************************
        public ProfilerView(iCSpect _CSpect)
        {
            CSpect = _CSpect;
        }

        /// <summary>RGB (0xRRGGBB) + alpha -> ImGui packed colour</summary>
        static uint Col(int _rgb, int _a = 255)
        {
            return (uint)((_a << 24) | ((_rgb & 0xff) << 16) | (_rgb & 0xff00) | ((_rgb >> 16) & 0xff));
        }

        #region Actions
        // ************************************************************************************************************************
        /// <summary>
        ///     START profiling - clear the emulator's profile counts and watch them live
        /// </summary>
        // ************************************************************************************************************************
        void StartProfiling()
        {
            ProfileRead = (int[])CSpect.GetGlobal(eGlobal.profile_read);
            for (int i = 0; i < ProfileRead.Length; i++)
            {
                ProfileRead[i] = 0;
            }
            Running = true;
            DataVersion++;
            ResetZoom();
        }

        // ************************************************************************************************************************
        /// <summary>
        ///     Stop profiling - copy the profile data (the emulator keeps accumulating into its own array)
        /// </summary>
        // ************************************************************************************************************************
        void StopProfiling()
        {
            Running = false;
            max_sample = 0;

            int[] localProfileRead = (int[])CSpect.GetGlobal(eGlobal.profile_read);
            ProfileRead = new int[localProfileRead.Length];
            for (int i = 0; i < ProfileRead.Length; i++)
            {
                ProfileRead[i] = localProfileRead[i];
                if (max_sample < ProfileRead[i]) max_sample = ProfileRead[i];
            }
            DataVersion++;
            ResetZoom();
        }

        // ******************************************************************************************
        /// <summary>
        ///     Reset the "zoom" on the profile window
        /// </summary>
        // ******************************************************************************************
        void ResetZoom()
        {
            DissStart = -1;
            StartAddress = 0;
            EndAddress = (ProfileRead != null) ? Math.Min(MEMORY_SIZE, ProfileRead.Length) : MEMORY_SIZE;
            Selecting = false;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Zoom into the cells between two graph X positions
        /// </summary>
        // ******************************************************************************************
        void Zoom(int _x1, int _x2)
        {
            if (_x1 > _x2) { int t = _x1; _x1 = _x2; _x2 = t; }
            if (_x1 == _x2) return;

            int start = BlockAdd[_x1];
            int end = (_x2 >= NumCells - 1) ? EndAddress : BlockAdd[_x2 + 1];
            if (end - start < 2) return;

            DissStart = -1;
            StartAddress = start;
            EndAddress = end;
        }
        #endregion

        #region Cells
        // *******************************************************************************************************************
        /// <summary>
        ///     Fill the cells by scaling the memory range into the cells.
        /// </summary>
        // *******************************************************************************************************************
        void FillCells(int _numCells)
        {
            NumCells = _numCells;
            if (Blocks == null || Blocks.Length != NumCells + 1)
            {
                Blocks = new int[NumCells + 1];
                BlockAdd = new int[NumCells + 1];
            }

            int len = ProfileRead.Length;
            max_value = 0;
            CellSize = ((EndAddress - StartAddress) / (double)NumCells);
            double Add = StartAddress;
            for (int i = 0; i < NumCells; i++)
            {
                int a = (int)Add;
                long v;
                if (CellSize > 1)
                {
                    v = 0;
                    int e = Math.Min(a + (int)CellSize, len);
                    for (int b = a; b < e; b++)
                    {
                        v += ProfileRead[b];
                    }
                    v = (long)(v / CellSize);       // average cell data
                }
                else
                {
                    v = (a < len) ? ProfileRead[a] : 0;
                }
                if (v > int.MaxValue) v = int.MaxValue;
                if (max_value < v) max_value = (int)v;
                Blocks[i] = (int)v;
                BlockAdd[i] = a;
                Add += CellSize;
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Recalculate the cells only when something changed (or every few frames while live)
        /// </summary>
        // ******************************************************************************************
        void UpdateCells(int _numCells)
        {
            bool stale = CacheStart != StartAddress || CacheEnd != EndAddress || CacheCells != _numCells || CacheVersion != DataVersion;
            if (!stale && !(Running && (FrameCount % 6) == 0)) return;

            FillCells(_numCells);
            CacheStart = StartAddress;
            CacheEnd = EndAddress;
            CacheCells = _numCells;
            CacheVersion = DataVersion;
        }

        /// <summary>Heat colour index (0 = cold) for a profile count</summary>
        int HeatIndex(int _prof)
        {
            if (max_value <= 0) return 0;
            double d = _prof * (200.0 / max_value);
            double col_scaler = (((double)PerfColours.Length - 1) + 0.5) / 200.0;
            int idx = (int)Math.Round(d * col_scaler);
            if (idx < 0) idx = 0;
            if (idx >= PerfColours.Length) idx = PerfColours.Length - 1;
            return idx;
        }

        // ****************************************************************************************************************
        /// <summary>
        ///     Find the address in the blocks, and colour it based on hits
        /// </summary>
        /// <returns>packed colour, or 0 for none</returns>
        // ****************************************************************************************************************
        uint GetColour(int _address)
        {
            if (Blocks == null || NumCells <= 0) return 0;
            int index = _address - StartAddress;
            if (index < 0) return 0;

            index = (int)Math.Ceiling(index / CellSize);
            if (index >= NumCells) return 0;

            int col = PerfColours[HeatIndex(Blocks[index])];
            if (col == 0) return 0;
            return Col(col);
        }
        #endregion

        #region Drawing
        // ******************************************************************************************
        /// <summary>
        ///     iWindow ImGui draw callback - one panel filling the OS window
        /// </summary>
        // ******************************************************************************************
        public void Draw()
        {
            FrameCount++;
            ImGuiIOPtr io = ImGui.GetIO();
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(io.DisplaySize);
            ImGuiWindowFlags wflags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                                      ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus;
            bool open = true;                               // no close button (NoDecoration) - the OS window closes it
            if (ImGui.Begin("##Profiler", ref open, wflags))
            {
                // ---- toolbar ----
                ImGui.BeginDisabled(Running);
                if (ImGui.Button("Start")) StartProfiling();
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.BeginDisabled(!Running);
                if (ImGui.Button("Stop")) StopProfiling();
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.BeginDisabled(ProfileRead == null);
                if (ImGui.Button("Reset zoom")) ResetZoom();
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.TextUnformatted(string.Format("   Range ${0:X6} - ${1:X6}", StartAddress, EndAddress));
                if (Running)
                {
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, ColRunning);
                    ImGui.TextUnformatted("   Profiling...");
                    ImGui.PopStyleColor();
                }

                // ---- overview ----
                ImGui.SeparatorText("Profile Overview");
                DrawOverview(io);

                // ---- disassembly + help ----
                ImGui.SeparatorText("Disassembly");
                Vector2 avail = ImGui.GetContentRegionAvail();
                if (avail.Y < 20) avail.Y = 20;
                if (ImGui.BeginChild("##dis", new Vector2(avail.X * 0.6f, avail.Y), ImGuiChildFlags.Borders))
                {
                    DrawDisassembly();
                }
                ImGui.EndChild();
                ImGui.SameLine();
                if (ImGui.BeginChild("##help", new Vector2(0, avail.Y), ImGuiChildFlags.None))
                {
                    DrawHelp();
                }
                ImGui.EndChild();
            }
            ImGui.End();
        }

        // ******************************************************************************************
        /// <summary>
        ///     The profile overview graph + its mouse handling
        /// </summary>
        // ******************************************************************************************
        void DrawOverview(ImGuiIOPtr _io)
        {
            Vector2 p0 = ImGui.GetCursorScreenPos();
            float w = Math.Max(ImGui.GetContentRegionAvail().X, 1.0f);
            ImGui.InvisibleButton("##overview", new Vector2(w, OVERVIEW_HEIGHT), ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
            bool hovered = ImGui.IsItemHovered();
            bool active = ImGui.IsItemActive();

            ImDrawListPtr dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(p0, new Vector2(p0.X + w, p0.Y + OVERVIEW_HEIGHT), ColBack);

            int n = (int)w - (PROF_X_START + PROF_X_END);
            if (ProfileRead == null || n < 16)
            {
                dl.AddText(new Vector2(p0.X + PROF_X_START, p0.Y + OVERVIEW_HEIGHT * 0.5f - 8), ColText,
                           "Click Start, run your code, then Stop to analyse.");
                return;
            }

            UpdateCells(n);

            float gx = p0.X + PROF_X_START;
            float baseY = p0.Y + GRAPH_HEIGHT + 10;
            int mx = (int)(_io.MousePos.X - gx);
            int cx = Math.Max(0, Math.Min(NumCells - 1, mx));

            // ---- mouse ----
            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                Selecting = true;
                SelStartX = cx;
            }
            if (Selecting)
            {
                if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
                {
                    float x1 = gx + Math.Min(SelStartX, cx);
                    float x2 = gx + Math.Max(SelStartX, cx);
                    dl.AddRectFilled(new Vector2(x1, baseY - GRAPH_HEIGHT), new Vector2(x2 + 1, baseY), ColSelect);
                }
                else
                {
                    Selecting = false;
                    Zoom(SelStartX, cx);
                    UpdateCells(n);
                }
            }
            if (active && ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                DissStart = BlockAdd[cx];                   // left click/drag picks the disassembly start
            }

            // ---- bars ----
            double scaler = GRAPH_HEIGHT / (max_value == 0 ? 1 : max_value);
            for (int i = 0; i < NumCells; i++)
            {
                int v = (int)(Blocks[i] * scaler);
                if (v <= 0) continue;
                int heat = PerfColours[HeatIndex(Blocks[i])];
                uint col = heat == 0 ? ColBar : Col(heat);
                float x = gx + i + 0.5f;
                dl.AddLine(new Vector2(x, baseY), new Vector2(x, baseY - v), col);
            }

            // ---- disassembly marker ----
            if (DissStart >= StartAddress && DissStart < EndAddress && CellSize > 0)
            {
                float x = gx + (float)((DissStart - StartAddress) / CellSize) + 0.5f;
                dl.AddLine(new Vector2(x, baseY - GRAPH_HEIGHT), new Vector2(x, baseY), ColDiss);
            }

            // ---- axis ----
            dl.AddLine(new Vector2(gx, baseY + 2), new Vector2(gx + NumCells, baseY + 2), ColAxis);
            for (int i = 0; i < NumCells; i += 50)
            {
                dl.AddLine(new Vector2(gx + i, baseY + 2), new Vector2(gx + i, baseY + 10), ColAxis);
            }
            for (int i = 0; i < NumCells; i += 100)
            {
                dl.AddText(new Vector2(gx + i - 4, baseY + 12), ColText, string.Format("${0:X6}", BlockAdd[i]));
            }
            dl.AddText(new Vector2(p0.X + 2, baseY - GRAPH_HEIGHT), ColText, max_value.ToString());

            // ---- hover info ----
            if (hovered && !Selecting && mx >= 0 && mx < NumCells)
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(string.Format("${0:X6}   {1} hits (avg/address)", BlockAdd[cx], Blocks[cx]));
                ImGui.EndTooltip();
            }
        }

        // ****************************************************************************************************************
        /// <summary>
        ///     Heat coloured disassembly from DissStart
        /// </summary>
        // ****************************************************************************************************************
        void DrawDisassembly()
        {
            if (ProfileRead == null || DissStart < 0)
            {
                ImGui.TextDisabled("Left click in the Profile Overview to view disassembly");
                return;
            }

            float lh = ImGui.GetTextLineHeightWithSpacing();
            float width = ImGui.GetContentRegionAvail().X;
            int rows = (int)(ImGui.GetContentRegionAvail().Y / lh);
            ImDrawListPtr dl = ImGui.GetWindowDrawList();

            int Add = DissStart;
            for (int r = 0; r < rows && Add < MEMORY_SIZE; r++)
            {
                Vector2 p = ImGui.GetCursorScreenPos();
                uint bg = GetColour(Add);
                if (bg != 0)
                {
                    dl.AddRectFilled(new Vector2(p.X - 2, p.Y), new Vector2(p.X + width, p.Y + lh), bg);
                    ImGui.PushStyleColor(ImGuiCol.Text, ColBlackText);
                }

                DissassemblyLine l = CSpect.DissasembleMemory(Add, true);
                string label = CSpect.LookUpSymbol(Add);
                if (label != null)
                {
                    int inof = label.IndexOf('@');
                    if (inof >= 0)
                    {
                        label = label.Substring(inof);
                    }
                    if (label.Length > 20)
                    {
                        label = label.Substring(0, 20);
                    }
                }

                ImGui.TextUnformatted(string.Format("${0:X6}", Add));
                ImGui.SameLine(80);
                ImGui.TextUnformatted(label ?? "");
                ImGui.SameLine(260);
                ImGui.TextUnformatted(l != null && l.line != null ? l.line : "?");

                if (bg != 0) ImGui.PopStyleColor();

                int bytes = (l != null) ? l.bytes : 1;
                Add += (bytes > 0) ? bytes : 1;
            }
        }

        // ****************************************************************************************************************
        /// <summary>
        ///     Instructions + stats
        /// </summary>
        // ****************************************************************************************************************
        void DrawHelp()
        {
            ImGui.TextUnformatted("Instructions");
            ImGui.Separator();
            ImGui.TextWrapped("Click Start to begin profiling, and Stop to start analysis.");
            ImGui.TextWrapped("Right click and drag in the Profile Overview to select a region (zoom in).");
            ImGui.TextWrapped("Left click (or drag) in the Profile Overview to view disassembly.");
            ImGui.TextWrapped("Reset zoom shows the whole of memory again.");

            if (ProfileRead != null && NumCells > 0)
            {
                ImGui.Spacing();
                ImGui.TextUnformatted("Statistics");
                ImGui.Separator();
                ImGui.TextUnformatted(string.Format("Range:      ${0:X6} - ${1:X6}", StartAddress, EndAddress));
                ImGui.TextUnformatted(string.Format("Bytes/cell: {0:0.##}", CellSize));
                ImGui.TextUnformatted(string.Format("Max (view): {0}", max_value));
                if (!Running) ImGui.TextUnformatted(string.Format("Max (all):  {0}", max_sample));
            }
        }
        #endregion
    }
}

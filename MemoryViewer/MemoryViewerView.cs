// ********************************************************************************************************************************************
//      CSpect Memory Viewer extension - Dear ImGui view
//      Written by:
//                  Mike Dailly
//
//      contributions by:
//
//      Released under the GNU 3 license - please see license file for more details
//
//      Renders an area of physical memory as a Spectrum screen / 1bit / 256 / 16 colour / raw bitmap into a 1024x1024
//      ARGB buffer, uploads it to an ImGui texture (iWindow.CreateImGuiTexture) and shows it scaled. Hover a pixel to
//      see the byte, the PC that last wrote it and its physical address; double click to break on that code.
//
// ********************************************************************************************************************************************
using Plugin;
using System;
using System.Numerics;
using ImGuiNET;

namespace MemoryViewer
{
    public enum eMemoryMode : int
    {
        SpectrumScreen = 0,
        LinearBitmap,
        Layer2_256,
        Layer2_320,
        SixteenColour_Linear,
        SixteenColour_Stripped,
        Raw_Grayscale,
    }

    public class MemoryViewerView
    {
        /// <summary>Size of the render buffer / texture</summary>
        const int TEX_SIZE = 1024;
        /// <summary>Usable area (1 pixel border, as the original back buffer)</summary>
        int MAX_WIDTH = TEX_SIZE - 2;
        int MAX_HEIGHT = TEX_SIZE - 2;
        const int MEMORY_SIZE = 2 * 1024 * 1024;

        static readonly string[] ModeNames = { "Spectrum Screen", "Linear 1bit", "256 colour Linear", "256 colour stripped", "16 Colour Linear", "16 Colour stripped", "Raw (grayscale)" };
        static readonly string[] WidthPresets = { "1", "2", "4", "8", "16", "32", "64", "128", "256", "512", "1024", "2048" };
        static readonly string[] ScaleNames = { "x1", "x2", "x3", "x4", "x5", "x6", "x7" };
        static readonly string[] PaletteNames = { "ULA 0", "ULA 1", "Layer2 0", "Layer2 1", "Sprites 0", "Sprites 1", "Tiles 0", "Tiles 1" };
        static readonly int[] PaletteNumbers = { 0, 4, 1, 5, 2, 6, 3, 7 };
        static readonly string[] OffsetNames = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13", "14", "15" };

        /// <summary>Hex conversion</summary>
        public static string HEX = "0123456789abcdef";

        MemoryViewerPlugin Plugin;
        iCSpect CSpect;
        iWindow Window;

        /// <summary>The render buffer (ARGB) and its texture</summary>
        UInt32[] Pixels = new UInt32[TEX_SIZE * TEX_SIZE];
        IntPtr Texture = IntPtr.Zero;

        /// <summary>Memory window width (bytes per line)</summary>
        int MemoryWidth = 32;
        eMemoryMode Mode = eMemoryMode.SpectrumScreen;
        int ModeIndex = 0;

        /// <summary>Number of bytes to read from emulator</summary>
        int BufferSize = 16384;
        byte[] MemBuffer;

        /// <summary>Scale to draw the image at</summary>
        int DrawScale = 2;
        int ScaleIndex = 1;
        int CurrentAddress = 10 * 8192;
        bool DoClear = true;
        bool DoSnapshot = true;
        bool Realtime = true;
        bool ULAEnabled = true;

        int PaletteIndex = 1;
        /// <summary>Palette offset</summary>
        int PaletteOffset = 0;
        /// <summary>Palette offset * 16 for 256 palette</summary>
        int PaletteOffset16 = 0;

        string BankOffsetText = "10:$0000";
        string WidthText = "32";
        string BuffSizeText = "$4000";

        /// <summary>Last tooltip Physical PC address</summary>
        int LastPhysicalPC = -1;
        /// <summary>Last tooltip Logical PC address</summary>
        int LastLogicalPC = -1;

        // ******************************************************************************************
        /// <summary>
        ///     Create the viewer
        /// </summary>
        // ******************************************************************************************
        public MemoryViewerView(MemoryViewerPlugin _plugin)
        {
            Plugin = _plugin;
            CSpect = Plugin.CSpect;
            MemBuffer = new byte[BufferSize];
            Plugin.PaletteNumber = PaletteNumbers[PaletteIndex];
        }

        /// <summary>A window has been opened for us</summary>
        public void Attach(iWindow _window)
        {
            Window = _window;
            Texture = IntPtr.Zero;              // created on first draw
            DoClear = true;
            DoSnapshot = true;
        }

        /// <summary>Our window has gone (its textures were freed with it)</summary>
        public void Detach()
        {
            Window = null;
            Texture = IntPtr.Zero;
        }

        // ******************************************************************************************
        /// <summary>
        ///     Peek physical memory, allowing us to swap to/from ULA memory access
        /// </summary>
        // ******************************************************************************************
        public byte[] Peek(int _PhysicalAddress, int _count, byte[] _buffer = null)
        {
            if (ULAEnabled)
            {
                return CSpect.PeekPhysicalULA(_PhysicalAddress, _count, _buffer);
            }
            else
            {
                return CSpect.PeekPhysical(_PhysicalAddress, _count, _buffer);
            }
        }

        #region Rendering into the buffer
        // ******************************************************************************************
        /// <summary>
        ///     Read the memory and render it into Pixels[] (then upload to the texture)
        /// </summary>
        // ******************************************************************************************
        unsafe void RenderMemory()
        {
            byte[] mem = Peek(CurrentAddress, BufferSize, MemBuffer);
            if (mem == null || mem.Length == 0) return;

            fixed (UInt32* pData = &Pixels[0])
            fixed (byte* pMem = &mem[0])
            {
                UInt32* pDataStart = pData + TEX_SIZE + 1;
                if (DoClear)
                {
                    DoClear = false;
                    for (int i = 0; i < TEX_SIZE * TEX_SIZE; i++) pData[i] = 0xff000080;
                }

                int size = Math.Min(BufferSize, mem.Length);
                switch (Mode)
                {
                    case eMemoryMode.SpectrumScreen: DrawSpectrumScreen(pDataStart, pMem, size); break;
                    case eMemoryMode.LinearBitmap: DrawLinearBitmap(pDataStart, pMem, size); break;
                    case eMemoryMode.Layer2_256: DrawLayer2_256Bitmap(pDataStart, pMem, size); break;
                    case eMemoryMode.Layer2_320: DrawLayer2_320Bitmap(pDataStart, pMem, size); break;
                    case eMemoryMode.SixteenColour_Linear: Draw16_Linear_Bitmap(pDataStart, pMem, size); break;
                    case eMemoryMode.SixteenColour_Stripped: Draw16_Stripped_Bitmap(pDataStart, pMem, size); break;
                    case eMemoryMode.Raw_Grayscale: DrawRAWBitmap(pDataStart, pMem, size); break;
                }
            }

            if (Window != null && Texture != IntPtr.Zero) Window.UpdateImGuiTexture(Texture, Pixels);
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw the spectrum screen
        /// </summary>
        // ******************************************************************************************
        private unsafe void DrawSpectrumScreen(uint* pData, byte* pMem, int MemSize)
        {
            int index = 0;
            for (int y = 0; y < 192; y++)
            {
                int x = 0;
                while (x < 256)
                {
                    if (index >= MemSize) return;
                    int b = pMem[index++];
                    int offset_base = ((((y >> 3) & 0x18) | (y & 7)) << 8) | ((y << 2) & 0xe0);
                    offset_base = offset_base >> 5;
                    for (int xx = 0; xx < 8; xx++)
                    {
                        pData[x + (offset_base * TEX_SIZE)] = ((b & 128) != 0) ? 0xffffffff : 0xff000000;
                        b <<= 1;
                        x++;
                    }
                }
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw a linear 1 bit memory dump
        /// </summary>
        // ******************************************************************************************
        private unsafe void DrawLinearBitmap(uint* pData, byte* pMem, int MemSize)
        {
            int xpixels = MemoryWidth * 8;
            if (xpixels > MAX_WIDTH) xpixels = MAX_WIDTH;

            int index = 0;
            for (int y = 0; y < MAX_HEIGHT; y++)
            {
                int baseoffset = (y * TEX_SIZE);
                int x = 0;
                while (x < xpixels)
                {
                    if (index >= MemSize) return;
                    int b = pMem[index++];
                    for (int xx = 0; xx < 8; xx++)
                    {
                        if (x < xpixels) pData[x + baseoffset] = ((b & 128) != 0) ? 0xffffffff : 0xff000000;
                        b <<= 1;
                        x++;
                    }
                }
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw a 256 colour "linear" (row major) dump
        /// </summary>
        // ******************************************************************************************
        private unsafe void DrawLayer2_256Bitmap(uint* pData, byte* pMem, int MemSize)
        {
            int index = 0;
            for (int y = 0; y < MAX_HEIGHT; y++)
            {
                int x = 0;
                int CurrIndex = index;
                int WidthToRender = MemoryWidth;
                if (WidthToRender > MAX_WIDTH) WidthToRender = MAX_WIDTH;

                // Render a single space - maxing out at the buffer width
                while (x < WidthToRender)
                {
                    if (index >= MemSize) return;
                    int b = pMem[index++];

                    pData[x + (y * TEX_SIZE)] = ZXPalette.Get((b + PaletteOffset16) & 0xff);
                    x++;
                }
                index = CurrIndex + MemoryWidth;
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw a 256 colour "stripped" (column major, like Layer 2 320/640) dump
        /// </summary>
        // ******************************************************************************************
        private unsafe void DrawLayer2_320Bitmap(uint* pData, byte* pMem, int MemSize)
        {
            int index = 0;

            int WidthToRender = MemoryWidth;
            if (WidthToRender > MAX_HEIGHT) WidthToRender = MAX_HEIGHT;

            for (int x = 0; x < MAX_WIDTH; x++)
            {
                int y = 0;
                while (y < WidthToRender)
                {
                    if (index >= MemSize) return;           // Don't go past out buffer size
                    int b = pMem[index++];

                    pData[x + (y * TEX_SIZE)] = ZXPalette.Get((b + PaletteOffset16) & 0xff);
                    y++;
                }
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw a 16 colour "linear" dump
        /// </summary>
        // ******************************************************************************************
        private unsafe void Draw16_Linear_Bitmap(uint* pData, byte* pMem, int MemSize)
        {
            int index = 0;
            for (int y = 0; y < MAX_HEIGHT; y++)
            {
                int x = 0;
                int CurrIndex = index;
                int WidthToRender = MemoryWidth * 2;
                if (WidthToRender > MAX_WIDTH) WidthToRender = MAX_WIDTH;

                // Render a single space - maxing out at the buffer width
                while (x < WidthToRender)
                {
                    if (index >= MemSize) return;
                    int b = pMem[index++];

                    pData[x + (y * TEX_SIZE)] = ZXPalette.Get(((b >> 4) & 15) + PaletteOffset16);
                    pData[x + 1 + (y * TEX_SIZE)] = ZXPalette.Get((b & 15) + PaletteOffset16);
                    x += 2;
                }
                index = CurrIndex + MemoryWidth;
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw a 16 colour "stripped" (column major) dump
        /// </summary>
        // ******************************************************************************************
        private unsafe void Draw16_Stripped_Bitmap(uint* pData, byte* pMem, int MemSize)
        {
            int index = 0;

            int WidthToRender = MemoryWidth;
            if (WidthToRender > MAX_HEIGHT) WidthToRender = MAX_HEIGHT;

            for (int x = 0; x < MAX_WIDTH; x += 2)
            {
                int y = 0;
                while (y < WidthToRender)
                {
                    if (index >= MemSize) return;           // Don't go past out buffer size
                    int b = pMem[index++];

                    pData[x + (y * TEX_SIZE)] = ZXPalette.Get(((b >> 4) & 15) + PaletteOffset16);
                    pData[x + 1 + (y * TEX_SIZE)] = ZXPalette.Get((b & 15) + PaletteOffset16);
                    y++;
                }
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Draw a raw grayscale dump
        /// </summary>
        // ******************************************************************************************
        private unsafe void DrawRAWBitmap(uint* pData, byte* pMem, int MemSize)
        {
            int index = 0;
            for (int y = 0; y < MAX_HEIGHT; y++)
            {
                int x = 0;
                int CurrIndex = index;
                int WidthToRender = MemoryWidth;
                if (WidthToRender > MAX_WIDTH) WidthToRender = MAX_WIDTH;

                // Render a single space - maxing out at the buffer width
                while (x < WidthToRender)
                {
                    if (index >= MemSize) return;
                    uint b = (UInt32)pMem[index++];

                    pData[x + (y * TEX_SIZE)] = 0xff000000 | b | (b << 8) | (b << 16);
                    x++;
                }
                index = CurrIndex + MemoryWidth;
            }
        }

        // ******************************************************************************************
        /// <summary>
        ///     Size of the area the current mode draws into (so we only show/scroll that much)
        /// </summary>
        // ******************************************************************************************
        void GetContentSize(out int _w, out int _h)
        {
            int mw = Math.Max(1, MemoryWidth);
            int lines = (BufferSize + mw - 1) / mw;
            switch (Mode)
            {
                case eMemoryMode.SpectrumScreen: _w = 256; _h = 192; break;
                case eMemoryMode.LinearBitmap: _w = mw * 8; _h = lines; break;
                case eMemoryMode.Layer2_320: _w = lines; _h = mw; break;
                case eMemoryMode.SixteenColour_Linear: _w = mw * 2; _h = lines; break;
                case eMemoryMode.SixteenColour_Stripped: _w = lines * 2; _h = mw; break;
                default: _w = mw; _h = lines; break;          // 256 linear, raw
            }
            _w = Math.Max(1, Math.Min(_w, MAX_WIDTH));
            _h = Math.Max(1, Math.Min(_h, MAX_HEIGHT));
        }

        // ***************************************************************************************************************************
        /// <summary>
        ///      Get pixel memory details
        /// </summary>
        /// <param name="_x">Pixel X in the memory image</param>
        /// <param name="_y">Pixel Y in the memory image</param>
        /// <returns>Formatted string for tool tips</returns>
        // ***************************************************************************************************************************
        public string GetMemoryDetails(int _x, int _y)
        {
            int Address = 0;
            switch (Mode)
            {
                case eMemoryMode.SpectrumScreen:
                    {
                        int offset_base = ((((_y >> 3) & 0x18) | (_y & 7)) << 8) | ((_y << 2) & 0xe0);
                        Address = CurrentAddress + offset_base + (_x / 8);
                        break;
                    }
                case eMemoryMode.LinearBitmap: Address = CurrentAddress + (_x / 8) + (_y * MemoryWidth); break;
                case eMemoryMode.Layer2_256: Address = CurrentAddress + _x + (_y * MemoryWidth); break;
                case eMemoryMode.Layer2_320: Address = CurrentAddress + _y + (_x * MemoryWidth); break;
                case eMemoryMode.SixteenColour_Linear: Address = CurrentAddress + (_x / 2) + (_y * MemoryWidth); break;
                case eMemoryMode.SixteenColour_Stripped: Address = CurrentAddress + _y + ((_x / 2) * MemoryWidth); break;
                case eMemoryMode.Raw_Grayscale: Address = CurrentAddress + _x + (_y * MemoryWidth); break;
            }

            if (Address < 0 || Address >= MEMORY_SIZE) Address = 0;
            byte[] data = Peek(Address, 1);
            byte b = (data != null && data.Length > 0) ? data[0] : (byte)0;

            SMemWrite mwrite = CSpect.GetMemoryAccess(Address, ULAEnabled);
            LastPhysicalPC = mwrite.PhysicalAddress;
            LastLogicalPC = mwrite.PC;

            int bank = mwrite.PhysicalAddress >> 13;
            int offs = mwrite.PhysicalAddress & 0x1fff;
            return "Address:  $" + (Address >> 13).ToString("X") + ":$" + (Address & 0x1fff).ToString("X4") + "\n" +
                   "Byte:     $" + b.ToString("X2") + "\n" +
                   "PC:       $" + mwrite.PC.ToString("X") + "\n" +
                   "Physical: $" + bank.ToString("X") + ":$" + offs.ToString("X") + "\n" +
                   "(double click: breakpoint on that write)";
        }
        #endregion

        #region Settings
        // ***********************************************************************************************
        /// <summary>
        ///     Convert a string to a long (decimal, or hex with $ or 0x)
        /// </summary>
        /// <returns>The value, or -1 if empty</returns>
        // ***********************************************************************************************
        public long ConvertNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            text = text.Trim().ToLower();
            if (text.Length == 0) return -1;

            long value = 0;
            bool ishex = false;
            int index = 0;
            if (text[0] == '$') { ishex = true; index = 1; }
            else if (text.Length >= 2 && text[0] == '0' && text[1] == 'x') { ishex = true; index = 2; }

            if (ishex)
            {
                while (index < text.Length)
                {
                    int d = HEX.IndexOf(text[index++]);
                    if (d < 0) return value;            // invalid character
                    value = (value * 16) + d;
                }
            }
            else
            {
                while (index < text.Length)
                {
                    char c = text[index++];
                    if (c < '0' || c > '9') return value;   // invalid character
                    value = (value * 10) + (c - '0');
                }
            }
            return value;
        }

        // ***********************************************************************************************
        /// <summary>
        ///     Physical address text changed - "bank:offset" or a full physical address
        /// </summary>
        // ***********************************************************************************************
        void ParseBankOffset()
        {
            string text = BankOffsetText;
            long value;
            int colonindex = text.IndexOf(':');
            if (colonindex >= 0)
            {
                long b = ConvertNumber(text.Substring(0, colonindex));
                long o = ConvertNumber(text.Substring(colonindex + 1));
                if (b < 0 || o < 0) return;                 // invalid address
                value = (b * 8192) + o;
            }
            else
            {
                value = ConvertNumber(text);
                if (value < 0) return;
            }
            if (value >= MEMORY_SIZE) value = MEMORY_SIZE - 1;
            CurrentAddress = (int)value;
            DoSnapshot = true;
        }

        // **************************************************************************************************
        /// <summary>
        ///     Move the address by a bank (8K)
        /// </summary>
        // **************************************************************************************************
        void StepBank(int _dir)
        {
            string text = BankOffsetText;
            int colonindex = text.IndexOf(':');
            if (colonindex >= 0)
            {
                long b = ConvertNumber(text.Substring(0, colonindex));
                if (b < 0) return;
                b += _dir;
                if (b < 0) b = 255;
                if (b > 255) b = 0;
                BankOffsetText = "$" + b.ToString("X") + ":" + text.Substring(colonindex + 1);
            }
            else
            {
                long b = ConvertNumber(text);
                if (b < 0) return;
                b += _dir * 8192;
                if (b < 0) b = MEMORY_SIZE - 8192;
                if (b >= MEMORY_SIZE) b = 0;
                BankOffsetText = "$" + b.ToString("X");
            }
            ParseBankOffset();
        }

        void SetWidth(string _text)
        {
            long v = ConvertNumber(_text);
            if (v > 0)
            {
                MemoryWidth = (int)Math.Min(v, MEMORY_SIZE);
                DoClear = true;
                DoSnapshot = true;
            }
        }

        void SetBufferSize(string _text)
        {
            long value = ConvertNumber(_text);
            if (value > 0)
            {
                if (value > MEMORY_SIZE) value = MEMORY_SIZE;
                BufferSize = (int)value;
                MemBuffer = new byte[BufferSize];
                DoClear = true;
                DoSnapshot = true;
            }
        }
        #endregion

        #region ImGui
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
                Texture = Window.CreateImGuiTexture(TEX_SIZE, TEX_SIZE);
                DoSnapshot = true;
            }

            // Fresh image? (every frame in RealTime, else on SnapShot / settings change)
            if (Realtime || DoSnapshot)
            {
                DoSnapshot = false;
                RenderMemory();
            }

            ImGuiIOPtr io = ImGui.GetIO();
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(io.DisplaySize);
            ImGuiWindowFlags wflags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
                                      ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus;
            bool open = true;                               // no close button (NoDecoration) - the OS window closes it
            if (ImGui.Begin("##MemoryViewer", ref open, wflags))
            {
                DrawToolbar();
                ImGui.Separator();
                DrawImage();
            }
            ImGui.End();
        }

        // ******************************************************************************************
        /// <summary>
        ///     The two rows of settings
        /// </summary>
        // ******************************************************************************************
        void DrawToolbar()
        {
            // ---- row 1 ----
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("Mode", ref ModeIndex, ModeNames, ModeNames.Length))
            {
                Mode = (eMemoryMode)ModeIndex;
                DoClear = true;
                DoSnapshot = true;
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(60);
            if (ImGui.InputText("##width", ref WidthText, 16)) SetWidth(WidthText);
            ImGui.SameLine(0, 0);
            if (ImGui.BeginCombo("Byte Width", "", ImGuiComboFlags.NoPreview))
            {
                foreach (string p in WidthPresets)
                {
                    if (ImGui.Selectable(p))
                    {
                        WidthText = p;
                        SetWidth(p);
                    }
                }
                ImGui.EndCombo();
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputText("Bank:Offset", ref BankOffsetText, 32)) ParseBankOffset();
            ImGui.SameLine();
            if (ImGui.ArrowButton("##bankdown", ImGuiDir.Up)) StepBank(-1);
            ImGui.SameLine();
            if (ImGui.ArrowButton("##bankup", ImGuiDir.Down)) StepBank(1);

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            if (ImGui.Combo("Palette", ref PaletteIndex, PaletteNames, PaletteNames.Length))
            {
                Plugin.PaletteNumber = PaletteNumbers[PaletteIndex];
                DoSnapshot = true;
            }


            // ---- row 2 ----
            if (ImGui.Checkbox("ULA Enable", ref ULAEnabled)) DoSnapshot = true;
            ImGui.SameLine();
            ImGui.Checkbox("RealTime", ref Realtime);

            ImGui.SameLine();
            ImGui.SetNextItemWidth(60);
            if (ImGui.Combo("Scale", ref ScaleIndex, ScaleNames, ScaleNames.Length)) DrawScale = ScaleIndex + 1;

            ImGui.SameLine();
            ImGui.SetNextItemWidth(90);
            if (ImGui.InputText("BufferSize", ref BuffSizeText, 16)) SetBufferSize(BuffSizeText);

            ImGui.SameLine();
            ImGui.SetNextItemWidth(60);
            if (ImGui.Combo("Offset", ref PaletteOffset, OffsetNames, OffsetNames.Length))
            {
                PaletteOffset16 = PaletteOffset * 16;
                DoSnapshot = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("SnapShot", new Vector2(70, 0))) DoSnapshot = true;

            // emulator control
            bool paused = (bool)CSpect.GetGlobal(eGlobal.pause);
            ImGui.SameLine();
            if (ImGui.Button(paused ? "Resume" : "Pause", new Vector2(70, 0)))
            {
                CSpect.SetGlobal(eGlobal.pause, !paused);
            }
            ImGui.SameLine();
            ImGui.BeginDisabled(!paused);
            if (ImGui.Button("Step", new Vector2(70, 0))) Plugin.DoStep = 2;
            ImGui.EndDisabled();
        }

        // ******************************************************************************************
        /// <summary>
        ///     The memory image (scrolls when bigger than the window), tooltip + double click breakpoint
        /// </summary>
        // ******************************************************************************************
        void DrawImage()
        {
            if (!ImGui.BeginChild("##memory", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
            {
                ImGui.EndChild();
                return;
            }

            if (Texture == IntPtr.Zero)
            {
                ImGui.TextDisabled("No texture (Dear ImGui unavailable)");
                ImGui.EndChild();
                return;
            }

            int w, h;
            GetContentSize(out w, out h);
            Vector2 uv0 = new Vector2(1.0f / TEX_SIZE, 1.0f / TEX_SIZE);                          // skip the 1 pixel border
            Vector2 uv1 = new Vector2((1.0f + w) / TEX_SIZE, (1.0f + h) / TEX_SIZE);
            ImGui.Image(Texture, new Vector2(w * DrawScale, h * DrawScale), uv0, uv1);

            if (ImGui.IsItemHovered())
            {
                Vector2 m = ImGui.GetIO().MousePos - ImGui.GetItemRectMin();
                int px = Math.Max(0, Math.Min(w - 1, (int)(m.X / DrawScale)));
                int py = Math.Max(0, Math.Min(h - 1, (int)(m.Y / DrawScale)));
                string tip = GetMemoryDetails(px, py);

                ImGui.BeginTooltip();
                ImGui.TextUnformatted(tip);
                ImGui.EndTooltip();

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && LastPhysicalPC != -1)
                {
                    CSpect.Debugger(eDebugCommand.SetPhysicalBreakpoint, LastPhysicalPC);
                    LastPhysicalPC = -1;
                }
            }
            ImGui.EndChild();
        }
        #endregion
    }
}

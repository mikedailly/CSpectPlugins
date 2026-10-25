// ****************************************************************************************
//  CSpect Palette Viewer plugin
//
//  Shows the ULA, Layer 2, Sprite and Tilemap palettes (first and second) as a 16x16 grid
//  of colours using the CSpect v4 Dear ImGui window interface.  Double click a colour to
//  edit it.
//
//  Key: <ctrl><alt>L   (change OPEN_KEY below to use something else)
// ****************************************************************************************
using System;
using System.Collections.Generic;
using System.Threading;
using Plugin;

namespace PaletteViewer
{
    /// <summary>A single pending colour write</summary>
    internal struct ColourEdit
    {
        public int Palette;     // 0-7 (CSpect palette number)
        public int Index;       // 0-255
        public int Value;       // 9 bit RRRGGGBBB
    }

    /// <summary>A copy of all 8 palettes plus the registers that say which ones are active</summary>
    internal sealed class PaletteSnapshot
    {
        public readonly uint[] Colours = new uint[8 * 256];     // [palette*256 + index], 9 bit values
        public byte Reg43;                                      // Palette control
        public byte Reg6B;                                      // Tilemap control

        public static PaletteSnapshot Capture(iCSpect _cspect, PaletteSnapshot _reuse)
        {
            PaletteSnapshot s = _reuse ?? new PaletteSnapshot();
            for (int p = 0; p < 8; p++)
            {
                int b = p * 256;
                for (int i = 0; i < 256; i++)
                {
                    s.Colours[b + i] = _cspect.GetColour(p, i) & 0x1ff;
                }
            }
            s.Reg43 = _cspect.GetNextRegister(0x43);
            s.Reg6B = _cspect.GetNextRegister(0x6B);
            return s;
        }

        public void CopyFrom(PaletteSnapshot _src)
        {
            Array.Copy(_src.Colours, Colours, Colours.Length);
            Reg43 = _src.Reg43;
            Reg6B = _src.Reg6B;
        }
    }


    // ****************************************************************************************
    /// <summary>
    ///     The plugin itself.
    ///     Tick()   - emulator thread: applies queued edits and captures the palettes.
    ///     OSTick() - UI thread: opens the window, and does Tick's job while the machine is paused.
    ///     Draw     - UI thread (ImGui callback): renders from the latest snapshot.
    /// </summary>
    // ****************************************************************************************
    public sealed class PaletteViewerPlugin : iPlugin
    {
        public const string OPEN_KEY = "<ctrl><alt>l";
        public const string WindowTitle = "Palette Viewer - Ctrl+Alt+L";

        private iCSpect CSpect;
        private iWindow Window;
        private PaletteViewerView View;

        private volatile bool Active;
        private int OpenRequested;

        private readonly object SnapshotLock = new object();
        private PaletteSnapshot Shared = new PaletteSnapshot();      // guarded by SnapshotLock
        private PaletteSnapshot TickCapture;                         // scratch for the emulator thread
        private PaletteSnapshot OSCapture;                           // scratch for the UI thread
        private int SnapshotVersion;

        private readonly object EditLock = new object();
        private readonly List<ColourEdit> PendingEdits = new List<ColourEdit>();


        // ------------------------------------------------------------------------------------
        public List<sIO> Init(iCSpect _CSpect)
        {
            CSpect = _CSpect;
            Console.WriteLine(" Palette Viewer added");
            return new List<sIO> { new sIO(OPEN_KEY, eAccess.KeyPress, 0) };
        }

        // ------------------------------------------------------------------------------------
        public bool KeyPressed(int _id)
        {
            if (_id != 0) return false;
            Interlocked.Exchange(ref OpenRequested, 1);
            return true;
        }

        // ------------------------------------------------------------------------------------
        /// <summary>Emulator thread, once per frame (not called while paused)</summary>
        // ------------------------------------------------------------------------------------
        public void Tick()
        {
            if (!Active) return;
            ApplyEdits();
            TickCapture = TakeSnapshot(TickCapture);
        }

        // ------------------------------------------------------------------------------------
        /// <summary>UI thread</summary>
        // ------------------------------------------------------------------------------------
        public void OSTick()
        {
            if (Interlocked.Exchange(ref OpenRequested, 0) != 0)
            {
                OpenWindow();
            }

            // Tick() stops while the debugger has the machine paused, so do the work here instead.
            if (Active && CSpect.Debugger(eDebugCommand.GetState) != 0)
            {
                ApplyEdits();
                OSCapture = TakeSnapshot(OSCapture);
            }
        }

        // ------------------------------------------------------------------------------------
        private void OpenWindow()
        {
            if (Window != null) return;     // already open

            // Get something to show straight away
            OSCapture = TakeSnapshot(OSCapture);

            iWindow w = CSpect.OpenWindow(WindowTitle, 440, 560);
            if (w == null) return;

            PaletteViewerView view = new PaletteViewerView(this);
            if (!w.SetImGuiDraw(view.Draw))
            {
                Console.WriteLine(" Palette Viewer: Dear ImGui not available on this system");
                w.Close();
                return;
            }
            w.OnClosed += Window_OnClosed;
            Window = w;
            View = view;
            Active = true;
        }

        // ------------------------------------------------------------------------------------
        private void Window_OnClosed(object sender, EventArgs e)
        {
            Active = false;
            if (Window != null) Window.OnClosed -= Window_OnClosed;
            Window = null;
            View = null;
            lock (EditLock) { PendingEdits.Clear(); }
        }

        // ------------------------------------------------------------------------------------
        private PaletteSnapshot TakeSnapshot(PaletteSnapshot _scratch)
        {
            PaletteSnapshot snap = PaletteSnapshot.Capture(CSpect, _scratch);
            lock (SnapshotLock)
            {
                Shared.CopyFrom(snap);
                SnapshotVersion++;
            }
            return snap;
        }

        // ------------------------------------------------------------------------------------
        private void ApplyEdits()
        {
            ColourEdit[] edits = null;
            lock (EditLock)
            {
                if (PendingEdits.Count == 0) return;
                edits = PendingEdits.ToArray();
                PendingEdits.Clear();
            }
            foreach (ColourEdit e in edits)
            {
                CSpect.SetColour(e.Palette, e.Index, e.Value);
            }
        }

        // ------------------------------------------------------------------------------------
        /// <summary>Called by the view (UI thread) - copies the latest palettes if they've changed</summary>
        // ------------------------------------------------------------------------------------
        internal bool CopySnapshot(PaletteSnapshot _dest, ref int _version)
        {
            lock (SnapshotLock)
            {
                if (_version == SnapshotVersion) return false;
                _dest.CopyFrom(Shared);
                _version = SnapshotVersion;
                return true;
            }
        }

        // ------------------------------------------------------------------------------------
        /// <summary>Called by the view (UI thread) - queue a colour change for the emulator</summary>
        // ------------------------------------------------------------------------------------
        internal void QueueEdit(int _palette, int _index, int _value)
        {
            ColourEdit e = new ColourEdit { Palette = _palette & 7, Index = _index & 0xff, Value = _value & 0x1ff };
            lock (EditLock) { PendingEdits.Add(e); }

            // Show the change immediately, before the emulator gets around to it
            lock (SnapshotLock)
            {
                Shared.Colours[(e.Palette * 256) + e.Index] = (uint)e.Value;
                SnapshotVersion++;
            }
        }

        // ------------------------------------------------------------------------------------
        public void Reset()
        {
            lock (EditLock) { PendingEdits.Clear(); }
        }

        // ------------------------------------------------------------------------------------
        public void Quit()
        {
            Active = false;
            Interlocked.Exchange(ref OpenRequested, 0);
            if (Window != null)
            {
                Window.OnClosed -= Window_OnClosed;
                Window.Close();
                Window = null;
            }
            View = null;
        }

        // ------------------------------------------------------------------------------------
        public byte Read(eAccess _type, int _port, int _id, out bool _isvalid)
        {
            _isvalid = false;
            return 0;
        }

        // ------------------------------------------------------------------------------------
        public bool Write(eAccess _type, int _port, int _id, byte _value)
        {
            return false;
        }
    }
}

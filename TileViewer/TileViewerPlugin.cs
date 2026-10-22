using System;
using System.Collections.Generic;
using System.Threading;
using Plugin;

namespace TileViewer
{
    // Ctrl+Alt+T opens the viewer in its own OS window (iCSpect.OpenWindow) drawn with Dear ImGui
    // (iWindow.SetImGuiDraw) - no WinForms.
    public sealed class TileViewerPlugin : iPlugin
    {
        internal const string WindowTitle = "Tile Viewer - Ctrl+Alt+T";

        private iCSpect cspect;
        private iWindow window;
        private TileViewerModel model;
        private TileViewerView view;
        private TileSnapshot lastFrame, pendingFrame;
        private volatile bool active;
        private int openRequested;
        private readonly TileMapEditor editor = new TileMapEditor();

        // The open window's state (null when closed) - for the tests
        internal TileViewerModel Model { get { return model; } }

        public List<sIO> Init(iCSpect emulator)
        {
            cspect = emulator;
            return new List<sIO> { new sIO("<ctrl><alt>t", eAccess.KeyPress, 0) };
        }

        public bool KeyPressed(int id)
        {
            if (id != 0) return false;
            Interlocked.Exchange(ref openRequested, 1);
            return true;
        }

        // Capture emulator state here; create and draw windows only on the OS thread.
        public void Tick()
        {
            if (!active) return;
            editor.ApplyPending(cspect);
            TileSnapshot frame = TileSnapshot.Capture(cspect);
            if (frame.SameAs(lastFrame)) return;
            lastFrame = frame;
            Interlocked.Exchange(ref pendingFrame, frame);
        }

        public void OSTick()
        {
            if (Interlocked.Exchange(ref openRequested, 0) != 0 && window == null) OpenWindow();

            TileSnapshot frame = Interlocked.Exchange(ref pendingFrame, null);
            if (frame != null && model != null) model.UpdateFrame(frame);
            // Tick stops while paused. The paused machine can be captured and edited on the OS thread.
            if (active && (editor.HasPending || lastFrame == null) && cspect.Debugger(eDebugCommand.GetState) != 0)
            {
                if (editor.ApplyPending(cspect) || lastFrame == null)
                {
                    TileSnapshot edited = TileSnapshot.Capture(cspect);
                    Interlocked.Exchange(ref lastFrame, edited);
                    Interlocked.Exchange(ref pendingFrame, null);
                    if (model != null) model.UpdateFrame(edited);
                }
            }
            if (active && model != null) model.UpdateEditorState(editor.CanUndo, editor.TakeStatus());
        }

        private void OpenWindow()
        {
            iWindow w = cspect.OpenWindow(WindowTitle, 680, 820);
            if (w == null) return;
            var newModel = new TileViewerModel();
            var newView = new TileViewerView(newModel);
            if (!w.SetImGuiDraw(newView.Draw))
            {
                Console.WriteLine(" Tile Viewer: Dear ImGui not available on this system");
                w.Close();
                return;
            }
            newModel.EditRequested += editor.Enqueue;
            newView.Attach(w);
            w.OnClosed += WindowClosed;
            model = newModel;
            view = newView;
            window = w;
            // Running-machine snapshots come from Tick; paused snapshots come from OSTick.
            Interlocked.Exchange(ref lastFrame, null);
            active = true;
        }

        // Closed by the user, or by Quit - its textures went with it
        private void WindowClosed(object sender, EventArgs e)
        {
            active = false;
            if (view != null) view.Detach();
            window = null;
            model = null;
            view = null;
            Interlocked.Exchange(ref pendingFrame, null);
            editor.Reset();
        }

        public void Reset() { editor.Reset(); Interlocked.Exchange(ref lastFrame, null); }

        public void Quit()
        {
            active = false;
            editor.Reset();
            Interlocked.Exchange(ref openRequested, 0);
            Interlocked.Exchange(ref pendingFrame, null);
            if (window != null) { iWindow w = window; window = null; w.Close(); }
        }

        public byte Read(eAccess type, int address, int id, out bool isValid)
        { isValid = false; return 0; }

        public bool Write(eAccess type, int address, int id, byte value) { return false; }
    }
}

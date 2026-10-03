using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using Plugin;

namespace TileViewer
{
    public sealed class TileViewerPlugin : iPlugin
    {
        private iCSpect cspect;
        private TileViewerForm form;
        private TileSnapshot lastFrame, pendingFrame;
        private volatile bool active;
        private int openRequested;
        private readonly TileMapEditor editor = new TileMapEditor();

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

        // Capture emulator state here; create and paint windows only in OSTick.
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
            if (Interlocked.Exchange(ref openRequested, 0) != 0)
            {
                if (form == null || form.IsDisposed)
                {
                    form = new TileViewerForm();
                    form.EditRequested += editor.Enqueue;
                    form.FormClosed += delegate
                    {
                        active = false;
                        form = null;
                        Interlocked.Exchange(ref pendingFrame, null);
                        editor.Reset();
                    };
                    // Running-machine snapshots come from Tick; paused snapshots can use OSTick.
                    Interlocked.Exchange(ref lastFrame, null);
                    active = true;
                    form.Show(new EmulatorWindow((IntPtr)cspect.GetGlobal(eGlobal.window_handle)));
                }
                else
                {
                    if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
                    form.Activate();
                }
            }
            TileSnapshot frame = Interlocked.Exchange(ref pendingFrame, null);
            if (frame != null && form != null && !form.IsDisposed) form.UpdateFrame(frame);
            if (active && form != null) form.PumpMessages();
            // Tick stops while paused. The paused machine can be edited on the OS thread.
            if (active && (editor.HasPending || lastFrame == null) && cspect.Debugger(eDebugCommand.GetState) != 0)
            {
                if (editor.ApplyPending(cspect) || lastFrame == null)
                {
                    TileSnapshot edited = TileSnapshot.Capture(cspect);
                    Interlocked.Exchange(ref lastFrame, edited);
                    Interlocked.Exchange(ref pendingFrame, null);
                    if (form != null) form.UpdateFrame(edited);
                }
            }
            if (active && form != null) form.UpdateEditorState(editor.CanUndo, editor.TakeStatus());
        }

        public void Reset() { editor.Reset(); Interlocked.Exchange(ref lastFrame, null); }

        public void Quit()
        {
            active = false;
            editor.Reset();
            Interlocked.Exchange(ref openRequested, 0);
            Interlocked.Exchange(ref pendingFrame, null);
            if (form != null) { form.Dispose(); form = null; }
        }

        public byte Read(eAccess type, int address, int id, out bool isValid)
        { isValid = false; return 0; }

        public bool Write(eAccess type, int address, int id, byte value) { return false; }

        private sealed class EmulatorWindow : IWin32Window
        {
            public IntPtr Handle { get; private set; }
            internal EmulatorWindow(IntPtr handle) { Handle = handle; }
        }
    }
}

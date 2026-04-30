using Plugin;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MemoryDumper
{
    /// <summary>
    /// Helper so the dialog parents to the CSpect main window.
    /// </summary>
    public class WindowWrapper : IWin32Window
    {
        public IntPtr Handle { get; }
        public WindowWrapper(IntPtr h) { Handle = h; }
    }

    /// <summary>
    /// Memory Dumper plugin -- press Ctrl+Alt+S to open a small window that
    /// reads a range from the emulated machine (Z80 64K via current MMU, or
    /// directly from the Next's 2MB physical RAM if a bank is supplied) and
    /// writes it to a binary file.
    ///
    /// Built specifically because CSpect's built-in `>save` console command
    /// is fiddly (quoting / hex / cwd) and the user kept losing dumps. One
    /// dialog, one button, file goes where you tell it.
    /// </summary>
    public class MemoryDumperPlugin : iPlugin
    {
        public iCSpect CSpect;
        public WindowWrapper Owner;
        private MemoryDumperForm form;
        private bool openRequested;

        /// <summary>
        /// Most recently opened map file path. Persists across form re-opens
        /// within a CSpect session so the user doesn't have to re-Load each
        /// time. Cleared when the plugin is unloaded.
        /// </summary>
        public string LastMapPath;

        public List<sIO> Init(iCSpect _CSpect)
        {
            Console.WriteLine("MemoryDumper plugin loaded -- Ctrl+Alt+S to open");
            CSpect = _CSpect;
            Owner = new WindowWrapper((IntPtr)CSpect.GetGlobal(eGlobal.window_handle));

            // Hotkey to open the dumper window. ID 0 == "open".
            var ports = new List<sIO>();
            ports.Add(new sIO("<ctrl><alt>s", eAccess.KeyPress, 0));
            return ports;
        }

        public bool KeyPressed(int _id)
        {
            if (_id == 0)
            {
                // Defer the actual Show() to OSTick -- KeyPressed runs on
                // the emulator thread and creating a Form there can deadlock
                // against the WinForms message pump.
                openRequested = true;
                return true;
            }
            return false;
        }

        public void OSTick()
        {
            if (!openRequested) return;
            openRequested = false;

            if (form == null || form.IsDisposed)
            {
                form = new MemoryDumperForm(this);
            }
            if (!form.Visible) form.Show(Owner);
            form.BringToFront();
            form.Activate();
        }

        // -- Unused iPlugin members --
        public void Quit() { form?.Close(); }
        public byte Read(eAccess _type, int _address, int _id, out bool _isvalid)
        {
            _isvalid = false; return 0;
        }
        public void Reset() { }
        public void Tick() { }
        public bool Write(eAccess _type, int _port, int _id, byte _value) { return false; }
    }
}

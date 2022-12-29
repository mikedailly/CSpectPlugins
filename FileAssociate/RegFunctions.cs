using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileAssociate
{
    // ************************************************************************************************************
    /// <summary>
    ///     The registry functions are in a seperate class, so that creating the plugin
    ///     shouldn't crash Linux/Mac, as the windows registry dependancies should only 
    ///     be loaded when THIS class is created.
    /// </summary>
    // ************************************************************************************************************
    public class RegFunctions
    {
        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const int SHCNE_ASSOCCHANGED = 0x8000000;
        private const int SHCNF_FLUSH = 0x1000;


        // ****************************************************************************************************************
        /// <summary>
        ///     Set the .NEX file assosoation
        /// </summary>
        // ****************************************************************************************************************
        public void EnsureAssociationsSet()
        {
            var filePath = Process.GetCurrentProcess().MainModule.FileName;
            bool madeChanges1 = SetAssociation(".nex", "NEX_Program_File", "ZX Spectrum Next program", filePath);
            bool madeChanges2 = SetAssociation(".snx", "SNX_Program_File", "ZX Spectrum Next program", filePath);
            if (madeChanges1 || madeChanges2)
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
                MessageBox.Show(".NEX and .SNX file association added.", "File Association Plugin");
            }
        }

        // ****************************************************************************************************************
        /// <summary>
        ///     Set file assosiation for windows
        /// </summary>
        /// <param name="extension">The extension to setup</param>
        /// <param name="progId">program id</param>
        /// <param name="fileTypeDescription">long description</param>
        /// <param name="applicationFilePath">path to exe to bind to</param>
        /// <returns>
        ///     True for okay, False for error
        /// </returns>
        // ****************************************************************************************************************
        public bool SetAssociation(string extension, string progId, string fileTypeDescription, string applicationFilePath)
        {
            bool madeChanges = false;
            madeChanges |= SetKeyDefaultValue(@"Software\Classes\" + extension, progId);
            madeChanges |= SetKeyDefaultValue(@"Software\Classes\" + progId, fileTypeDescription);
            madeChanges |= SetKeyDefaultValue($@"Software\Classes\{progId}\shell\open\command", "\"" + applicationFilePath + "\" \"%1\"");

            var CurrentUser = Registry.ClassesRoot.OpenSubKey(".nex\\DefaultIcon", true);
            if (CurrentUser == null) CurrentUser = Registry.ClassesRoot.CreateSubKey(".nex\\DefaultIcon");
            CurrentUser.SetValue("", applicationFilePath + "\\ZXNext.ico");

            return madeChanges;
        }

        // ****************************************************************************************************************
        /// <summary>
        ///     Set a key/value pair into the registry
        /// </summary>
        /// <param name="keyPath">the key we're setting</param>
        /// <param name="value">the value to set</param>
        /// <returns>
        ///     true for set okay, false for error
        /// </returns>
        // ****************************************************************************************************************
        private bool SetKeyDefaultValue(string keyPath, string value)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                if (key.GetValue(null) as string != value)
                {
                    key.SetValue(null, value);
                    return true;
                }
            }
            return false;
        }
    }
}

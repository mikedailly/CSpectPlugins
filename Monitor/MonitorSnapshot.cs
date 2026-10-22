// ********************************************************************************************************************************************
//      CSpect Monitor extension - a debugger front end: registers, disassembly, stack and memory in one window
//      Released under the GNU 3 license - please see license file for more details
// ********************************************************************************************************************************************
using Plugin;
using System.Globalization;

namespace Monitor
{
    // *********************************************************************************************************
    /// <summary>
    ///     One disassembled instruction
    /// </summary>
    // *********************************************************************************************************
    public sealed class DisasmLine
    {
        /// <summary>64k address, or a physical address if Physical</summary>
        public int Address;
        /// <summary>Disassembled from physical memory (Address is physical)</summary>
        public bool Physical;
        /// <summary>Instruction length in bytes</summary>
        public int Length;
        /// <summary>The instruction bytes</summary>
        public byte[] Bytes;
        /// <summary>Instruction text (labels already substituted by CSpect)</summary>
        public string Text;
        /// <summary>Label at this address, or null</summary>
        public string Label;
        /// <summary>Is there a breakpoint here?</summary>
        public bool Breakpoint;
        /// <summary>T-States (and the "branch taken" count, or 0)</summary>
        public int TStates1, TStates2;
    }

    // *********************************************************************************************************
    /// <summary>
    ///     Everything the window shows, captured on the emulator thread (MonitorPlugin.Tick).
    ///     Never modified after it's published, so the UI can read it without locking.
    /// </summary>
    // *********************************************************************************************************
    public sealed class MonitorSnapshot
    {
        /// <summary>Stopped in the debugger?</summary>
        public bool Stopped;
        /// <summary>Bumped every time the CPU is stepped or stops, so the UI can highlight what changed</summary>
        public int StopCount;
        public Z80Regs Regs;
        /// <summary>The CPU's 64k view of memory</summary>
        public byte[] Memory;
        /// <summary>MMU slots 0-7 (NextReg $50-$57)</summary>
        public byte[] Mmu;
        /// <summary>Disassembly for the lines the UI asked for</summary>
        public DisasmLine[] Disasm;
        /// <summary>Labels for the words at SP-8 .. SP+(2*StackWords) (index 0 = SP-8)</summary>
        public string[] StackLabels;
        public const int StackBelow = 4, StackWords = 48;
        /// <summary>CSpect's own debugger screen hidden ("remote" mode)?</summary>
        public bool Remote;
        /// <summary>Symbol auto-complete for each Goto box (MonitorPlugin.TargetDisasm/TargetMemory): the text, and what matched</summary>
        public string[] SymbolQuery = new string[2];
        public SymbolMatch[][] SymbolMatches = new SymbolMatch[2][];
        /// <summary>Physical memory the memory pane asked for (PhysMemory[0] is at PhysStart), or null</summary>
        public byte[] PhysMemory;
        public int PhysStart;

        /// <summary>Physical address -> 64k address, if its 8K bank is paged in right now (else -1)</summary>
        public int ToLogical(int _physical)
        {
            int bank = _physical >> 13;
            for (int slot = 0; slot < 8; slot++) if (Mmu[slot] == bank) return (slot << 13) | (_physical & 0x1fff);
            return -1;
        }

        /// <summary>64k address -> physical address through the MMU, or -1 for ROM</summary>
        public int ToPhysical(int _logical)
        {
            int bank = Mmu[(_logical >> 13) & 7];
            return bank == 0xff ? -1 : (bank << 13) | (_logical & 0x1fff);
        }
    }

    // *********************************************************************************************************
    /// <summary>
    ///     A symbol offered by a Goto box, already resolved to where it is now
    /// </summary>
    // *********************************************************************************************************
    public sealed class SymbolMatch
    {
        public string Name;
        public int Address;
        public bool Physical;
    }

    // *********************************************************************************************************
    /// <summary>
    ///     The result of a symbol Goto (resolved on the emulator thread)
    /// </summary>
    // *********************************************************************************************************
    public sealed class GotoResult
    {
        /// <summary>Goes up by one for every Goto, so the UI knows it's new</summary>
        public int Sequence;
        public int Address;
        public bool Physical;
        /// <summary>null if it worked, else why not</summary>
        public string Error;
    }

    // *********************************************************************************************************
    /// <summary>
    ///     Addresses as typed and shown:
    ///         $8000       hex (a leading $), else decimal: 32768
    ///         10:$0000    bank:offset - a physical address, 8K banks (bank * 8192 + offset), either part hex or decimal
    ///     Plain numbers up to 65535 are the CPU's 64k view; above that (or bank:offset) are physical memory,
    ///     which is shown as bank:offset.
    ///     Anything else is taken as a symbol name, optionally +/- an offset: main_loop, buffer+$20, table-2
    /// </summary>
    // *********************************************************************************************************
    public static class MonitorAddress
    {
        /// <summary>Size of physical memory (2MB, 256 8K banks)</summary>
        public const int PhysicalSize = 0x200000;
        public const int BankSize = 8192;

        public static bool TryParse(string _text, out int _address, out bool _physical)
        {
            _address = 0; _physical = false;
            if (_text == null) return false;
            string t = _text.Trim();
            int colon = t.IndexOf(':');
            if (colon >= 0)
            {
                int bank, offset;
                if (!TryNumber(t.Substring(0, colon), out bank) || !TryNumber(t.Substring(colon + 1), out offset)) return false;
                long a = (long)bank * BankSize + offset;
                if (a < 0 || a >= PhysicalSize) return false;
                _address = (int)a; _physical = true;
                return true;
            }
            int v;
            if (!TryNumber(t, out v) || v < 0 || v >= PhysicalSize) return false;
            _address = v; _physical = v > 0xffff;
            return true;
        }

        /// <summary>Split "name+offset" / "name-offset" (offset $hex or decimal). False if it can't be a symbol.</summary>
        public static bool TrySplitSymbol(string _text, out string _name, out int _offset)
        {
            _name = null; _offset = 0;
            string t = (_text ?? "").Trim();
            if (t.Length == 0 || char.IsDigit(t[0]) || t[0] == '$' || t[0] == '-' || t[0] == '+' || t.IndexOf(':') >= 0) return false;
            int op = -1;
            for (int i = 1; i < t.Length; i++) if (t[i] == '+' || t[i] == '-') { op = i; break; }
            if (op < 0) { _name = t; return true; }
            int off;
            if (!TryNumber(t.Substring(op + 1), out off)) return false;
            _name = t.Substring(0, op).Trim();
            _offset = t[op] == '-' ? -off : off;
            return _name.Length > 0;
        }

        /// <summary>$hex (or 0xhex), otherwise decimal</summary>
        public static bool TryNumber(string _text, out int _value)
        {
            _value = 0;
            string t = (_text ?? "").Trim();
            if (t.StartsWith("$")) return t.Length > 1 && int.TryParse(t.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _value);
            if (t.StartsWith("0x") || t.StartsWith("0X")) return t.Length > 2 && int.TryParse(t.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _value);
            return t.Length > 0 && int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out _value);
        }

        /// <summary>$XXXX for the 64k view, bank:$offset for physical memory</summary>
        public static string Format(int _address, bool _physical)
        {
            if (!_physical) return "$" + (_address & 0xffff).ToString("X4");
            return (_address / BankSize).ToString() + ":$" + (_address % BankSize).ToString("X4");
        }
    }
}

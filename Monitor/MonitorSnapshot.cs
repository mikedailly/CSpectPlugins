// ********************************************************************************************************************************************
//      CSpect Monitor extension - a debugger front end: registers, disassembly, stack and memory in one window
//      Released under the GNU 3 license - please see license file for more details
// ********************************************************************************************************************************************
using Plugin;
using System;
using System.Collections.Generic;
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
        /// <summary>...and is it only a physical (bank:offset) one? (drawn darker than a 64K one)</summary>
        public bool PhysicalBreakpoint;
        /// <summary>T-States (and the "branch taken" count, or 0)</summary>
        public int TStates1, TStates2;
    }

    // *********************************************************************************************************
    /// <summary>
    ///     One line of a source file, for source level debugging
    /// </summary>
    // *********************************************************************************************************
    public sealed class SourceRow
    {
        /// <summary>Line number (1 = first)</summary>
        public int Line;
        public string Text;
        /// <summary>Physical address of the line's code, or -1 if it has none (comments, blank lines, ...)</summary>
        public int Address = -1;
        public bool Breakpoint;
        /// <summary>...and is it only a physical one? (drawn darker)</summary>
        public bool PhysicalBreakpoint;
        /// <summary>Words in the code that are labels, as (start, length) pairs</summary>
        public int[] Labels = new int[0];
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
        public string[] SymbolQuery = new string[3];
        public SymbolMatch[][] SymbolMatches = new SymbolMatch[3][];
        /// <summary>Physical memory the memory pane asked for (PhysMemory[0] is at PhysStart), or null</summary>
        public byte[] PhysMemory;
        public int PhysStart;

        // ---- source level debugging (map file [FILES]/[LINES]) ----
        /// <summary>Source files from the map file (empty if it had none)</summary>
        public string[] SourceFiles = new string[0];
        /// <summary>Source view on? (and it's showing SourceFile from SourceRows[0].Line)</summary>
        public bool SourceMode;
        public int SourceFile = -1;
        public SourceRow[] SourceRows = new SourceRow[0];
        /// <summary>The source line PC is on (null if PC's code has no source line)</summary>
        public SourceLocation PCSource;

        /// <summary>Each watch tab's watches, in order</summary>
        public WatchResult[][] WatchSets = { new WatchResult[0], new WatchResult[0], new WatchResult[0], new WatchResult[0] };

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
    ///     One watch, worked out on the emulator thread every frame
    /// </summary>
    // *********************************************************************************************************
    public sealed class WatchResult
    {
        /// <summary>The watch as typed</summary>
        public string Expr;
        /// <summary>The address it looked at ($XXXX or bank:$offset), or null</summary>
        public string Where;
        public int Address = -1;
        public bool Physical;
        /// <summary>The value(s) - or what's wrong with the watch</summary>
        public string Value;
        /// <summary>More detail (decimal values, the register), for a tooltip</summary>
        public string Tip;
        public bool Error;
    }

    // *********************************************************************************************************
    /// <summary>
    ///     The result of a command line (run on the emulator thread)
    /// </summary>
    // *********************************************************************************************************
    public sealed class CommandResult
    {
        /// <summary>Goes up by one for every command, so the UI knows it's new</summary>
        public int Sequence;
        public string Command;
        /// <summary>What CSpect said (null = nothing)</summary>
        public string Message;
        public bool Error;
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
    ///     Picking a source line apart: where its ';' comment starts, and its words (for label colouring)
    /// </summary>
    // *********************************************************************************************************
    public static class SourceText
    {
        /// <summary>Where the ';' comment starts (a ';' in quotes doesn't count), or -1</summary>
        public static int CommentStart(string _text)
        {
            char quote = '\0';
            for (int i = 0; i < _text.Length; i++)
            {
                char ch = _text[i];
                if (quote != '\0') { if (ch == quote) quote = '\0'; }
                else if (IsQuote(_text, i)) quote = ch;
                else if (ch == ';') return i;
            }
            return -1;
        }

        /// <summary>A quote that starts a string - not the ' of AF' (after a letter or digit)</summary>
        static bool IsQuote(string _text, int _i)
        {
            char ch = _text[_i];
            return ch == '"' || (ch == '\'' && !(_i > 0 && char.IsLetterOrDigit(_text[_i - 1])));
        }

        public static bool IsWordChar(char _c) { return char.IsLetterOrDigit(_c) || _c == '_' || _c == '.' || _c == '@' || _c == '!'; }

        /// <summary>
        ///     The numbers before _end, as (start, length): $hex #hex 0xhex %binary decimal, and 0C000h / 0101b -
        ///     a whole word (not part of a longer one), not in quotes
        /// </summary>
        public static List<int> Numbers(string _text, int _end)
        {
            var nums = new List<int>();
            char quote = '\0';
            int i = 0;
            while (i < _end)
            {
                char ch = _text[i];
                if (quote != '\0') { if (ch == quote) quote = '\0'; i++; continue; }
                if (IsQuote(_text, i)) { quote = ch; i++; continue; }
                bool boundary = i == 0 || !(IsWordChar(_text[i - 1]) || _text[i - 1] == ')');
                if (!boundary) { i++; continue; }
                int prefix = (ch == '$' || ch == '#' || ch == '%') ? 1 : 0;
                int start = i + prefix;
                int stop = start;
                while (stop < _end && IsWordChar(_text[stop])) stop++;
                string word = _text.Substring(start, stop - start);
                if (word.Length > 0 && IsNumber(word, ch, prefix == 1)) { nums.Add(i); nums.Add(stop - i); i = stop; continue; }
                i = Math.Max(i + 1, prefix == 1 ? i + 1 : stop);
            }
            return nums;
        }

        static bool IsNumber(string _w, char _prefix, bool _hasPrefix)
        {
            if (_hasPrefix) return _prefix == '%' ? All(_w, "01") : All(_w, HEX);
            if (!char.IsDigit(_w[0])) return false;
            if (_w.Length > 2 && (_w[1] == 'x' || _w[1] == 'X') && _w[0] == '0') return All(_w.Substring(2), HEX);
            char last = char.ToLowerInvariant(_w[_w.Length - 1]);
            if (_w.Length > 1 && last == 'h') return All(_w.Substring(0, _w.Length - 1), HEX);
            if (_w.Length > 1 && last == 'b' && All(_w.Substring(0, _w.Length - 1), "01")) return true;
            return All(_w, "0123456789");
        }
        const string HEX = "0123456789abcdefABCDEF";
        static bool All(string _s, string _chars)
        {
            if (_s.Length == 0) return false;
            foreach (char c in _s) if (_chars.IndexOf(c) < 0) return false;
            return true;
        }

        /// <summary>
        ///     The words before _end that could be labels, as (start, length) - not in quotes, not numbers
        ///     (a digit first, or after $ % #). Anything that isn't a word character splits words.
        /// </summary>
        public static List<int> Words(string _text, int _end)
        {
            var words = new List<int>();
            char quote = '\0';
            int i = 0;
            while (i < _end)
            {
                char ch = _text[i];
                if (quote != '\0') { if (ch == quote) quote = '\0'; i++; continue; }
                if (IsQuote(_text, i)) { quote = ch; i++; continue; }
                if (!IsWordChar(ch)) { i++; continue; }
                int start = i;
                while (i < _end && IsWordChar(_text[i])) i++;
                bool number = char.IsDigit(_text[start]) || (start > 0 && (_text[start - 1] == '$' || _text[start - 1] == '%' || _text[start - 1] == '#'));
                if (!number) { words.Add(start); words.Add(i - start); }
            }
            return words;
        }
    }

    // *********************************************************************************************************
    /// <summary>
    ///     A calculation, for watch addresses: $1234+hl+(a*4), ix+label, table+(b<<1)...
    ///         operators   + - * / %   &amp; | ^ &lt;&lt; &gt;&gt;   unary - + ~   ( )   (C precedence)
    ///         numbers     $hex, 0xhex, %binary, decimal
    ///         names       anything else - registers and labels, looked up by the caller
    /// </summary>
    // *********************************************************************************************************
    public static class WatchExpression
    {
        /// <summary>Work out _text. _lookup gives a name's value, or null if it doesn't know it.</summary>
        public static bool TryEval(string _text, Func<string, long?> _lookup, out long _value, out string _error)
        {
            var p = new Parser { Text = _text ?? "", Lookup = _lookup };
            _value = 0; _error = null;
            try
            {
                p.Skip();
                if (p.Pos >= p.Text.Length) throw new FormatException("Expected an address, label or register");
                _value = p.Or();
                p.Skip();
                if (p.Pos < p.Text.Length) throw new FormatException("Unexpected '" + p.Text.Substring(p.Pos) + "'");
                return true;
            }
            catch (FormatException ex) { _error = ex.Message; return false; }
        }

        sealed class Parser
        {
            public string Text;
            public int Pos;
            public Func<string, long?> Lookup;

            public void Skip() { while (Pos < Text.Length && char.IsWhiteSpace(Text[Pos])) Pos++; }
            bool Take(string _op)
            {
                Skip();
                if (string.CompareOrdinal(Text, Pos, _op, 0, _op.Length) != 0) return false;
                // don't take "<" out of "<<" etc.
                if (_op.Length == 1 && Pos + 1 < Text.Length && (_op == "<" || _op == ">") && Text[Pos + 1] == _op[0]) return false;
                Pos += _op.Length;
                return true;
            }

            public long Or() { long v = Xor(); while (Take("|")) v |= Xor(); return v; }
            long Xor() { long v = And(); while (Take("^")) v ^= And(); return v; }
            long And() { long v = Shift(); while (Take("&")) v &= Shift(); return v; }
            long Shift()
            {
                long v = Add();
                while (true)
                {
                    if (Take("<<")) v <<= (int)Math.Min(63, Math.Max(0, Add()));
                    else if (Take(">>")) v >>= (int)Math.Min(63, Math.Max(0, Add()));
                    else return v;
                }
            }
            long Add()
            {
                long v = Mul();
                while (true)
                {
                    if (Take("+")) v += Mul();
                    else if (Take("-")) v -= Mul();
                    else return v;
                }
            }
            long Mul()
            {
                long v = Unary();
                while (true)
                {
                    if (Take("*")) v *= Unary();
                    else if (Take("/")) { long d = Unary(); if (d == 0) throw new FormatException("Divide by zero"); v /= d; }
                    else if (Take("%")) { long d = Unary(); if (d == 0) throw new FormatException("Divide by zero"); v %= d; }
                    else return v;
                }
            }
            long Unary()
            {
                if (Take("-")) return -Unary();
                if (Take("+")) return Unary();
                if (Take("~")) return ~Unary();
                return Primary();
            }
            long Primary()
            {
                Skip();
                if (Pos >= Text.Length) throw new FormatException("Expression ends too soon");
                char c = Text[Pos];
                if (c == '(')
                {
                    Pos++;
                    long v = Or();
                    if (!Take(")")) throw new FormatException("Missing ')'");
                    return v;
                }
                if (c == '$') { Pos++; return Digits(16, "hex"); }
                if (c == '%') { Pos++; return Digits(2, "binary"); }
                if (c == '0' && Pos + 1 < Text.Length && (Text[Pos + 1] == 'x' || Text[Pos + 1] == 'X')) { Pos += 2; return Digits(16, "hex"); }
                if (char.IsDigit(c)) return Digits(10, "decimal");
                if (char.IsLetter(c) || c == '_' || c == '@' || c == '.')
                {
                    int start = Pos;
                    while (Pos < Text.Length && (char.IsLetterOrDigit(Text[Pos]) || Text[Pos] == '_' || Text[Pos] == '@' || Text[Pos] == '.' || Text[Pos] == '!')) Pos++;
                    if (Pos < Text.Length && Text[Pos] == '\'') Pos++;          // AF', HL'...
                    string name = Text.Substring(start, Pos - start);
                    long? v = Lookup(name);
                    if (v == null) throw new FormatException("Unknown label '" + name + "'");
                    return v.Value;
                }
                throw new FormatException("Unexpected '" + c + "'");
            }
            long Digits(int _base, string _what)
            {
                long v = 0; int n = 0;
                while (Pos < Text.Length)
                {
                    int d = HexDigit(Text[Pos]);
                    if (d < 0 || d >= _base) break;
                    v = v * _base + d; Pos++; n++;
                }
                if (n == 0) throw new FormatException("Expected a " + _what + " number");
                return v;
            }
            static int HexDigit(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }
        }
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

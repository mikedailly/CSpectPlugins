// ********************************************************************************************************************************************
//      CSpect MCP server - address/value expressions: numbers, symbols, registers and simple maths
//      Written by:
//                  Mike Dailly
//
//      Released under the GNU 3 license - please see license file for more details
//
//      Numbers: $8000 #8000 0x8000 8000h %1010 0b1010 32768 'A'.  Operators (C precedence): unary - ~ !,
//      * / %, + -, << >>, &, ^, |.  Registers: A F B C D E H L I R AF BC DE HL IX IY SP PC IXH IXL IYH IYL and
//      the shadow set AF' BC' DE' HL'.  Anything else is looked up as a symbol. Call on the emulator thread.
// ********************************************************************************************************************************************
using Plugin;
using System;
using System.Globalization;

namespace MCP
{
    // *********************************************************************************************************
    /// <summary>
    ///     Expression evaluator
    /// </summary>
    // *********************************************************************************************************
    public class Expr
    {
        readonly iCSpect CSpect;
        readonly string Text;
        int Pos;
        Z80Regs Regs;

        Expr(iCSpect _cspect, string _text) { CSpect = _cspect; Text = _text; }

        // ****************************************************************************************
        /// <summary>
        ///     Evaluate (emulator thread). Numbers in JSON come through as plain numbers.
        /// </summary>
        // ****************************************************************************************
        public static long Eval(iCSpect _cspect, object _value, string _what = "value")
        {
            if (_value == null) throw new ArgumentException("No " + _what + " given");
            if (_value is double) return (long)(double)_value;
            string s = _value.ToString().Trim();
            if (s.Length == 0) throw new ArgumentException("No " + _what + " given");
            Expr e = new Expr(_cspect, s);
            long v = e.Or();
            e.SkipWhite();
            if (e.Pos != s.Length) throw new ArgumentException("Can't understand " + _what + " '" + s + "' at '" + s.Substring(e.Pos) + "'");
            return v;
        }

        void SkipWhite() { while (Pos < Text.Length && char.IsWhiteSpace(Text[Pos])) Pos++; }

        bool Take(string _op)
        {
            SkipWhite();
            if (string.CompareOrdinal(Text, Pos, _op, 0, _op.Length) != 0) return false;
            // don't take '<' of '<<' etc.
            if (_op.Length == 1 && Pos + 1 < Text.Length && (_op == "<" || _op == ">") && Text[Pos + 1] == _op[0]) return false;
            Pos += _op.Length;
            return true;
        }

        long Or() { long v = Xor(); while (Take("|")) v |= Xor(); return v; }
        long Xor() { long v = And(); while (Take("^")) v ^= And(); return v; }
        long And() { long v = Shift(); while (Take("&")) v &= Shift(); return v; }

        long Shift()
        {
            long v = Add();
            while (true)
            {
                if (Take("<<")) v <<= (int)Add();
                else if (Take(">>")) v >>= (int)Add();
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
                else if (Take("/")) { long d = Unary(); if (d == 0) throw new ArgumentException("Divide by zero"); v /= d; }
                else if (PeekModulo()) { Pos++; long d = Unary(); if (d == 0) throw new ArgumentException("Divide by zero"); v %= d; }
                else return v;
            }
        }

        /// <summary>'%' is modulo after a value, binary before digits</summary>
        bool PeekModulo()
        {
            SkipWhite();
            if (Pos >= Text.Length || Text[Pos] != '%') return false;
            return true;
        }

        long Unary()
        {
            if (Take("-")) return -Unary();
            if (Take("+")) return Unary();
            if (Take("~")) return ~Unary();
            if (Take("!")) return Unary() == 0 ? 1 : 0;
            return Primary();
        }

        long Primary()
        {
            SkipWhite();
            if (Pos >= Text.Length) throw new ArgumentException("Expression ends too soon");
            char c = Text[Pos];
            if (c == '(' || c == '[')
            {
                char close = c == '(' ? ')' : ']';
                Pos++;
                long v = Or();
                SkipWhite();
                if (Pos >= Text.Length || Text[Pos] != close) throw new ArgumentException("Missing '" + close + "'");
                Pos++;
                return v;
            }
            if (c == '$' || c == '#')
            {
                Pos++;
                if (c == '$' && (Pos >= Text.Length || !IsHex(Text[Pos])))
                {
                    // "$" alone = PC
                    return GetRegs().PC;
                }
                return Digits(16);
            }
            if (c == '%') { Pos++; return Digits(2); }
            if (c == '\'' && Pos + 2 < Text.Length && Text[Pos + 2] == '\'') { long ch = Text[Pos + 1]; Pos += 3; return ch; }
            if (char.IsDigit(c)) return Number();
            if (char.IsLetter(c) || c == '_' || c == '.' || c == '@')
            {
                int start = Pos;
                while (Pos < Text.Length && (char.IsLetterOrDigit(Text[Pos]) || Text[Pos] == '_' || Text[Pos] == '.' || Text[Pos] == '@' || Text[Pos] == '?')) Pos++;
                if (Pos < Text.Length && Text[Pos] == '\'') Pos++;          // AF' etc.
                return Name(Text.Substring(start, Pos - start));
            }
            throw new ArgumentException("Unexpected '" + c + "'");
        }

        static bool IsHex(char _c) { return Uri.IsHexDigit(_c); }

        long Digits(int _base)
        {
            int start = Pos;
            while (Pos < Text.Length && (IsHex(Text[Pos]) || Text[Pos] == '_')) Pos++;
            string s = Text.Substring(start, Pos - start).Replace("_", "");
            if (s.Length == 0) throw new ArgumentException("Number expected");
            return Convert.ToInt64(s, _base);
        }

        long Number()
        {
            // 0x / 0b prefixes, h / b suffixes, else decimal
            if (Text[Pos] == '0' && Pos + 1 < Text.Length && (Text[Pos + 1] == 'x' || Text[Pos + 1] == 'X')) { Pos += 2; return Digits(16); }
            if (Text[Pos] == '0' && Pos + 2 < Text.Length && (Text[Pos + 1] == 'b' || Text[Pos + 1] == 'B') && (Text[Pos + 2] == '0' || Text[Pos + 2] == '1')) { Pos += 2; return Digits(2); }
            int start = Pos;
            while (Pos < Text.Length && (IsHex(Text[Pos]) || Text[Pos] == '_')) Pos++;
            string s = Text.Substring(start, Pos - start).Replace("_", "");
            if (Pos < Text.Length && (Text[Pos] == 'h' || Text[Pos] == 'H')) { Pos++; return Convert.ToInt64(s, 16); }
            // 1010b - a binary suffix eaten as a hex digit
            if ((s.EndsWith("b") || s.EndsWith("B")) && s.Substring(0, s.Length - 1).Trim('0', '1').Length == 0 && s.Length > 1)
                return Convert.ToInt64(s.Substring(0, s.Length - 1), 2);
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long d)) return d;
            throw new ArgumentException("Bad number '" + s + "' (use $ or 0x for hex)");
        }

        Z80Regs GetRegs()
        {
            if (Regs == null) Regs = CSpect.GetRegs();
            return Regs;
        }

        long Name(string _name)
        {
            long r;
            if (Register(_name, out r)) return r;
            Symbol s = CSpect.FindSymbol(_name);
            if (s == null && _name.StartsWith("@")) s = CSpect.FindSymbol(_name.Substring(1));
            if (s == null) throw new ArgumentException("Unknown symbol '" + _name + "'");
            return s.value;
        }

        bool Register(string _name, out long _value)
        {
            _value = 0;
            string n = _name.ToUpperInvariant();
            Z80Regs r;
            switch (n)
            {
                case "A": case "F": case "B": case "C": case "D": case "E": case "H": case "L":
                case "I": case "R": case "AF": case "BC": case "DE": case "HL": case "IX": case "IY": case "SP": case "PC":
                case "IXH": case "IXL": case "IYH": case "IYL": case "AF'": case "BC'": case "DE'": case "HL'":
                    r = GetRegs();
                    break;
                default:
                    return false;
            }
            int v = RegisterValue(r, n);
            if (v < 0) return false;
            _value = v;
            return true;
        }

        // ****************************************************************************************
        /// <summary>
        ///     A register by name (upper case), or -1
        /// </summary>
        // ****************************************************************************************
        public static int RegisterValue(Z80Regs r, string _name)
        {
            switch (_name)
            {
                case "A": return r.AF >> 8;
                case "F": return r.AF & 0xff;
                case "B": return r.BC >> 8;
                case "C": return r.BC & 0xff;
                case "D": return r.DE >> 8;
                case "E": return r.DE & 0xff;
                case "H": return r.HL >> 8;
                case "L": return r.HL & 0xff;
                case "I": return r.I;
                case "R": return r.R;
                case "AF": return r.AF;
                case "BC": return r.BC;
                case "DE": return r.DE;
                case "HL": return r.HL;
                case "IX": return r.IX;
                case "IY": return r.IY;
                case "SP": return r.SP;
                case "PC": return r.PC;
                case "IXH": return r.IX >> 8;
                case "IXL": return r.IX & 0xff;
                case "IYH": return r.IY >> 8;
                case "IYL": return r.IY & 0xff;
                case "AF'": return r._AF;
                case "BC'": return r._BC;
                case "DE'": return r._DE;
                case "HL'": return r._HL;
                case "IM": return r.IM;
            }
            return -1;
        }

        // ****************************************************************************************
        /// <summary>
        ///     Set a register by name (upper case); false if there's no such register
        /// </summary>
        // ****************************************************************************************
        public static bool SetRegister(Z80Regs r, string _name, int v)
        {
            ushort w = (ushort)v;
            byte b = (byte)v;
            switch (_name)
            {
                case "A": r.AF = (ushort)((r.AF & 0x00ff) | (b << 8)); return true;
                case "F": r.AF = (ushort)((r.AF & 0xff00) | b); return true;
                case "B": r.BC = (ushort)((r.BC & 0x00ff) | (b << 8)); return true;
                case "C": r.BC = (ushort)((r.BC & 0xff00) | b); return true;
                case "D": r.DE = (ushort)((r.DE & 0x00ff) | (b << 8)); return true;
                case "E": r.DE = (ushort)((r.DE & 0xff00) | b); return true;
                case "H": r.HL = (ushort)((r.HL & 0x00ff) | (b << 8)); return true;
                case "L": r.HL = (ushort)((r.HL & 0xff00) | b); return true;
                case "I": r.I = b; return true;
                case "R": r.R = b; return true;
                case "AF": r.AF = w; return true;
                case "BC": r.BC = w; return true;
                case "DE": r.DE = w; return true;
                case "HL": r.HL = w; return true;
                case "IX": r.IX = w; return true;
                case "IY": r.IY = w; return true;
                case "SP": r.SP = w; return true;
                case "PC": r.PC = w; return true;
                case "IXH": r.IX = (ushort)((r.IX & 0x00ff) | (b << 8)); return true;
                case "IXL": r.IX = (ushort)((r.IX & 0xff00) | b); return true;
                case "IYH": r.IY = (ushort)((r.IY & 0x00ff) | (b << 8)); return true;
                case "IYL": r.IY = (ushort)((r.IY & 0xff00) | b); return true;
                case "AF'": r._AF = w; return true;
                case "BC'": r._BC = w; return true;
                case "DE'": r._DE = w; return true;
                case "HL'": r._HL = w; return true;
                case "IM": r.IM = (byte)(v & 3); return true;
                case "IFF1": r.IFF1 = v != 0; return true;
                case "IFF2": r.IFF2 = v != 0; return true;
            }
            return false;
        }
    }
}

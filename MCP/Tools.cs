// ********************************************************************************************************************************************
//      CSpect MCP server - the tools
//      Written by:
//                  Mike Dailly
//
//      Released under the GNU 3 license - please see license file for more details
//
//      Handlers run on a socket thread; anything touching the machine goes through Plugin.Emu(), which runs it on
//      the emulator thread (in Tick) and waits.
// ********************************************************************************************************************************************
using Plugin;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MCP
{
    // *********************************************************************************************************
    /// <summary>
    ///     Tool definitions and handlers
    /// </summary>
    // *********************************************************************************************************
    public static class Tools
    {
        static MCPPlugin P;
        static iCSpect CS { get { return P.CSpect; } }

        const string ADDR = "Address: a number ($8000, 0x8000, 32768), a symbol, a register, or maths (label+4)";
        const int MAX_WAIT_MS = 120000;
        const int PHYSICAL_SIZE = 0x200000;         // 2MB Next RAM (physical breakpoints go to 4MB)

        static string H2(int _v) { return "$" + (_v & 0xff).ToString("X2"); }
        static string H4(int _v) { return "$" + (_v & 0xffff).ToString("X4"); }
        static string H6(int _v) { return "$" + _v.ToString("X6"); }

        // ****************************************************************************************
        /// <summary>
        ///     All the tools
        /// </summary>
        // ****************************************************************************************
        public static List<Tool> Build(MCPPlugin _plugin)
        {
            P = _plugin;
            List<Tool> t = new List<Tool>();

            // ---------------- state ----------------
            t.Add(new Tool
            {
                Name = "get_status",
                Description = "Is the CPU running or stopped, plus registers, flags, the MMU banks paged into each 8K slot, and the source line at PC.",
                ReadOnly = true,
                Handler = a => ToolResult.Text(P.Emu(() => Status()))
            });

            t.Add(new Tool
            {
                Name = "set_registers",
                Description = "Set Z80 registers. Best done while stopped (pause first). e.g. {\"registers\":{\"HL\":\"$4000\",\"A\":5,\"PC\":\"main\"}}. " +
                              "Names: A F B C D E H L I R AF BC DE HL IX IY IXH IXL IYH IYL SP PC AF' BC' DE' HL' IM IFF1 IFF2.",
                Handler = SetRegisters
            }.Param("registers", "object", "Register name -> value (number, symbol or expression)", true));

            // ---------------- memory ----------------
            t.Add(new Tool
            {
                Name = "read_memory",
                Description = "Hex dump of memory (with ASCII). The Z80's 64K view, or physical Next RAM with physical=true.",
                ReadOnly = true,
                Handler = ReadMemory
            }.Param("address", "string", ADDR, true)
             .Param("length", "integer", "Bytes to read (default 128, max 16384)")
             .Param("physical", "boolean", "Address is physical (bank8k*$2000+offset) rather than the 64K view"));

            t.Add(new Tool
            {
                Name = "write_memory",
                Description = "Write bytes to memory: data as hex (\"3E 01 C9\", \"3E01C9\", \"$3E,$01\") and/or text as ASCII.",
                Handler = WriteMemory
            }.Param("address", "string", ADDR, true)
             .Param("data", "string", "Bytes in hex")
             .Param("text", "string", "ASCII text to write (after data, if both are given)")
             .Param("physical", "boolean", "Address is physical rather than the 64K view"));

            t.Add(new Tool
            {
                Name = "disassemble",
                Description = "Disassemble instructions, with labels, bytes, T-states and breakpoints. '>' marks PC.",
                ReadOnly = true,
                Handler = Disassemble
            }.Param("address", "string", ADDR + ". Default PC")
             .Param("count", "integer", "Instructions (default 20, max 500)")
             .Param("physical", "boolean", "Address is physical rather than the 64K view"));

            // ---------------- execution ----------------
            t.Add(new Tool
            {
                Name = "pause",
                Description = "Stop the CPU (enter the debugger) and return the status.",
                Handler = a => Pause()
            });

            t.Add(new Tool
            {
                Name = "run",
                Description = "Carry on running from PC. Returns straight away; use wait_for_stop to wait for a breakpoint.",
                Handler = a => { P.Emu(() => CS.Debugger(eDebugCommand.Run)); return ToolResult.Text("Running"); }
            });

            t.Add(new Tool
            {
                Name = "step",
                Description = "Execute instructions one at a time (CPU must be stopped). Returns the status and the next instructions.",
                Handler = Step
            }.Param("count", "integer", "Instructions to step (default 1, max 100000)"));

            t.Add(new Tool
            {
                Name = "step_over",
                Description = "Step over the instruction at PC: CALL, RST, DJNZ, LDIR etc run to the next instruction (CPU must be stopped).",
                Handler = StepOver
            }.Param("timeout_ms", "integer", "How long to wait for a CALL to return (default 5000)"));

            t.Add(new Tool
            {
                Name = "step_back",
                Description = "Rewind one instruction (CSpect keeps a history while stepping). CPU must be stopped.",
                Handler = a => P.Emu(() =>
                {
                    if (CS.Debugger(eDebugCommand.GetState) == 0) return ToolResult.Error("The CPU is running - pause first");
                    CS.Debugger(eDebugCommand.UnStep);
                    return ToolResult.Text(Status() + "\n\n" + DisasmText(CS.GetRegs().PC, 6, false));
                })
            });

            t.Add(new Tool
            {
                Name = "run_to",
                Description = "Run until PC reaches an address (a temporary breakpoint), or any other breakpoint hits, or the timeout. " +
                              "On a timeout the CPU keeps running and the temporary breakpoint stays until the next stop.",
                Handler = RunTo
            }.Param("address", "string", ADDR, true)
             .Param("physical", "boolean", "Address is physical (only stops when that bank is paged in)")
             .Param("timeout_ms", "integer", "How long to wait (default 10000, max 120000)"));

            t.Add(new Tool
            {
                Name = "wait_for_stop",
                Description = "Wait until the CPU stops (a breakpoint, or pause from elsewhere). Returns the status.",
                Handler = WaitForStop
            }.Param("timeout_ms", "integer", "How long to wait (default 10000, max 120000)"));

            // ---------------- breakpoints ----------------
            t.Add(new Tool
            {
                Name = "set_breakpoint",
                Description = "Set a breakpoint. execute (default) stops before running the instruction there; read/write stop on memory access (64K view only). " +
                              "Port breakpoints: use debug_command with BR (type 3 OutPort, 4 InPort).",
                Handler = a => Breakpoint(a, true)
            }.Param("address", "string", ADDR, true)
             .EnumParam("type", new[] { "execute", "read", "write" }, "Breakpoint type (default execute)")
             .Param("physical", "boolean", "execute only: a physical address, so it only stops in that bank"));

            t.Add(new Tool
            {
                Name = "clear_breakpoint",
                Description = "Clear a breakpoint, or all of them with all=true.",
                Handler = a => Breakpoint(a, false)
            }.Param("address", "string", ADDR)
             .EnumParam("type", new[] { "execute", "read", "write" }, "Breakpoint type (default execute)")
             .Param("physical", "boolean", "execute only: a physical address")
             .Param("all", "boolean", "Clear every breakpoint of every type"));

            t.Add(new Tool
            {
                Name = "list_breakpoints",
                Description = "List the breakpoints: execute (64K and physical), memory read/write and port in/out.",
                ReadOnly = true,
                Handler = a => ToolResult.Text(P.Emu(() => ListBreakpoints(), 15000))
            });

            // ---------------- symbols / source ----------------
            t.Add(new Tool
            {
                Name = "evaluate",
                Description = "Evaluate an expression (numbers, symbols, registers, + - * / & | ^ << >>), with the symbol and source line at the result.",
                ReadOnly = true,
                Handler = Evaluate
            }.Param("expression", "string", "e.g. \"main+4\", \"hl+2*b\", \"$C000|%101\"", true));

            t.Add(new Tool
            {
                Name = "find_symbols",
                Description = "Find symbols (labels, equates) whose names contain some text.",
                ReadOnly = true,
                Handler = FindSymbols
            }.Param("text", "string", "Text to look for (case insensitive)", true)
             .Param("max", "integer", "Most results (default 50)"));

            t.Add(new Tool
            {
                Name = "get_source",
                Description = "Show the source lines around an address (needs a SNASM map with [LINES]), or around file+line. '>' marks the line.",
                ReadOnly = true,
                Handler = GetSource
            }.Param("address", "string", ADDR + ". Default PC")
             .Param("physical", "boolean", "Address is physical")
             .Param("file", "string", "Source file name (or index) instead of an address")
             .Param("line", "integer", "Line in that file")
             .Param("before", "integer", "Lines before (default 6)")
             .Param("after", "integer", "Lines after (default 12)"));

            // ---------------- hardware ----------------
            t.Add(new Tool
            {
                Name = "read_next_registers",
                Description = "Read Next registers. Default: all 256 as a table.",
                ReadOnly = true,
                Handler = ReadNextRegisters
            }.Param("registers", "string", "Which: \"$50-$57,$07\" (default all)"));

            t.Add(new Tool
            {
                Name = "write_next_register",
                Description = "Write a Next register.",
                Handler = a =>
                {
                    return P.Emu(() =>
                    {
                        int r = (int)Expr.Eval(CS, a.Raw("register"), "register") & 0xff;
                        int v = (int)Expr.Eval(CS, a.Raw("value")) & 0xff;
                        CS.SetNextRegister((byte)r, (byte)v);
                        return ToolResult.Text("NextReg " + H2(r) + " = " + H2(v) + " (reads back " + H2(CS.GetNextRegister((byte)r)) + ")");
                    });
                }
            }.Param("register", "string", "Register number", true)
             .Param("value", "string", "Value", true));

            t.Add(new Tool
            {
                Name = "port_in",
                Description = "Read an I/O port (as the Z80's IN would - may have side effects).",
                Handler = a => P.Emu(() =>
                {
                    int port = (int)Expr.Eval(CS, a.Raw("port"), "port") & 0xffff;
                    int v = CS.InPort((ushort)port);
                    return ToolResult.Text("IN " + H4(port) + " = " + H2(v) + " (" + v + ", %" + Convert.ToString(v, 2).PadLeft(8, '0') + ")");
                })
            }.Param("port", "string", "16 bit port number", true));

            t.Add(new Tool
            {
                Name = "port_out",
                Description = "Write an I/O port (as the Z80's OUT would).",
                Handler = a => P.Emu(() =>
                {
                    int port = (int)Expr.Eval(CS, a.Raw("port"), "port") & 0xffff;
                    int v = (int)Expr.Eval(CS, a.Raw("value")) & 0xff;
                    CS.OutPort((ushort)port, (byte)v);
                    return ToolResult.Text("OUT " + H4(port) + ", " + H2(v));
                })
            }.Param("port", "string", "16 bit port number", true)
             .Param("value", "string", "Byte to write", true));

            t.Add(new Tool
            {
                Name = "debug_command",
                Description = "Run a CSpect debugger command line, as typed into its debugger: BR (breakpoints, incl. port: type 3 OutPort, 4 InPort), " +
                              "WRITE/READ (memory breakpoints), PWRITE, INPORT, OUTPORT, PUSH, POP, POKE, OUT, NEXTREG, SET/CLR flags, LOG, " +
                              "SAVE \"file\" addr,len, LOAD \"file\" addr, TONE, NEXTBRK, register assignments such as \"HL $4000\".",
                Handler = a => P.Emu(() =>
                {
                    string cmd = a.String("command", "");
                    if (cmd.Trim().Length == 0) return ToolResult.Error("No command");
                    string said = CS.DebugCommand(cmd);
                    return ToolResult.Text(string.IsNullOrEmpty(said) ? "OK" : said);
                })
            }.Param("command", "string", "The command line", true));

            // ---------------- display / input ----------------
            t.Add(new Tool
            {
                Name = "screenshot",
                Description = "The Spectrum display (including border) as a PNG. scale 2 (default) = 640x512, 1 = 320x256.",
                ReadOnly = true,
                Handler = Screenshot
            }.Param("scale", "integer", "1 or 2 (default 2)"));

            t.Add(new Tool
            {
                Name = "press_keys",
                Description = "Press Spectrum keys, one after another. Each item is a key or keys held together joined by '+', separated by spaces or commas: " +
                              "\"CAPS+5 ENTER\", \"SYM+P\", \"Q,Q,SPACE\". Keys: A-Z 0-9 ENTER SPACE CAPS SYM, plus BREAK DELETE LEFT RIGHT UP DOWN EDIT EXTEND GRAPHICS " +
                              "CAPSLOCK TRUEVIDEO INVVIDEO. Plays while the CPU runs; waits until done.",
                Handler = PressKeys
            }.Param("keys", "string", "The keys", true)
             .Param("hold_frames", "integer", "Frames to hold each (default 3)")
             .Param("gap_frames", "integer", "Frames between keys (default 3)"));

            t.Add(new Tool
            {
                Name = "type_text",
                Description = "Type text on the Spectrum keyboard: letters (upper case uses CAPS), digits, space, newline = ENTER, and the symbols on SYM " +
                              "(! @ # $ % & ' ( ) _ < > ; \" = + - ^ : £ ? / * , .). In 48K BASIC's K mode letters type keywords. Waits until typed.",
                Handler = TypeText
            }.Param("text", "string", "The text", true)
             .Param("hold_frames", "integer", "Frames to hold each key (default 3)")
             .Param("gap_frames", "integer", "Frames between keys (default 3)"));

            t.Add(new Tool
            {
                Name = "reset_machine",
                Description = "Reset the Next: hard (default, as F3) or soft (as NextReg $02). Returns the status.",
                Handler = ResetMachine
            }.Param("hard", "boolean", "true = hard reset (default), false = soft reset"));

            t.Add(new Tool
            {
                Name = "load_nex",
                Description = "Load and run a .nex file (full path), or reload the current one with no path.",
                Handler = a =>
                {
                    string path = a.String("path", "");
                    P.Emu(() => CS.LoadNex(path));
                    return ToolResult.Text(path.Length > 0 ? "Loading " + path : "Reloading");
                }
            }.Param("path", "string", "Path to the .nex file"));

            return t;
        }

        // ******************************************************************************************
        //  Helpers (emulator thread)
        // ******************************************************************************************
        static bool Stopped() { return CS.Debugger(eDebugCommand.GetState) != 0; }

        /// <summary>64k address -> physical address through the MMU right now, or -1 for ROM</summary>
        static int ToPhysical(int _address)
        {
            int bank = CS.GetNextRegister((byte)(0x50 + ((_address >> 13) & 7)));
            return bank == 0xff ? -1 : (bank << 13) | (_address & 0x1fff);
        }

        /// <summary>Physical -> 64k address if that bank is paged in, or -1</summary>
        static int ToLogical(int _physical)
        {
            int bank = _physical >> 13;
            for (int slot = 0; slot < 8; slot++)
                if (CS.GetNextRegister((byte)(0x50 + slot)) == bank) return (slot << 13) | (_physical & 0x1fff);
            return -1;
        }

        static string Label(int _address)
        {
            try
            {
                string l = CS.LookUpSymbol(_address & 0xffff);
                return string.IsNullOrEmpty(l) ? null : l;
            }
            catch (Exception) { return null; }
        }

        /// <summary>"label" or "label+3" for an address, using the nearest label at or before it (within 256 bytes)</summary>
        static string Near(int _address)
        {
            for (int back = 0; back < 256; back++)
            {
                int a = (_address - back) & 0xffff;
                string l = Label(a);
                if (l != null) return back == 0 ? l : l + "+" + back;
            }
            return null;
        }

        static string SourceAt(int _physical)
        {
            if (_physical < 0) return null;
            try
            {
                SourceLocation loc = CS.GetSourceLocation(_physical);
                if (loc == null) return null;
                List<string> files = CS.GetSourceFiles();
                string name = files != null && loc.File >= 0 && loc.File < files.Count ? files[loc.File] : "file " + loc.File;
                string text = "";
                string[] lines = CS.GetSourceText(loc.File);
                if (lines != null && loc.Line >= 1 && loc.Line <= lines.Length) text = "   " + lines[loc.Line - 1].Trim();
                return name + ":" + loc.Line + text;
            }
            catch (Exception) { return null; }
        }

        static string Status()
        {
            Z80Regs r = CS.GetRegs();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CPU: " + (Stopped() ? "stopped (in the debugger)" : "running"));
            string near = Near(r.PC);
            sb.AppendLine("PC " + H4(r.PC) + (near != null ? " (" + near + ")" : "") + "   SP " + H4(r.SP));
            sb.AppendLine("AF " + H4(r.AF) + "  BC " + H4(r.BC) + "  DE " + H4(r.DE) + "  HL " + H4(r.HL));
            sb.AppendLine("AF'" + H4(r._AF) + "  BC'" + H4(r._BC) + "  DE'" + H4(r._DE) + "  HL'" + H4(r._HL));
            sb.AppendLine("IX " + H4(r.IX) + "  IY " + H4(r.IY) + "  I " + H2(r.I) + "  R " + H2(r.R) + "  IM " + r.IM + "  IFF1 " + (r.IFF1 ? 1 : 0) + "  IFF2 " + (r.IFF2 ? 1 : 0));
            int f = r.AF & 0xff;
            sb.AppendLine("Flags: " + ((f & 0x80) != 0 ? "S " : "- ") + ((f & 0x40) != 0 ? "Z " : "- ") + ((f & 0x10) != 0 ? "H " : "- ") +
                          ((f & 0x04) != 0 ? "P/V " : "- ") + ((f & 0x02) != 0 ? "N " : "- ") + ((f & 0x01) != 0 ? "C" : "-") +
                          "   (" + (((f & 0x40) != 0) ? "Z" : "NZ") + ", " + (((f & 0x01) != 0) ? "C" : "NC") + ", " + (((f & 0x04) != 0) ? "PE" : "PO") + ", " + (((f & 0x80) != 0) ? "M" : "P") + ")");
            StringBuilder mmu = new StringBuilder("MMU:");
            for (int s = 0; s < 8; s++)
            {
                int b = CS.GetNextRegister((byte)(0x50 + s));
                mmu.Append("  " + s + ":" + (b == 0xff ? "ROM" : H2(b)));
            }
            sb.AppendLine(mmu.ToString());
            string src = SourceAt(ToPhysical(r.PC));
            if (src != null) sb.AppendLine("Source: " + src);
            return sb.ToString().TrimEnd();
        }

        static string DisasmText(int _start, int _count, bool _physical)
        {
            Z80Regs r = CS.GetRegs();
            int pcPhys = ToPhysical(r.PC);
            StringBuilder sb = new StringBuilder();
            int a = _start;
            for (int i = 0; i < _count; i++)
            {
                DissassemblyLine d = CS.DissasembleMemory(a, _physical);
                int len = Math.Max(1, Math.Min(6, d.bytes));
                byte[] bytes = _physical ? CS.PeekPhysical(a, len) : Peek(a, len);
                int logical = _physical ? ToLogical(a) : a;
                bool isPC = _physical ? a == pcPhys : a == r.PC;
                bool bp;
                if (_physical) bp = CS.Debugger(eDebugCommand.GetPhysicalBreakpoint, a) != 0 || (logical >= 0 && CS.Debugger(eDebugCommand.GetBreakpoint, logical) != 0);
                else
                {
                    int pa = ToPhysical(a);
                    bp = CS.Debugger(eDebugCommand.GetBreakpoint, a) != 0 || (pa >= 0 && CS.Debugger(eDebugCommand.GetPhysicalBreakpoint, pa) != 0);
                }

                string label = logical >= 0 ? Label(logical) : null;
                if (label != null) sb.AppendLine("                     " + label + ":");

                StringBuilder hex = new StringBuilder();
                foreach (byte b in bytes) hex.Append(b.ToString("X2")).Append(' ');
                string ts = d.TStates2 > 0 && d.TStates2 != d.TStates1 ? d.TStates1 + "/" + d.TStates2 : d.TStates1.ToString();
                sb.Append(isPC ? ">" : " ").Append(bp ? "*" : " ");
                sb.Append(_physical ? H6(a) : H4(a)).Append("  ");
                sb.Append(hex.ToString().PadRight(13));
                sb.Append((d.line ?? "").PadRight(24));
                if (d.TStates1 > 0) sb.Append(" ; ").Append(ts).Append('T');
                sb.AppendLine();
                a = _physical ? (a + len) % PHYSICAL_SIZE : (a + len) & 0xffff;
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Read from the 64K view, wrapping at $FFFF</summary>
        static byte[] Peek(int _address, int _count)
        {
            byte[] r = new byte[_count];
            for (int i = 0; i < _count; i++) r[i] = CS.Peek((ushort)((_address + i) & 0xffff));
            return r;
        }

        // ******************************************************************************************
        //  Handlers
        // ******************************************************************************************
        static ToolResult SetRegisters(ToolArgs _a)
        {
            Dictionary<string, object> regs = _a.Object("registers");
            if (regs == null || regs.Count == 0) return ToolResult.Error("Give registers as an object, e.g. {\"HL\":\"$4000\"}");
            return P.Emu(() =>
            {
                Z80Regs r = CS.GetRegs();
                List<string> bad = new List<string>();
                foreach (KeyValuePair<string, object> kv in regs)
                {
                    int v = (int)Expr.Eval(CS, kv.Value, kv.Key);
                    if (!Expr.SetRegister(r, kv.Key.Trim().ToUpperInvariant(), v)) bad.Add(kv.Key);
                }
                if (bad.Count > 0) return ToolResult.Error("Unknown register(s): " + string.Join(", ", bad) + " - nothing changed");
                CS.SetRegs(r);
                return ToolResult.Text(Status());
            });
        }

        static ToolResult ReadMemory(ToolArgs _a)
        {
            int length = Math.Max(1, Math.Min(16384, _a.Int("length", 128)));
            bool phys = _a.Bool("physical");
            return P.Emu(() =>
            {
                int start = (int)Expr.Eval(CS, _a.Raw("address"), "address");
                start = phys ? start : start & 0xffff;
                if (phys && (start < 0 || start >= PHYSICAL_SIZE)) return ToolResult.Error("Physical address out of range (0-$1FFFFF)");
                if (phys && start + length > PHYSICAL_SIZE) length = PHYSICAL_SIZE - start;
                byte[] m = phys ? CS.PeekPhysical(start, length) : Peek(start, length);
                StringBuilder sb = new StringBuilder();
                for (int o = 0; o < m.Length; o += 16)
                {
                    int a = start + o;
                    sb.Append(phys ? H6(a) : H4(a)).Append("  ");
                    StringBuilder ascii = new StringBuilder();
                    for (int i = 0; i < 16; i++)
                    {
                        if (o + i < m.Length)
                        {
                            byte b = m[o + i];
                            sb.Append(b.ToString("X2")).Append(i == 7 ? "  " : " ");
                            ascii.Append(b >= 32 && b < 127 ? (char)b : '.');
                        }
                        else sb.Append(i == 7 ? "    " : "   ");
                    }
                    sb.Append(' ').Append(ascii).AppendLine();
                }
                return ToolResult.Text(sb.ToString().TrimEnd());
            });
        }

        static byte[] ParseHexBytes(string _data)
        {
            List<byte> r = new List<byte>();
            foreach (string raw in _data.Split(new[] { ' ', ',', '\t', '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string s = raw;
                if (s.StartsWith("$") || s.StartsWith("#")) s = s.Substring(1);
                else if (s.StartsWith("0x") || s.StartsWith("0X")) s = s.Substring(2);
                if (s.EndsWith("h") || s.EndsWith("H")) s = s.Substring(0, s.Length - 1);
                if (s.Length % 2 == 1) s = "0" + s;
                for (int i = 0; i < s.Length; i += 2)
                {
                    if (!byte.TryParse(s.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                        throw new ArgumentException("Bad hex '" + raw + "'");
                    r.Add(b);
                }
            }
            return r.ToArray();
        }

        static ToolResult WriteMemory(ToolArgs _a)
        {
            List<byte> bytes = new List<byte>();
            if (_a.Has("data")) bytes.AddRange(ParseHexBytes(_a.String("data")));
            if (_a.Has("text")) bytes.AddRange(Encoding.ASCII.GetBytes(_a.String("text")));
            if (bytes.Count == 0) return ToolResult.Error("Nothing to write: give data (hex) or text");
            bool phys = _a.Bool("physical");
            return P.Emu(() =>
            {
                int start = (int)Expr.Eval(CS, _a.Raw("address"), "address");
                if (phys)
                {
                    if (start < 0 || start + bytes.Count > PHYSICAL_SIZE) return ToolResult.Error("Physical address out of range (0-$1FFFFF)");
                    CS.PokePhysical(start, bytes.ToArray());
                    return ToolResult.Text("Wrote " + bytes.Count + " bytes at physical " + H6(start));
                }
                for (int i = 0; i < bytes.Count; i++) CS.Poke((ushort)((start + i) & 0xffff), bytes[i]);
                return ToolResult.Text("Wrote " + bytes.Count + " bytes at " + H4(start));
            });
        }

        static ToolResult Disassemble(ToolArgs _a)
        {
            int count = Math.Max(1, Math.Min(500, _a.Int("count", 20)));
            bool phys = _a.Bool("physical");
            return P.Emu(() =>
            {
                int start = _a.Has("address") ? (int)Expr.Eval(CS, _a.Raw("address"), "address") : CS.GetRegs().PC;
                start = phys ? start % PHYSICAL_SIZE : start & 0xffff;
                return ToolResult.Text(DisasmText(start, count, phys));
            }, 15000);
        }

        static ToolResult Pause()
        {
            int since = 0;
            bool already = P.Emu(() =>
            {
                since = P.StopCounter;
                if (Stopped()) return true;
                CS.Debugger(eDebugCommand.Enter);
                return false;
            });
            if (!already) P.WaitForStop(since, 2000);
            return ToolResult.Text(P.Emu(() => Status() + "\n\n" + DisasmText(CS.GetRegs().PC, 6, false)));
        }

        static ToolResult Step(ToolArgs _a)
        {
            int count = Math.Max(1, Math.Min(100000, _a.Int("count", 1)));
            return P.Emu(() =>
            {
                if (!Stopped()) return ToolResult.Error("The CPU is running - pause first");
                int done = 0;
                for (; done < count; done++)
                {
                    CS.Debugger(eDebugCommand.Step);
                    if (!Stopped()) break;
                }
                return ToolResult.Text((count > 1 ? "Stepped " + done + " instructions\n" : "") + Status() + "\n\n" + DisasmText(CS.GetRegs().PC, 6, false));
            }, 30000);
        }

        static ToolResult StepOver(ToolArgs _a)
        {
            int timeout = Math.Max(1, Math.Min(MAX_WAIT_MS, _a.Int("timeout_ms", 5000)));
            int since = 0;
            string err = null;
            bool running = P.Emu(() =>
            {
                if (!Stopped()) { err = "The CPU is running - pause first"; return false; }
                since = P.StopCounter;
                CS.Debugger(eDebugCommand.StepOver);
                return !Stopped();          // a CALL etc: it runs until the temp breakpoint after it
            });
            if (err != null) return ToolResult.Error(err);
            if (running && !P.WaitForStop(since, timeout))
                return ToolResult.Text("Still running after " + timeout + "ms (the call hasn't returned) - use wait_for_stop or pause");
            return ToolResult.Text(P.Emu(() => Status() + "\n\n" + DisasmText(CS.GetRegs().PC, 6, false)));
        }

        static ToolResult RunTo(ToolArgs _a)
        {
            int timeout = Math.Max(1, Math.Min(MAX_WAIT_MS, _a.Int("timeout_ms", 10000)));
            bool phys = _a.Bool("physical");
            int since = 0;
            int target = P.Emu(() =>
            {
                int addr = (int)Expr.Eval(CS, _a.Raw("address"), "address");
                addr = phys ? addr % PHYSICAL_SIZE : addr & 0xffff;
                since = P.StopCounter;
                if (phys)
                {
                    if (CS.Debugger(eDebugCommand.GetPhysicalBreakpoint, addr) == 0)
                    {
                        CS.Debugger(eDebugCommand.SetPhysicalBreakpoint, addr);
                        P.AddTempBreakpoint(addr, true);
                    }
                }
                else if (CS.Debugger(eDebugCommand.GetBreakpoint, addr) == 0)
                {
                    CS.Debugger(eDebugCommand.SetBreakpoint, addr);
                    P.AddTempBreakpoint(addr, false);
                }
                if (Stopped()) CS.Debugger(eDebugCommand.Run);
                return addr;
            });
            if (!P.WaitForStop(since, timeout))
                return ToolResult.Text("Still running after " + timeout + "ms - " + (phys ? H6(target) : H4(target)) + " not reached yet. Use wait_for_stop or pause.");
            return ToolResult.Text(P.Emu(() =>
            {
                int pc = CS.GetRegs().PC;
                bool hit = phys ? ToPhysical(pc) == target : pc == target;
                return (hit ? "Reached " : "Stopped somewhere else (another breakpoint?) before ") + (phys ? H6(target) : H4(target)) + "\n" +
                       Status() + "\n\n" + DisasmText(pc, 6, false);
            }));
        }

        static ToolResult WaitForStop(ToolArgs _a)
        {
            int timeout = Math.Max(1, Math.Min(MAX_WAIT_MS, _a.Int("timeout_ms", 10000)));
            int since = 0;
            bool stopped = P.Emu(() => { since = P.StopCounter; return Stopped(); });
            if (!stopped && !P.WaitForStop(since, timeout)) return ToolResult.Text("Still running after " + timeout + "ms");
            return ToolResult.Text(P.Emu(() => Status() + "\n\n" + DisasmText(CS.GetRegs().PC, 6, false)));
        }

        static ToolResult Breakpoint(ToolArgs _a, bool _set)
        {
            string type = (_a.String("type", "execute") ?? "execute").Trim().ToLowerInvariant();
            bool phys = _a.Bool("physical");
            if (!_set && _a.Bool("all"))
            {
                P.Emu(() => { CS.Debugger(eDebugCommand.ClearAllBreakpoints); P.PhysicalBreakpointsSet.Clear(); });
                return ToolResult.Text("All breakpoints cleared");
            }
            if (!_a.Has("address")) return ToolResult.Error("No address given");
            if (phys && type != "execute") return ToolResult.Error("Read/write breakpoints are on the 64K view only");
            return P.Emu(() =>
            {
                int addr = (int)Expr.Eval(CS, _a.Raw("address"), "address");
                addr = phys ? addr : addr & 0xffff;
                string where = phys ? "physical " + H6(addr) : H4(addr);
                string near = phys ? null : Near(addr);
                if (near != null) where += " (" + near + ")";
                switch (type)
                {
                    case "execute":
                    case "exe":
                    case "x":
                        if (phys)
                        {
                            CS.Debugger(_set ? eDebugCommand.SetPhysicalBreakpoint : eDebugCommand.ClearPhysicalBreakpoint, addr);
                            if (_set) P.PhysicalBreakpointsSet.Add(addr); else P.PhysicalBreakpointsSet.Remove(addr);
                        }
                        else CS.Debugger(_set ? eDebugCommand.SetBreakpoint : eDebugCommand.ClearBreakpoint, addr);
                        break;
                    case "read":
                    case "r":
                        CS.Debugger(_set ? eDebugCommand.SetReadBreakpoint : eDebugCommand.ClearReadBreakpoint, addr);
                        break;
                    case "write":
                    case "w":
                        CS.Debugger(_set ? eDebugCommand.SetWriteBreakpoint : eDebugCommand.ClearWriteBreakpoint, addr);
                        break;
                    default:
                        return ToolResult.Error("type should be execute, read or write");
                }
                return ToolResult.Text((_set ? "Set " : "Cleared ") + type + " breakpoint at " + where);
            });
        }

        static string ListBreakpoints()
        {
            List<BreakpointInfo> list = null;
            try { list = GetBreakpoints(); }
            catch (MissingMethodException) { }              // an older CSpect: scan for them
            if (list == null) return ScanBreakpoints();

            List<string> exe = new List<string>(), rd = new List<string>(), wr = new List<string>(), ph = new List<string>(), pin = new List<string>(), pout = new List<string>();
            foreach (BreakpointInfo b in list)
            {
                switch (b.Type)
                {
                    case eBreakpointType.Execute: exe.Add(Describe(b.Address)); break;
                    case eBreakpointType.PhysicalExecute: ph.Add(DescribePhysical(b.Address)); break;
                    case eBreakpointType.Read: rd.Add(Describe(b.Address)); break;
                    case eBreakpointType.Write: wr.Add(Describe(b.Address)); break;
                    case eBreakpointType.InPort: pin.Add(H4(b.Address)); break;
                    case eBreakpointType.OutPort: pout.Add(H4(b.Address)); break;
                }
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Execute (64K): " + (exe.Count == 0 ? "none" : string.Join(", ", exe)));
            sb.AppendLine("Execute (physical): " + (ph.Count == 0 ? "none" : string.Join(", ", ph)));
            sb.AppendLine("Read: " + (rd.Count == 0 ? "none" : string.Join(", ", rd)));
            sb.AppendLine("Write: " + (wr.Count == 0 ? "none" : string.Join(", ", wr)));
            sb.AppendLine("Port IN: " + (pin.Count == 0 ? "none" : string.Join(", ", pin)));
            sb.Append("Port OUT: " + (pout.Count == 0 ? "none" : string.Join(", ", pout)));
            return sb.ToString();
        }

        /// <summary>(Its own method: calling a missing interface method throws when this is JIT compiled)</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static List<BreakpointInfo> GetBreakpoints() { return CS.ListBreakpoints(); }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Reset(bool _hard) { CS.ResetMachine(_hard); }

        static ToolResult ResetMachine(ToolArgs _a)
        {
            bool hard = _a.Bool("hard", true);
            return P.Emu(() =>
            {
                try { Reset(hard); }
                catch (MissingMethodException) { return ToolResult.Error("This CSpect can't reset from plugins - it needs a newer CSpect (iCSpect.ResetMachine)"); }
                return ToolResult.Text((hard ? "Hard" : "Soft") + " reset\n" + Status());
            });
        }

        static string DescribePhysical(int _a)
        {
            int l = ToLogical(_a);
            string near = l >= 0 ? Near(l) : null;
            return H6(_a) + " (bank8k " + H2(_a >> 13) + "+" + H4(_a & 0x1fff) + (l >= 0 ? ", now at " + H4(l) : "") + (near != null ? " " + near : "") + ")";
        }

        /// <summary>Older CSpects: look for breakpoints one address at a time (no port breakpoints)</summary>
        static string ScanBreakpoints()
        {
            StringBuilder sb = new StringBuilder();
            List<string> exe = new List<string>(), rd = new List<string>(), wr = new List<string>(), ph = new List<string>();
            for (int a = 0; a < 0x10000; a++)
            {
                if (CS.Debugger(eDebugCommand.GetBreakpoint, a) != 0) exe.Add(Describe(a));
                if (CS.Debugger(eDebugCommand.GetReadBreakpoint, a) != 0) rd.Add(Describe(a));
                if (CS.Debugger(eDebugCommand.GetWriteBreakpoint, a) != 0) wr.Add(Describe(a));
            }
            // physical: the whole 4MB table (also finds ones set by the Monitor or CSpect's debugger)
            const int PHYS_BP_SIZE = 4 * 1024 * 1024;
            for (int a = 0; a < PHYS_BP_SIZE; a++)
            {
                if (CS.Debugger(eDebugCommand.GetPhysicalBreakpoint, a) != 0) ph.Add(DescribePhysical(a));
            }
            sb.AppendLine("Execute (64K): " + (exe.Count == 0 ? "none" : string.Join(", ", exe)));
            sb.AppendLine("Execute (physical): " + (ph.Count == 0 ? "none" : string.Join(", ", ph)));
            sb.AppendLine("Read: " + (rd.Count == 0 ? "none" : string.Join(", ", rd)));
            sb.AppendLine("Write: " + (wr.Count == 0 ? "none" : string.Join(", ", wr)));
            sb.Append("(this CSpect can't list port breakpoints)");
            return sb.ToString();
        }

        static string Describe(int _address)
        {
            string near = Near(_address);
            return H4(_address) + (near != null ? " (" + near + ")" : "");
        }

        static ToolResult Evaluate(ToolArgs _a)
        {
            return P.Emu(() =>
            {
                long v = Expr.Eval(CS, _a.Raw("expression"), "expression");
                StringBuilder sb = new StringBuilder();
                sb.Append(v.ToString(CultureInfo.InvariantCulture));
                if (v >= 0) sb.Append("  $" + v.ToString("X"));
                if (v >= 0 && v <= 0xff) sb.Append("  %" + Convert.ToString(v, 2).PadLeft(8, '0'));
                if (v >= 0 && v <= 0xffff)
                {
                    string near = Near((int)v);
                    if (near != null) sb.Append("\nSymbol: " + near);
                    string src = SourceAt(ToPhysical((int)v));
                    if (src != null) sb.Append("\nSource: " + src);
                    sb.Append("\nByte there: " + H2(CS.Peek((ushort)v)) + "  word: " + H4(CS.Peek((ushort)v) | (CS.Peek((ushort)(v + 1)) << 8)));
                }
                return ToolResult.Text(sb.ToString());
            });
        }

        static ToolResult FindSymbols(ToolArgs _a)
        {
            string text = _a.String("text", "");
            int max = Math.Max(1, Math.Min(1000, _a.Int("max", 50)));
            return P.Emu(() =>
            {
                List<Symbol> list = CS.FindSymbols(text, max);
                if (list == null || list.Count == 0) return ToolResult.Text("No symbols match '" + text + "'" + " (is a map file loaded?)");
                StringBuilder sb = new StringBuilder();
                foreach (Symbol s in list)
                {
                    sb.Append(s.name).Append(" = ").Append(s.value > 0xffff ? H6(s.value) : H4(s.value));
                    if (s.type == eLabelType.Equate) sb.Append("  (equate)");
                    else if (s.PhysicalAddress >= 0 && s.type == eLabelType.Address) sb.Append("  physical " + H6(s.PhysicalAddress));
                    sb.AppendLine();
                }
                if (list.Count >= max) sb.AppendLine("(first " + max + ")");
                return ToolResult.Text(sb.ToString().TrimEnd());
            });
        }

        static ToolResult GetSource(ToolArgs _a)
        {
            int before = Math.Max(0, Math.Min(200, _a.Int("before", 6)));
            int after = Math.Max(0, Math.Min(400, _a.Int("after", 12)));
            return P.Emu(() =>
            {
                List<string> files = CS.GetSourceFiles();
                if (files == null || files.Count == 0) return ToolResult.Error("No source files - load a SNASM map file with a [LINES] section");
                int file = -1, line = -1;
                if (_a.Has("file"))
                {
                    string f = _a.String("file");
                    if (int.TryParse(f, out int idx)) file = idx;
                    else
                    {
                        for (int i = 0; i < files.Count && file < 0; i++)
                            if (string.Equals(files[i], f, StringComparison.OrdinalIgnoreCase)) file = i;
                        for (int i = 0; i < files.Count && file < 0; i++)
                            if (files[i].EndsWith(f, StringComparison.OrdinalIgnoreCase)) file = i;
                    }
                    if (file < 0 || file >= files.Count) return ToolResult.Error("No source file '" + f + "'. Files: " + string.Join(", ", files));
                    line = _a.Int("line", 1);
                }
                else
                {
                    int addr = _a.Has("address") ? (int)Expr.Eval(CS, _a.Raw("address"), "address") : CS.GetRegs().PC;
                    int phys = _a.Bool("physical") ? addr : ToPhysical(addr & 0xffff);
                    SourceLocation loc = phys >= 0 ? CS.GetSourceLocation(phys) : null;
                    if (loc == null) return ToolResult.Error("No source line for " + (_a.Bool("physical") ? H6(addr) : H4(addr)) + ". Files: " + string.Join(", ", files));
                    file = loc.File;
                    line = loc.Line;
                }
                string[] text = CS.GetSourceText(file);
                if (text == null) return ToolResult.Error("Can't read " + files[file]);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(files[file] + ":" + line);
                int from = Math.Max(1, line - before), to = Math.Min(text.Length, line + after);
                for (int l = from; l <= to; l++)
                    sb.Append(l == line ? ">" : " ").Append(l.ToString().PadLeft(5)).Append("  ").AppendLine(text[l - 1].Replace("\t", "    "));
                return ToolResult.Text(sb.ToString().TrimEnd());
            });
        }

        static ToolResult ReadNextRegisters(ToolArgs _a)
        {
            string which = _a.String("registers", "");
            return P.Emu(() =>
            {
                StringBuilder sb = new StringBuilder();
                if (which.Trim().Length == 0)
                {
                    sb.AppendLine("     +0 +1 +2 +3 +4 +5 +6 +7 +8 +9 +A +B +C +D +E +F");
                    for (int row = 0; row < 256; row += 16)
                    {
                        sb.Append(H2(row)).Append("  ");
                        for (int i = 0; i < 16; i++) sb.Append(CS.GetNextRegister((byte)(row + i)).ToString("X2")).Append(' ');
                        sb.AppendLine();
                    }
                    return ToolResult.Text(sb.ToString().TrimEnd());
                }
                foreach (string part in which.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int dash = part.IndexOf('-', 1);
                    int a = (int)Expr.Eval(CS, dash > 0 ? part.Substring(0, dash) : part, "register") & 0xff;
                    int b = dash > 0 ? (int)Expr.Eval(CS, part.Substring(dash + 1), "register") & 0xff : a;
                    for (int r = a; r <= b; r++)
                    {
                        int v = CS.GetNextRegister((byte)r);
                        sb.AppendLine("NextReg " + H2(r) + " = " + H2(v) + "  %" + Convert.ToString(v, 2).PadLeft(8, '0'));
                    }
                }
                return ToolResult.Text(sb.ToString().TrimEnd());
            });
        }

        static ToolResult Screenshot(ToolArgs _a)
        {
            int scale = _a.Int("scale", 2) == 1 ? 1 : 2;
            uint[,] frame = P.Emu(() => CS.GetGlobal(eGlobal.last_frame) as uint[,]);
            if (frame == null) return ToolResult.Error("No frame yet");
            int fh = frame.GetLength(0), fw = frame.GetLength(1);         // 256 x 640 (320 doubled across)
            int w = scale == 2 ? fw : fw / 2, h = scale == 2 ? fh * 2 : fh;
            uint[] px = new uint[w * h];
            for (int y = 0; y < h; y++)
            {
                int sy = scale == 2 ? y >> 1 : y;
                for (int x = 0; x < w; x++) px[y * w + x] = frame[sy, scale == 2 ? x : x * 2];
            }
            byte[] png = Png.Encode(px, w, h);
            ToolResult r = new ToolResult();
            r.Content.Add(new JObj { { "type", "text" }, { "text", "Display " + w + "x" + h + (P.IsStoppedNow ? " (CPU stopped)" : "") } });
            return r.AddImage(png);
        }

        // ******************************************************************************************
        //  Keyboard
        // ******************************************************************************************
        static readonly string[][] Matrix =
        {
            new[] { "CAPS", "Z", "X", "C", "V" },
            new[] { "A", "S", "D", "F", "G" },
            new[] { "Q", "W", "E", "R", "T" },
            new[] { "1", "2", "3", "4", "5" },
            new[] { "0", "9", "8", "7", "6" },
            new[] { "P", "O", "I", "U", "Y" },
            new[] { "ENTER", "L", "K", "J", "H" },
            new[] { "SPACE", "SYM", "M", "N", "B" },
        };

        static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "CAPSSHIFT", "CAPS" }, { "CS", "CAPS" }, { "SHIFT", "CAPS" },
            { "SYMBOL", "SYM" }, { "SYMSHIFT", "SYM" }, { "SS", "SYM" },
            { "RETURN", "ENTER" }, { "CR", "ENTER" },
            { "BREAK", "CAPS+SPACE" }, { "DELETE", "CAPS+0" }, { "DEL", "CAPS+0" }, { "BACKSPACE", "CAPS+0" },
            { "LEFT", "CAPS+5" }, { "DOWN", "CAPS+6" }, { "UP", "CAPS+7" }, { "RIGHT", "CAPS+8" },
            { "EDIT", "CAPS+1" }, { "CAPSLOCK", "CAPS+2" }, { "TRUEVIDEO", "CAPS+3" }, { "INVVIDEO", "CAPS+4" },
            { "GRAPHICS", "CAPS+9" }, { "EXTEND", "CAPS+SYM" },
        };

        /// <summary>Characters typed with SYM</summary>
        static readonly Dictionary<char, string> SymChars = new Dictionary<char, string>
        {
            { '!', "1" }, { '@', "2" }, { '#', "3" }, { '$', "4" }, { '%', "5" }, { '&', "6" }, { '\'', "7" }, { '(', "8" }, { ')', "9" }, { '_', "0" },
            { '<', "R" }, { '>', "T" }, { ';', "O" }, { '"', "P" }, { '=', "L" }, { '+', "K" }, { '-', "J" }, { '^', "H" },
            { ':', "Z" }, { '£', "X" }, { '?', "C" }, { '/', "V" }, { '*', "B" }, { ',', "N" }, { '.', "M" },
        };

        /// <summary>Press one key into rows (false if unknown)</summary>
        static bool AddKey(int[] _rows, string _key)
        {
            for (int r = 0; r < 8; r++)
                for (int b = 0; b < 5; b++)
                    if (string.Equals(Matrix[r][b], _key, StringComparison.OrdinalIgnoreCase)) { _rows[r] &= ~(1 << b); return true; }
            return false;
        }

        static int[] NoKeys()
        {
            int[] r = new int[8];
            for (int i = 0; i < 8; i++) r[i] = 0xff;
            return r;
        }

        /// <summary>"CAPS+5" -> rows; throws on an unknown key</summary>
        static int[] Combo(string _combo)
        {
            int[] rows = NoKeys();
            foreach (string raw in _combo.Split('+'))
            {
                string k = raw.Trim();
                if (k.Length == 0) continue;
                if (Aliases.TryGetValue(k, out string alias))
                {
                    foreach (string sub in alias.Split('+')) AddKey(rows, sub);
                    continue;
                }
                if (!AddKey(rows, k)) throw new ArgumentException("Unknown key '" + k + "'");
            }
            return rows;
        }

        static void AddStep(List<MCPPlugin.KeyStep> _steps, int[] _rows, int _hold, int _gap)
        {
            _steps.Add(new MCPPlugin.KeyStep { Rows = _rows, Frames = _hold });
            if (_gap > 0) _steps.Add(new MCPPlugin.KeyStep { Rows = NoKeys(), Frames = _gap });
        }

        static ToolResult PlayKeys(List<MCPPlugin.KeyStep> _steps, string _what)
        {
            if (!P.KeysSupported) return ToolResult.Error("This CSpect can't take key presses from plugins - it needs a newer CSpect (iCSpect.SetKeyboardRow)");
            int frames = 0;
            foreach (MCPPlugin.KeyStep s in _steps) frames += Math.Max(1, s.Frames);
            int since = P.QueueKeys(_steps);
            if (P.IsStoppedNow) return ToolResult.Text(_what + " queued - the CPU is stopped, so they'll be pressed when it runs");
            int timeout = Math.Min(MAX_WAIT_MS, frames * 40 + 5000);
            if (!P.WaitForKeys(since, timeout)) return ToolResult.Text(_what + " still being pressed (the CPU stopped, or it's running slowly)");
            return ToolResult.Text(_what + " done (" + frames + " frames)");
        }

        static ToolResult PressKeys(ToolArgs _a)
        {
            int hold = Math.Max(1, Math.Min(500, _a.Int("hold_frames", 3)));
            int gap = Math.Max(0, Math.Min(500, _a.Int("gap_frames", 3)));
            string keys = _a.String("keys", "");
            List<MCPPlugin.KeyStep> steps = new List<MCPPlugin.KeyStep>();
            foreach (string item in keys.Split(new[] { ' ', ',', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                AddStep(steps, Combo(item), hold, gap);
            if (steps.Count == 0) return ToolResult.Error("No keys given");
            return PlayKeys(steps, "Keys");
        }

        static ToolResult TypeText(ToolArgs _a)
        {
            int hold = Math.Max(1, Math.Min(500, _a.Int("hold_frames", 3)));
            int gap = Math.Max(0, Math.Min(500, _a.Int("gap_frames", 3)));
            string text = _a.String("text", "");
            if (text.Length == 0) return ToolResult.Error("No text given");
            List<MCPPlugin.KeyStep> steps = new List<MCPPlugin.KeyStep>();
            foreach (char c in text.Replace("\r\n", "\n"))
            {
                int[] rows = NoKeys();
                if (c >= 'a' && c <= 'z') AddKey(rows, c.ToString());
                else if (c >= 'A' && c <= 'Z') { AddKey(rows, "CAPS"); AddKey(rows, c.ToString()); }
                else if (c >= '0' && c <= '9') AddKey(rows, c.ToString());
                else if (c == ' ') AddKey(rows, "SPACE");
                else if (c == '\n' || c == '\r') AddKey(rows, "ENTER");
                else if (SymChars.TryGetValue(c, out string k)) { AddKey(rows, "SYM"); AddKey(rows, k); }
                else return ToolResult.Error("Can't type '" + c + "' on the Spectrum keyboard (try press_keys with EXTEND for [ ] { } ~ | \\ ©)");
                AddStep(steps, rows, hold, gap);
            }
            return PlayKeys(steps, "Text");
        }
    }
}

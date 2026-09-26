// *****************************************************************************
// CSpect Printer plugin.
//
// Echoes the Spectrum's printer output onto the console, or into a text file.
//
// Written by: Jörg Pleumann (with assistance from Claude Code).
//
// Released under the GNU 3 license - please see license file for more details.
//
// This plugin uses the EXE extension method and traps trying to execute the
// ROM's character output routine at $15F2, which is where the RST $10 vector
// jumps to. Whenever that routine is entered and the printer channel ("P",
// channel 3) is the current one, the character in the accumulator is written
// out. That's the channel LPRINT and LLIST use, and the system never prints to
// it by itself - so unlike the screen channels, what arrives here is only ever
// what the program deliberately sent.
//
// Command line:
//   -printer            prints to the console.
//   -printer=<file>     prints to <file>, creating it or appending to it as
//                       necessary. A relative path is taken from the directory
//                       CSpect was started in - see ResolvePath().
//
// Output is UTF-8. The Spectrum's character set is ASCII apart from "pound"
// and "copyright", plus the block graphics, all of which Unicode covers - see
// Translate() below. The user defined graphics are the one thing the two
// destinations show differently, see PutUDG().
//
// Cursor left (8) and right (9) move the print position within the line, and a
// later character overwrites whatever sits there. We stay within the line, the
// way a real printer does, hence no support for cursor up and down.
//
// The two destinations reach that result by different means, on purpose. The
// console gets every character as it is printed, with VT100 moves for the
// cursor, so the line can be watched taking shape. A file gets nothing until
// the line ends, and then the finished line in one piece, with the
// overprinting already resolved - no escape sequence ever reaches it. Either
// way the characters themselves are translated the same, see Translate().
//
// *****************************************************************************
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Plugin;

namespace Printer
{
    // *************************************************************************
    /// <summary>
    ///     Echoes the Spectrum's printer output to the console or into a file.
    /// </summary>
    // *************************************************************************
    public class PrinterPlugin : iPlugin
    {
        #region Plugin interface

        /// <summary>
        ///     Address of the ROM's "print a character" routine.
        /// </summary>
        public const int PC_Address = 0x10;

        /// <summary>
        ///     Z80 opcode for an absolute jump (required at $0010).
        /// </summary>
        public const int Opcode_JP = 0xc3;

        /// <summary>
        ///     The ROM's character output routine (PRINT-A-2), which is where
        ///     the RST $10 vector jumps to. This is what we trap, rather than
        ///     the vector itself, because the ROM's number printer (OUT-CODE,
        ///     $15EF) converts a digit and falls straight into this routine
        ///     without ever going near $0010. If we just trap the vector many
        ///     (integer) numbers will stay invisible. The address originates
        ///     from the 48K ROM and seems to have been kept in place for the
        ///     Next.
        /// </summary>
        public const int OutputRoutine = 0x15f2;

        /// <summary>The command line option enabling us.</summary>
        public const string CommandLineOption = "-printer";

        /// <summary>
        ///     CHANS system variable - start of the channel information area.
        /// </summary>
        public const int SysVar_CHANS = 0x5c4f;

        /// <summary>
        ///     CURCHL system variable - the channel information currently used
        ///     for input/output.
        /// </summary>
        public const int SysVar_CURCHL = 0x5c51;

        /// <summary>Size of a channel information block.</summary>
        public const int ChannelEntrySize = 5;

        /// <summary>
        ///     The channel we're listening on - "P", the printer.
        /// </summary>
        public const int PrinterChannel = 3;

        /// <summary>
        ///     The "PRINT comma" control code - what a comma in LPRINT sends to
        ///     move to the next field.
        /// </summary>
        public const int Char_PrintComma = 6;

        /// <summary>Cursor control code - one position to the left.</summary>
        public const int Char_CursorLeft = 8;

        /// <summary>Cursor control code - one position to the right.</summary>
        public const int Char_CursorRight = 9;

        /// <summary>
        ///     Width of a print field - what a comma moves on to. The Spectrum
        ///     fits two of these into its 32 column printer line.
        /// </summary>
        public const int FieldWidth = 16;

        /// <summary>
        ///     The character that starts a VT100 escape sequence.
        /// </summary>
        public const char Escape = (char)27;

        /// <summary>First control code taking parameter bytes (INK).</summary>
        public const int Char_FirstWithParameters = 16;

        /// <summary>The AT control code - takes a line and a column.</summary>
        public const int Char_At = 22;

        /// <summary>
        ///     The TAB control code - takes a column as two bytes.
        /// </summary>
        public const int Char_Tab = 23;

        /// <summary>
        ///     Where ASCII has a back quote, the Spectrum has a pound sign.
        /// </summary>
        public const int Char_Pound = 96;

        /// <summary>
        ///     Where ASCII has "delete", the Spectrum has a copyright sign.
        /// </summary>
        public const int Char_Copyright = 127;

        /// <summary>
        ///     First of the Spectrum's block graphics characters.
        /// </summary>
        public const int Char_FirstBlock = 128;

        /// <summary>Last of the Spectrum's block graphics characters.</summary>
        public const int Char_LastBlock = 143;

        /// <summary>First of the user defined graphics (UDG "A").</summary>
        public const int Char_FirstUDG = 144;

        /// <summary>Last of the user defined graphics (UDG "U").</summary>
        public const int Char_LastUDG = 164;

        /// <summary>
        ///     What we print for a user defined graphic in a file. Unicode's
        ///     circled capitals start here, so the UDGs "A" to "U" come out as
        ///     circled letters - that way the output still says which UDG was
        ///     printed, even though the actual bits are lost. See PutUDG().
        /// </summary>
        const char UDG_A_Glyph = '\u24b6';

        /// <summary>
        ///     The Spectrum's block graphics (codes 128-143) as Unicode
        ///     quadrant blocks.
        /// </summary>
        const string BlockGraphics =
            " \u259d\u2598\u2580\u2597\u2590\u259a\u259c\u2596\u259e\u258c\u259b\u2584\u259f\u2599\u2588";

        /// <summary>
        ///     File extensions CSpect loads itself - we refuse to print over
        ///     any of those.
        /// </summary>
        static readonly string[] ProtectedExtensions =
            {
              ".nex", ".sna", ".z80", ".tap", ".tzx", ".rom", ".dsk", ".trd",
              ".scl", ".mmc", ".img", ".vhd", ".csw", ".rzx"
            };

        /// <summary>
        ///     The file we print to, or null when we print to the console. This
        ///     is what tells the two apart throughout.
        /// </summary>
        TextWriter LogFile;

        /// <summary>
        ///     How many parameter bytes of a control code are still to come?
        ///     Those aren't characters and must not be printed.
        /// </summary>
        int ParametersToSwallow = 0;

        /// <summary>
        ///     The line being printed to a file. It only goes out once it is
        ///     finished, so that a program moving the print position about
        ///     within the line still ends up with the right result. Printing to
        ///     the console needs none of this and leaves it empty.
        /// </summary>
        StringBuilder Line = new StringBuilder();

        /// <summary>
        ///     Where in the line the next character goes. Needed either way -
        ///     to place the character when printing to a file, and to work out
        ///     the cursor moves when printing to the console. It can sit behind
        ///     the end of the line after a cursor move to the right.
        /// </summary>
        int Column = 0;

        public iCSpect CSpect;

        // *********************************************************************
        /// <summary>
        ///     Initializes the interface.
        /// </summary>
        /// <returns>
        ///     List of addresses we're monitoring, or null when we're not
        ///     enabled.
        /// </returns>
        // *********************************************************************
        public List<sIO> Init(iCSpect _CSpect)
        {
            CSpect = _CSpect;

            // No "-printer" on the command line? Then don't activate.
            if (!ParseCommandLine()) return null;

            // Create a list of the addresses we're interested in.
            List<sIO> ports = new List<sIO>();
            ports.Add(new sIO(OutputRoutine, eAccess.Memory_EXE));

            return ports;
        }

        // *********************************************************************
        /// <summary>
        ///     Quit the device - free up anything we need to.
        /// </summary>
        // *********************************************************************
        public void Quit()
        {
            // A line the program never finished would otherwise be lost. When
            // printing to the console there is no line to look at, so the
            // print position is what tells us one was started.
            if (Line.Length > 0 || Column > 0) FlushLine();

            if (LogFile == null) return;

            LogFile.Flush();
            LogFile.Dispose();
            LogFile = null;
        }

        // *********************************************************************
        /// <summary>
        ///     Called when machine is reset.
        /// </summary>
        // *********************************************************************
        public void Reset()
        {
            ParametersToSwallow = 0;
            Line.Length = 0;
            Column = 0;
        }

        // *********************************************************************
        /// <summary>
        ///     Called once an emulation FRAME.
        /// </summary>
        // *********************************************************************
        public void Tick()
        {
        }

        // *********************************************************************
        /// <summary>
        ///     Called once an OS emulator frame - do all UI rendering, opening
        ///     windows etc here.
        /// </summary>
        // *********************************************************************
        public void OSTick()
        {
        }

        // *********************************************************************
        /// <summary>
        ///     Key press callback.
        /// </summary>
        /// <param name="_id">The registered key ID.</param>
        /// <returns>
        ///     True indicates the plugin handled the key. False indicates
        ///     someone else can handle it.
        /// </returns>
        // *********************************************************************
        public bool KeyPressed(int _id)
        {
            return false;
        }

        // *********************************************************************
        /// <summary>
        ///     CPU is about to execute an instruction at one of the registered
        ///     addresses.
        /// </summary>
        /// <param name="_port">Port/Address.</param>
        /// <param name="_isvalid">
        ///     TRUE if the plugin handles the complete opcode, FALSE if the
        ///     emulator should execute it.
        /// </param>
        /// <returns>The number of T-states.</returns>
        // *********************************************************************
        public byte Read(eAccess _type, int _port, int _id, out bool _isvalid)
        {
            // Always let the emulator execute the instruction normally.
            _isvalid = false;

            if (_type != eAccess.Memory_EXE || _port != OutputRoutine) return 0;

            // Check if ROM is paged in and looks as expected.
            if (CSpect.GetNextRegister(0x50) != 255 || !IsStandardRom()) return 0;

            DoPrint();

            return 0;
        }

        // *********************************************************************
        /// <summary>
        ///     Write a value to one of the registered ports.
        /// </summary>
        /// <param name="_port">The port being written to.</param>
        /// <param name="_value">The value to write.</param>
        // *********************************************************************
        public bool Write(eAccess _type, int _port, int _id, byte _value)
        {
            return false;
        }

        #endregion


        // *********************************************************************
        /// <summary>
        ///     Look for a "-printer" on CSpect's command line, with an optional
        ///     file to print to. Plugins don't get the command line handed to
        ///     them, but we're running inside CSpect's own process, so we can
        ///     go and read it ourselves.
        /// </summary>
        /// <returns>
        ///     TRUE when we should be active, FALSE for "not enabled".
        /// </returns>
        // *********************************************************************
        bool ParseCommandLine()
        {
            string path = GetOptionValue(CommandLineOption, true);
            if (path == null) return false;

            OpenLogFile(path);
            return true;
        }

        // *********************************************************************
        /// <summary>
        ///     Fetch one of our options off CSpect's command line. Plugins
        ///     don't get the command line handed to them, but we're running
        ///     inside CSpect's own process, so we can go and read it ourselves.
        /// </summary>
        /// <param name="_option">
        ///     The option to look for, leading "-" included.
        /// </param>
        /// <param name="_allowSeparateValue">
        ///     May the value be an argument of its own?
        /// </param>
        /// <returns>
        ///     The value, "" for the option on its own, or null when it isn't
        ///     there at all.
        /// </returns>
        // *********************************************************************
        string GetOptionValue(string _option, bool _allowSeparateValue)
        {
            string[] args = Environment.GetCommandLineArgs();

            // Index 0 is the executable itself.
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith(_option, StringComparison.OrdinalIgnoreCase)) continue;

                string value = arg.Substring(_option.Length);

                // "-option=<value>" - the usual CSpect style.
                if (value.StartsWith("=") || value.StartsWith(":")) return value.Substring(1).Trim();

                if (value.Length == 0)
                {
                    // The option on its own, possibly followed by its value as
                    // an argument of its own. Anything starting with a "-" is
                    // somebody else's option, not our value.
                    if (_allowSeparateValue && i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        return args[i + 1].Trim();
                    }

                    return "";
                }

                // A longer option that merely starts out the same way - keep
                // looking.
            }

            return null;
        }

        // *********************************************************************
        /// <summary>
        ///     Open the file we log to on top of the console, if one was asked
        ///     for at all. The console always gets the output, so whatever goes
        ///     wrong in here costs us the file, never the plugin.
        /// </summary>
        /// <param name="_path">
        ///     The file to log to, or "" for the console alone.
        /// </param>
        // *********************************************************************
        void OpenLogFile(string _path)
        {
            string path = _path.Length > 0 ? ResolvePath(_path) : "";

            if (path.Length > 0)
            {
                // CSpect takes the file to load as a bare command line
                // argument, so "-printer game.nex" is all too easy to type -
                // and printer output appended to a .nex ruins it just as
                // thoroughly as overwriting would. Don't.
                string extension = Path.GetExtension(path).ToLowerInvariant();

                if (Array.IndexOf(ProtectedExtensions, extension) >= 0)
                {
                    Console.WriteLine(" Printer - \"" + _path + "\" looks like a file CSpect loads, not printing over it");
                }
                else
                {
                    try
                    {
                        // Create new file or append to existing file.
                        StreamWriter writer = new StreamWriter(path, true);

                        // CSpect may well never get around to calling Quit(),
                        // so don't sit on buffered output.
                        writer.AutoFlush = true;

                        LogFile = writer;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(" Printer - can't write to \"" + _path + "\": " + e.Message);
                    }
                }
            }

            if (LogFile == null)
            {
                Console.WriteLine(" Printer added");
            }
            else
            {
                Console.WriteLine(" Printer added (" + Path.GetFullPath(path) + ")");
            }
        }

        // *********************************************************************
        /// <summary>
        ///     Make sense of a relative path. CSpect moves its own working
        ///     directory to where it keeps its files, so by the time a plugin
        ///     runs, a relative path would no longer mean what the user typed
        ///     it to mean. Shells export the directory they were in as PWD, and
        ///     a program changing its working directory does not touch that, so
        ///     it still names where the command was given.
        /// </summary>
        /// <param name="_path">
        ///     The path as it came off the command line.
        /// </param>
        /// <returns>
        ///     An absolute path, or the original one when there is no PWD to go
        ///     by - as happens on Windows, or when CSpect wasn't started from a
        ///     shell at all.
        /// </returns>
        // *********************************************************************
        string ResolvePath(string _path)
        {
            if (Path.IsPathRooted(_path)) return _path;

            string shellDirectory = Environment.GetEnvironmentVariable("PWD");
            if (string.IsNullOrEmpty(shellDirectory)) return _path;
            if (!Path.IsPathRooted(shellDirectory)) return _path;

            return Path.Combine(shellDirectory, _path);
        }

        // *********************************************************************
        /// <summary>
        ///     Does the ROM currently paged in look like the standard ROM? We
        ///     check the three bytes at $0010 in order to find out.
        /// </summary>
        // *********************************************************************
        bool IsStandardRom()
        {
            return CSpect.Peek((ushort)PC_Address) == Opcode_JP && Peek16(PC_Address + 1) == OutputRoutine;
        }

        // *********************************************************************
        /// <summary>
        ///     Read a 16 bit value from the Z80's address space.
        /// </summary>
        /// <param name="_address">Address to read from.</param>
        /// <returns>The 16 bit value.</returns>
        // *********************************************************************
        int Peek16(int _address)
        {
            return CSpect.Peek((ushort)_address) | (CSpect.Peek((ushort)(_address + 1)) << 8);
        }

        // *********************************************************************
        /// <summary>
        ///     Is the printer channel ("P") the one currently being printed to?
        ///     We work that out from how far CURCHL points into the channel
        ///     information area.
        /// </summary>
        // *********************************************************************
        bool IsPrinterChannel()
        {
            int chans = Peek16(SysVar_CHANS);
            int curchl = Peek16(SysVar_CURCHL);

            // System variables not set up yet (or no channels at all)?
            if (chans == 0 || curchl < chans) return false;

            int offset = curchl - chans;
            if ((offset % ChannelEntrySize) != 0) return false;

            return (offset / ChannelEntrySize) == PrinterChannel;
        }

        // *********************************************************************
        /// <summary>
        ///     The ROM is about to print the character in the accumulator.
        ///     Write it out if it went to the printer channel.
        /// </summary>
        // *********************************************************************
        void DoPrint()
        {
            if (!IsPrinterChannel()) return;

            int c = CSpect.GetRegs().A;

            // The parameters of a control code arrive as characters in their
            // own right, but they're numbers, not text - and they can be any
            // value at all, 13 and 10 included. So they have to be eaten before
            // anything else looks at them.
            if (ParametersToSwallow > 0)
            {
                ParametersToSwallow--;
                return;
            }

            // The Spectrum ends a line with a single 13. There is no CR/LF pair
            // to fold here - 10 is a cursor code of its own, not a line feed.
            if (c == 13)
            {
                FlushLine();
                return;
            }

            // The print position moves within the line, the way a real printer
            // does. Nothing is written yet - a character put down later simply
            // overwrites whatever sits at that position.
            if (c == Char_CursorLeft)
            {
                if (Column > 0)
                {
                    Column--;
                    MoveConsole(1, 'D');
                }
                return;
            }

            if (c == Char_CursorRight)
            {
                Column++;
                MoveConsole(1, 'C');
                return;
            }

            // A comma moves on to the next field. TAB is meant to name a column
            // outright, but its two parameter bytes are swallowed below, so the
            // best we can do is treat it the same way.
            if (c == Char_PrintComma || c == Char_Tab)
            {
                ParametersToSwallow = ParameterCount(c);

                int field = ((Column / FieldWidth) + 1) * FieldWidth;
                MoveConsole(field - Column, 'C');
                Column = field;
                return;
            }

            ParametersToSwallow = ParameterCount(c);

            // A user defined graphic has no character of its own, and the two
            // destinations make the best of that in different ways.
            if (c >= Char_FirstUDG && c <= Char_LastUDG)
            {
                PutUDG(c - Char_FirstUDG);
                return;
            }

            char translated = Translate(c);
            if (translated == 0) return;

            Put(translated);
        }

        // *********************************************************************
        /// <summary>
        ///     Print a user defined graphic. We can't show the shape a program
        ///     gave it, so both destinations settle for naming which of the 21
        ///     it was - a file with a circled capital, which needs a font that
        ///     has those, and the console with a plain capital shown inverse.
        ///     Inverse costs exactly one cell, where a circled capital is wider
        ///     than one in many terminal fonts and ends up overlapping.
        /// </summary>
        /// <param name="_index">
        ///     Which UDG, 0 for "A" through 20 for "U".
        /// </param>
        // *********************************************************************
        void PutUDG(int _index)
        {
            if (LogFile != null)
            {
                Put((char)(UDG_A_Glyph + _index));
                return;
            }

            // Switched off again right away, so nothing stays inverse should a
            // program stop in the middle of a line.
            Console.Write(Escape.ToString() + "[7m");
            Put((char)('A' + _index));
            Console.Write(Escape.ToString() + "[27m");
        }

        // *********************************************************************
        /// <summary>
        ///     Put a character down at the current print position. Printing to
        ///     a file collects it in the line, growing that as needed and
        ///     padding with spaces where cursor right has left a gap. Printing
        ///     to the console hands it straight over instead, the cursor having
        ///     been moved there already.
        /// </summary>
        /// <param name="_c">The character to put down.</param>
        // *********************************************************************
        void Put(char _c)
        {
            if (LogFile != null)
            {
                while (Line.Length < Column) Line.Append(' ');

                if (Column < Line.Length) Line[Column] = _c;
                else Line.Append(_c);
            }
            else
            {
                Console.Write(_c);
            }

            // Both ways of printing need this - the file to know where in the
            // line the next character goes, the console to work out its moves.
            Column++;
        }

        // *********************************************************************
        /// <summary>
        ///     Move the console's cursor along with our print position, so the
        ///     line can be watched taking shape, overprinting and all. This is
        ///     for the console alone - escape sequences have no business in a
        ///     text file, which is why printing to a file collects the finished
        ///     line instead.
        /// </summary>
        /// <param name="_by">
        ///     How far to move, ignored when not positive.
        /// </param>
        /// <param name="_direction">"C" for right, "D" for left.</param>
        // *********************************************************************
        void MoveConsole(int _by, char _direction)
        {
            if (LogFile != null || _by <= 0) return;

            Console.Write(Escape.ToString() + "[" + _by + _direction);
        }

        // *********************************************************************
        /// <summary>
        ///     The line is finished. A file gets it in one piece, with the
        ///     overprinting already resolved into the characters that ended up
        ///     on the paper. The console has been watching it take shape all
        ///     along and only needs the line break.
        /// </summary>
        // *********************************************************************
        void FlushLine()
        {
            if (LogFile != null) LogFile.WriteLine(Line.ToString());
            else Console.WriteLine();

            Line.Length = 0;
            Column = 0;
        }

        // *********************************************************************
        /// <summary>
        ///     How many bytes of parameters does a control code take? Those
        ///     follow it through this very routine, one "character" at a time.
        /// </summary>
        /// <param name="_c">The Spectrum character code.</param>
        /// <returns>The number of parameter bytes still to come.</returns>
        // *********************************************************************
        int ParameterCount(int _c)
        {
            // INK, PAPER, FLASH, BRIGHT, INVERSE and OVER take a single value.
            if (_c >= Char_FirstWithParameters && _c < Char_At) return 1;

            // AT takes a line and a column, TAB a column as a 16 bit value.
            if (_c == Char_At || _c == Char_Tab) return 2;

            return 0;
        }

        // *********************************************************************
        /// <summary>
        ///     Turn a Spectrum character into the character we write out. The
        ///     Spectrum's set is ASCII apart from two characters, plus a block
        ///     of graphics that Unicode happens to cover exactly.
        /// </summary>
        /// <param name="_c">The Spectrum character code.</param>
        /// <returns>
        ///     The character to write, or 0 for "nothing sensible to write".
        /// </returns>
        // *********************************************************************
        char Translate(int _c)
        {
            // The pound sign.
            if (_c == Char_Pound) return '\u00a3';

            // The copyright sign.
            if (_c == Char_Copyright) return '\u00a9';

            // All valid ASCII chars.
            if (_c >= 32 && _c <= 126) return (char)_c;

            // The block graphics chars.
            if (_c >= Char_FirstBlock && _c <= Char_LastBlock) return BlockGraphics[_c - Char_FirstBlock];

            // Everything else we drop. Control codes below 32 would only
            // garble the output, the user defined graphics (144-164) are dealt
            // with in PutUDG() because they differ per destination, and the
            // keyword tokens (165-255) never need handling here - the ROM
            // expands those into single characters and sends them through this
            // very routine again, so we see the spelled out keyword anyway.
            return (char)0;
        }
    }
}

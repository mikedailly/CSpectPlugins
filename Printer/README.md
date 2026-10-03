# CSpect Printer Plugin

This plugin echoes the Spectrum's printer output to the console of the machine
running [CSpect](http://www.cspect.org), or into a text file.

Anything a program sends with `LPRINT` or `LLIST` shows up on your host, so you
can read it, scroll back through it, or keep it. That is useful for program
output that would otherwise scroll off the Spectrum's screen, for logging from
your own code, and for getting a listing out of the emulator as plain text.

The printer channel is a good place to listen. The system never prints to it by
itself, so unlike the screen channels nothing arrives there but what the program
deliberately sent - no editor echo, no keywords appearing as you type them.


# Command Line

~~~
-printer            prints to the console
-printer=<file>     prints to <file>
~~~

Without `-printer` the plugin stays completely out of the way and registers
nothing.

It is one or the other, not both. With a file, the console stays quiet.

The file is created if it does not exist and appended to if it does, so output
from several runs accumulates instead of being thrown away.

A relative path is taken from the directory you started CSpect in, which is not
necessarily the directory CSpect itself ends up working in. On macOS and Linux
this works out of the box; on Windows, where the shell does not export a `PWD`
variable, a relative path is resolved the way CSpect resolves its own, so use an
absolute path there if the result surprises you. The startup message always
names the file in full:

~~~
 Printer added (/Users/joerg/work/listing.txt)
~~~

As a safety net the plugin refuses to print into files whose extension CSpect
loads itself (`.nex`, `.sna`, `.tap`, `.z80`, `.rom`, `.dsk` and friends). It
says so and prints to the console instead.


# What Arrives

Everything the program prints to channel "P" - `LPRINT` and `LLIST`.

Numbers come through as well. That is worth mentioning because it is not free:
the ROM prints digits through a routine that bypasses the `RST $10` vector
entirely, so the plugin traps the ROM's shared character output routine instead
of the vector. `LLIST` therefore gives you line numbers, not just keywords.

Keywords arrive spelled out. The ROM expands its own tokens into single
characters before printing them, so nothing needs doing here.


# Characters

Output is UTF-8. The Spectrum's character set is ASCII apart from two
characters, plus the graphics.

| Spectrum | Comes out as |
|---|---|
| 32-126 | unchanged, except for the two below |
| 96 | `£` - where ASCII has a back quote |
| 127 | `©` - where ASCII has "delete" |
| 128-143 | the block graphics, as Unicode quadrant blocks ` ▝▘▀▗▐▚▜▖▞▌▛▄▟▙█` |
| 144-164 | the user defined graphics, see below |
| everything else | dropped |

A user defined graphic has no character of its own, so the plugin can only say
*which* of the 21 was printed, not what it looked like. In a file that is a
circled capital, `Ⓐ` to `Ⓤ`, which needs a font that has those - most ordinary
text fonts do, many terminal fonts do not and draw them too wide. On the console
it is a plain capital shown inverse instead, which always takes exactly one
cell.


# Control Codes

The print position moves within the line, the way a real printer does.

| Code | Effect |
|---|---|
| 6 | `PRINT` comma - moves on to the next 16 column field |
| 8 | cursor left - a character printed afterwards overwrites what was there |
| 9 | cursor right |
| 13 | ends the line |
| 16-21 | `INK`, `PAPER`, `FLASH`, `BRIGHT`, `INVERSE`, `OVER` - ignored, along with the value byte each one carries |
| 22 | `AT` - ignored, along with its two bytes |
| 23 | `TAB` - treated like a comma; its column is not honoured |

Cursor up and down are not supported. Paper only moves one way.


# Console and File

Both destinations end up with the same text, but they get there differently, and
it is worth knowing which you are looking at.

The **console** gets every character the moment it is printed, with VT100
sequences for the cursor movements. You can watch a line take shape, including a
program overprinting a character - which is often exactly what you want to see.
This assumes a terminal that understands VT100, which any modern one does.

A **file** gets nothing until the line is finished, and then the whole line at
once, with the overprinting already resolved into the characters that ended up
on the paper. No escape sequence ever reaches a file. A line the program never
finished is still written out when CSpect quits.


# Requirements

The plugin needs the standard ROM's character output routine to be in place. It
checks this on every single trap, by confirming that the `RST $10` vector really
does jump where it is expected to. Should a ROM be paged in that is laid out
differently, the plugin falls silent rather than printing nonsense.

The 32 column width of the ZX Printer is not emulated; lines grow as long as the
program makes them.


# Installation

Place `Printer.dll` in the root directory of CSpect, next to `CSpect.exe`.
CSpect picks it up on startup, and with `-printer` on the command line you will
see:

~~~
 Printer added
~~~

A typical command line on macOS or Linux, where CSpect needs Mono:

~~~
mono CSpect.exe -w4 -zxnext -nextrom -printer=listing.txt
~~~


# Build

The plugin targets .NET Framework 4.5.2 and references `Plugin.csproj`, which is
built along with it.

~~~
msbuild Printer/Printer.csproj /p:Configuration=Release
~~~

The DLL ends up in `Printer/bin/Release/`. This builds with Mono's msbuild on
macOS and Linux, and with Visual Studio on Windows.


# Acknowledgements

Based on the plugin interface from Mike Dailly's
[CSpect](http://www.cspect.org). The `DebugOut` and `esxDOS` plugins in this
repository served as the model for trapping ROM addresses.

# CSpect Monitor

A debugger front end for CSpect: registers, disassembly, stack, memory and watches in one Dear ImGui window.
Ctrl+Shift+M opens it, and with `-monitor` CSpect uses it as its debugger whenever it stops.

## Source level debugging

Tick **Source** to show the source instead of the disassembly. Step and Step Over then go a source line at a
time, and clicking a line's gutter (or double-clicking the line) sets a breakpoint on its code. The source lines can come from:

| Where from | How |
|---|---|
| A map file with `[FILES]` and `[LINES]` sections | Load it with `-map=`, as before |
| A symbol loader plugin that gives CSpect source lines | e.g. Pasta/80: `-pasta80=game.lst` gives Pascal source |
| z88dk listings | Build with `--list -m`; the Monitor reads the `.lis` files and `.map` itself |

The source is coloured by file type: `;` comments for asm, `//` and `/* */` for C, `//`, `{ }` and `(* *)` for Pascal.

### z88dk (C)

Build with listings and a map file:

    zcc +zxn --list -m -Cc-gcline ... -create-app

* `--list` writes a `.lis` file per source file, and `-m` the `.map` file that places them in memory.
* `-Cc-gcline` makes sccz80 mark every C statement. Without it only the start of each function is marked, so
  a whole function is one source line. It costs a little code size (sccz80 can't optimise across the marks),
  so it's best kept for debug builds.

When CSpect has no source lines of its own, the Monitor uses the `.lis` files and newest `.map` in CSpect's
current folder, and loads them again whenever they change (after a rebuild, the next time the CPU stops).
Your own asm (`.asm` files in the build) and C headers that hold code get source lines too. Library code
has no listing, so source stepping runs straight through it.

| Command | Does |
|---|---|
| `LIS <folder>` | Use the z88dk listings in a folder (or a `.map`/`.lis` file's folder) - even if CSpect has source lines |
| `LIS` | Say what's loaded |
| `LIS OFF` | Back to CSpect's own source lines |

Code outside `PAGE_nn` / `BANK_nn` sections is placed in the pages a NEX starts with (slots 2-7 = pages
10, 11, 4, 5, 0, 1), so code moved to other pages at run time shows the wrong lines.

### Pasta/80 (Pascal)

The Pasta80 symbol loader reads the Pascal lines out of the sjasmplus `.lst` file (Pasta/80 writes each one
into the generated asm as a comment), following `{$I}` includes, so `-pasta80=game.lst` gives Pascal source.
Stepping into the run-time library shows its asm source. This needs a CSpect with `iCSpect.AddSourceFile` /
`AddSourceLine` (4.3 on); an older one loads the symbols only.

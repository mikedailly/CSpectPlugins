# CSpect Palette Viewer plugin

A CSpect v4 plugin that shows the ULA, Layer 2, Sprite and Tilemap palettes as 16x16 grids of colours in a Dear ImGui window. You can edit any colour by double-clicking it.

## Use

- Press **Ctrl+Alt+L** in CSpect to open the window. To use a different key, change `OPEN_KEY` in `PaletteViewerPlugin.cs`.
- There's one tab per layer. Each tab shows its **First** or **Second** palette. With **Follow active** ticked, the tab shows whichever palette the hardware is displaying (NextReg $43 bits 1–3 for ULA, Layer 2 and Sprites; NextReg $6B bit 4 for the tilemap).
- Hover over a colour to see its index, 9-bit value, R/G/B (0–7) and 8-bit RRRGGGBB value. Click a colour to select it.
- **Double-click** a colour to open the editor:
  - Set the colour with the R/G/B sliders (0–7), or type a 9-bit hex value (RRRGGGBBB, `000`–`1FF`).
  - **Live preview** sends each change to the emulator as you make it.
  - **Apply** (or Enter) keeps the change. **Cancel** (or Escape, or the close button) puts the original colour back. Clicking the *Original* swatch resets the sliders to the original value.

### Loading and saving

The **File** menu loads into, or saves, the palette on screen: the current tab plus the First/Second selection. The top of the menu names that palette. The shortcuts are **Ctrl+O** (load) and **Ctrl+S** (save).

Load and Save open a file browser drawn in ImGui, so it looks the same on every platform. Double-click a folder to open it, and double-click a file (or press Enter) to choose it. On Windows there's a drive list. You can also type a path into the box at the top. Saving over an existing file asks you to confirm.

| Format | Size | Layout |
|---|---|---|
| 9-bit Next (default, `.nxp`) | 512 bytes | 2 bytes per colour: `RRRGGGBB`, `0000000B` |
| 8-bit Next (`.pal`) | 256 bytes | 1 byte per colour: `RRRGGGBB`. On load, the 3rd blue bit is B1 OR B0, as on the Next |
| 24-bit RGB (`.pal`) | 768 bytes | 3 bytes per colour, R G B. Rounded to 3 bits per channel on load |

Loading works out the format from the file size. The browser lists `.nxp`, `.npl`, `.pal` and `.bin` files; tick **Show all files** to see everything. Like a manual edit, a loaded palette is written to the emulator through `SetColour`.

Edits are queued and written with `iCSpect.SetColour` on the emulator thread (`Tick`). When the debugger has the machine paused they're written from `OSTick` instead, so you can edit palettes while paused. If the running program rewrites its palette every frame, it will overwrite your edits.

`SetColour` takes a 9-bit value, so editing a Layer 2 colour does not keep its priority bit.

## Build

Open `PaletteViewer.sln` in Visual Studio 2019 or 2022 (with the ".NET desktop development" workload) and build. It's a standard .NET Framework 4.8 class library, the same as CSpect itself.

The project builds on its own: the CSpect assemblies it needs, `Plugin.dll`, `ImGui.NET.dll` and `System.Numerics.Vectors.dll`, are in `lib\`. They aren't copied to the output folder, because CSpect already ships them. If you update CSpect, copy the new versions of those files into `lib\`.

The build produces `bin\Release\PaletteViewer.dll` (or `bin\Debug\`). To install it, copy that file next to `CSpect.exe`. If `..\..\CSpect4_0_1_0\CSpect.exe` exists, the build does the copy for you. To point the copy at another install, change `CSpectDir` near the top of `PaletteViewer.csproj`, or pass it on the command line:

```
msbuild PaletteViewer.sln -p:Configuration=Release -p:CSpectDir=C:\path\to\CSpect\
```

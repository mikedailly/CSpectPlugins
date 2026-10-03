# TileViewer

Live Spectrum Next tile and tilemap viewer/editor for CSpect 3.1.0 or newer. Open it with **Ctrl+Alt+T**.

## Viewing

- **Tiles** shows 256 or 512 patterns in a 16-column grid, with palette and bank 5/7 selection. Hover for the tile number, hex memory address, and map usage.
- **Tilemap** shows the full unscrolled map with palette and transform attributes applied. Hover for cell details.

Use the mouse wheel over either view to zoom in or out (1x–8x). Each view keeps its own zoom level; use the scrollbars to move around.

The viewer reads the map and tile locations from the Next registers and updates as memory, palettes, or registers change. It supports graphics and text modes, including attribute-free maps.

## Editing

Choose **Draw**, **Clear**, or **Pick**, then click or drag on the map:

- **Draw** paints the selected tile, palette, and attributes. Clicking a pattern in the Tiles tab selects it as the brush.
- **Clear** replaces cells with **Clear tile**. Select a blank/transparent pattern to remove visible tiles.
- **Pick** samples a cell's tile and attributes.
- **Undo cell** restores the previous cell edit, unless the program has since changed it.

Right-drag clears; middle-click picks. Editing works while running or paused and changes emulator memory only. A running program can overwrite edits.

## Build and install

Requires MSBuild (Visual Studio Build Tools) and a CSpect installation containing `Plugin.dll`. Targets .NET Framework 4.5.2.

From the repository root in PowerShell:

```powershell
.\TileViewer\Build.ps1 -CSpectDirectory 'path\to\CSpect'
```

The build creates `TileViewer\bin\Release\TileViewer.dll`. Copy it into the CSpect folder and restart the emulator. Add `-Test` to run the checks.

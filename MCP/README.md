# CSpect MCP server

A CSpect plugin that lets AI assistants inspect and control the emulator through the
[Model Context Protocol](https://modelcontextprotocol.io). The assistant can pause and step
the CPU, set breakpoints, read and write memory, disassemble, look up symbols and source lines,
take screenshots and type on the Spectrum keyboard. So it can build your code, run it, watch it
and debug it in a loop.

## Setup

Put `MCP.dll` next to `CSpect.exe` with the other plugins. When CSpect starts, it prints:

```
 MCP server on http://127.0.0.1:8765/mcp
```

The server only listens on the loopback address, so nothing outside your machine can reach it.
It also refuses requests from web pages that aren't served locally.

* **Port:** set the `CSPECT_MCP_PORT` environment variable to change it.
* **Turning it off:** set `CSPECT_MCP_PORT=off`.

### Claude Code

```
claude mcp add --transport http cspect http://127.0.0.1:8765/mcp
```

### Other clients

Any client that supports MCP's Streamable HTTP transport can use the URL above. Clients that only
launch stdio servers can go through a bridge such as
[`mcp-remote`](https://www.npmjs.com/package/mcp-remote):

```json
{
  "mcpServers": {
    "cspect": { "command": "npx", "args": ["mcp-remote", "http://127.0.0.1:8765/mcp"] }
  }
}
```

## Tools

| Tool | What it does |
|---|---|
| `get_status` | Running or stopped, registers, flags, MMU banks, and the source line at PC |
| `set_registers` | Set any registers, e.g. `{"registers":{"HL":"$4000","PC":"main"}}` |
| `read_memory` / `write_memory` | Hex dump, or write hex bytes or text. 64K view, or `physical` Next RAM |
| `disassemble` | Instructions with labels, bytes, T-states and breakpoints |
| `pause` / `run` | Stop and start the CPU |
| `step` / `step_over` / `step_back` | Single step, step over CALLs etc, and rewind |
| `run_to` | Run to an address (temporary breakpoint), with a timeout |
| `wait_for_stop` | Wait for a breakpoint to hit |
| `set_breakpoint` / `clear_breakpoint` / `list_breakpoints` | Execute (64K or physical), read and write breakpoints |
| `evaluate` | Work out an expression, plus the symbol and source line at the result |
| `find_symbols` | Search the map file's symbols |
| `get_source` | Source lines around an address, or a file and line (needs a SNASM map with `[LINES]`) |
| `read_next_registers` / `write_next_register` | Next registers |
| `port_in` / `port_out` | I/O ports |
| `debug_command` | Any CSpect debugger command line (port breakpoints, SAVE, LOAD, LOG...) |
| `screenshot` | The display, border included, as a PNG (640x512, or 320x256) |
| `press_keys` | Press Spectrum keys, e.g. `"CAPS+5 ENTER"`, `"SYM+P"`, `"BREAK"` |
| `type_text` | Type text: letters, digits, space, newline, and the SYM symbols |
| `load_nex` | Load and run a .nex file, or reload the current one |

Addresses and values accept:

* **Numbers:** `$8000`, `#8000`, `0x8000`, `8000h`, `%1010`, `32768`, `'A'`
* **Symbols** from the loaded map file
* **Registers:** `hl`, `ix`, `af'` and so on
* **Maths:** `+ - * / % & | ^ << >>` and brackets, e.g. `sprite_table+4*b`

## Notes

* **Threading:** requests arrive on socket threads, but each one runs on the emulator thread in
  the plugin's `Tick()`, between frames. Calls don't race the emulation. If CSpect is paused, or busy
  with a modal dialog, requests time out after 5 seconds.
* **Keyboard:** keys go through `iCSpect.SetKeyboardRow`, which holds Spectrum keys on top of the
  real keyboard. Older CSpects without it still work, except for `press_keys` and `type_text`.
  Keys are only pressed while the CPU runs.
* **Run to:** if `run_to` times out, the CPU keeps running. Its temporary breakpoint stays until the
  CPU next stops, then it's removed.

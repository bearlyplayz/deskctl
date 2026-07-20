# windeskctl

Desktop capture, input, and UI automation for Windows. One binary, two surfaces: a CLI and an
MCP stdio server over the same commands.

## Install

Download `windeskctl.exe` from the [latest release](../../releases/latest) and put it anywhere on your
`PATH`. It is a single self-contained binary — there is no installer and no .NET runtime to
install alongside it.

## Requirements

To *run* windeskctl: Windows 10 1903+ (Windows.Graphics.Capture). Nothing else — the released binary
carries its own runtime.

To *build* it from source:

- .NET 10 SDK
- To `publish`: the Visual Studio C++ toolchain (NativeAOT links with `link.exe`). The AOT
  targets locate it with `vswhere.exe`, which the installer does not put on `PATH` — build from a
  Developer PowerShell, or prepend
  `C:\Program Files (x86)\Microsoft Visual Studio\Installer` to `PATH`. `build` and `test` do not
  need it.

## Build

```powershell
dotnet build
dotnet test
dotnet publish src/WinDeskCtl/WinDeskCtl.csproj -c Release
```

## Use

```powershell
windeskctl doctor                                  # self-test against this machine
windeskctl doctor --json                           # machine-readable
windeskctl doctor --intrusive                      # adds checks that steal focus and pop Start

windeskctl windows list --title notepad            # DWM-accurate geometry, not GetWindowRect
windeskctl windows move 12345 --x 100 --y -1000    # --x/--y are the VISIBLE edges
windeskctl windows resize 12345 --width 800 --height 600
windeskctl windows focus 12345
windeskctl windows minimize 12345                  # also: maximize, restore

windeskctl capture --target monitor:1 --out shot.png
windeskctl capture --target win:12345 --region 0,0,400,300 --max-width 1280
windeskctl capture --target win:12345 --format jpeg --quality 75 --out shot.jpg

windeskctl record --target win:12345 --out-dir ./burst   # frames over time, to see motion

windeskctl snapshot win:12345                      # UI element tree
windeskctl snapshot win:12345 --json --max-depth 6

windeskctl input '[{"down":{"key":"ctrl"}},{"press":{"key":"c"}},{"up":{"key":"ctrl"}}]'
windeskctl input --snapshot win:12345 '[{"invoke":{"target":"elem:btn-save"}}]'

windeskctl mcp                                     # serve MCP over stdio
```

**Full documentation: [docs/Usage.md](docs/Usage.md)** — one page per command, plus the coordinate
concepts every command shares.

| Page | Covers |
|---|---|
| [doctor](docs/doctor.md) | Self-test the machine; read monitor ids. Run it first when coordinates look wrong. |
| [windows](docs/windows.md) | Find windows; focus, move, resize, minimize, maximize, restore. |
| [capture](docs/capture.md) | Pixels of a window or monitor, with the frame rect describing their space. |
| [record](docs/record.md) | A burst of frames to disk, for motion a single capture cannot show. |
| [snapshot](docs/snapshot.md) | The UI as a tree of named elements — the default way to see the screen. |
| [input](docs/input.md) | Mouse and keyboard as one atomic batch, and the full step grammar. |
| [mcp](docs/mcp.md) | Serving the Model Context Protocol over stdio. |

Every command takes a frame-qualified target, so an image and a click can never disagree about
which space they are in. A point is `<frame>@<x>,<y>`, or `<frame>` alone for its centre. Run
`doctor` first when coordinates behave unexpectedly — it measures this machine rather than
assuming.

Register the MCP server with a client by pointing it at `windeskctl.exe` with the `mcp` argument.
There is no port and no socket: the transport is stdio, so the OS process boundary is the security
model.

## Layout

| Project | Target | Rule |
|---|---|---|
| `src/WinDeskCtl.Core` | `net10.0` | Pure. Frames, coordinate maths, command contracts. Cannot reference Win32 — the TFM enforces it. |
| `src/WinDeskCtl.Platform` | `net10.0-windows` | Every P/Invoke and WinRT call. |
| `src/WinDeskCtl` | `net10.0-windows` | The `windeskctl` binary: CLI and MCP adapters only. |
| `tests/WinDeskCtl.Core.Tests` | `net10.0` | Unit tests. No mocks — Core has nothing to mock. |

`doctor` is the calibration surface: display topology, DPI, and drag thresholds are measured at
runtime, never hardcoded and never committed.

## Coordinate spaces

Two spaces exist and conflating them is the bug this project is built to prevent:

- A **frame point** is measured from its frame's top-left, so it is never negative. `FrameRect.Origin`
  records where that frame sits in absolute screen coordinates. `Translate` converts between frames.
- An **absolute screen point** is measured from the *primary* monitor's top-left, so a monitor
  above or left of primary has genuinely negative coordinates.

They are identical whenever the virtual desktop's origin is `0,0` — every single-monitor box and
every side-by-side layout — which is why mixing them survives review and fails in the field.
`ScreenCoords` is the only crossing between the two; every Win32 call taking or returning screen
coordinates goes through it.

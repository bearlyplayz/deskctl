# deskctl

Desktop capture, input, and UI automation for Windows. One binary, two surfaces: a CLI and an
MCP stdio server over the same commands.

## Install

Download `deskctl.exe` from the [latest release](../../releases/latest) and put it anywhere on your
`PATH`. It is a single self-contained binary — there is no installer and no .NET runtime to
install alongside it.

## Requirements

To *run* deskctl: Windows 10 1903+ (Windows.Graphics.Capture). Nothing else — the released binary
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
dotnet publish src/Deskctl/Deskctl.csproj -c Release
```

## Use

```powershell
deskctl doctor                                  # self-test against this machine
deskctl doctor --json                           # machine-readable
deskctl doctor --intrusive                      # adds checks that steal focus and pop Start

deskctl windows list --title notepad            # DWM-accurate geometry, not GetWindowRect
deskctl windows move 12345 --x 100 --y -1000    # --x/--y are the VISIBLE edges
deskctl windows resize 12345 --width 800 --height 600
deskctl windows focus 12345
deskctl windows minimize 12345                  # also: maximize, restore

deskctl capture --target monitor:1 --out shot.png
deskctl capture --target win:12345 --region 0,0,400,300 --max-width 1280
deskctl capture --target win:12345 --format jpeg --quality 75 --out shot.jpg

deskctl snapshot win:12345                      # UI element tree
deskctl snapshot win:12345 --json --max-depth 6

deskctl input '[{"down":{"key":"ctrl"}},{"press":{"key":"c"}},{"up":{"key":"ctrl"}}]'
deskctl input --snapshot win:12345 '[{"invoke":{"target":"elem:btn-save"}}]'

deskctl mcp                                     # serve MCP over stdio
```

Every command takes a frame-qualified target, so an image and a click can never disagree about
which space they are in. Run `doctor` first when coordinates behave unexpectedly — it measures
this machine rather than assuming.

A point is `<frame>@<x>,<y>`, or `<frame>` alone for its centre. Anything a batch leaves held is
released automatically, newest first, and reported back in `released` — that release is real
input, so a dangling `win` opens the Start menu. A batch that fails part-way unwinds too, and
names what it released in the error.

`--max-width`/`--max-height` are the lever worth reaching for: image cost tracks pixel
dimensions, not bytes, so downscaling saves tokens and `--format jpeg` saves none. Any downscale
sets `scale` in the returned rect rather than rescaling silently. JPEG is there for when bytes
genuinely cost something, such as writing to disk.

Register the MCP server with a client by pointing it at `deskctl.exe` with the `mcp` argument.
It exposes the same commands as tools: `doctor`, `windows`, `window_action`, `capture`,
`snapshot`, `input`. There is no port and no socket: the transport is stdio, so the OS process
boundary is the security model.

Element handles (`elem:...`) are minted by `snapshot` and scoped to the process that minted them,
so a CLI `snapshot` in one process cannot be referenced by a separate `input` process — pass
`input --snapshot <target>` for that, or hold one MCP session, where both share a process.

## Layout

| Project | Target | Rule |
|---|---|---|
| `src/Deskctl.Core` | `net10.0` | Pure. Frames, coordinate maths, command contracts. Cannot reference Win32 — the TFM enforces it. |
| `src/Deskctl.Platform` | `net10.0-windows` | Every P/Invoke and WinRT call. |
| `src/Deskctl` | `net10.0-windows` | The `deskctl` binary: CLI and MCP adapters only. |
| `tests/Deskctl.Core.Tests` | `net10.0` | Unit tests. No mocks — Core has nothing to mock. |

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

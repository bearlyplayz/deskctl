# Usage

windeskctl is a single Windows binary that captures the screen, reads UI, and sends input. It has two
surfaces over the same set of commands:

- a **CLI** — `windeskctl <command>`, one process per invocation
- an **MCP stdio server** — `windeskctl mcp`, one long-lived process that exposes the same commands as
  tools to an MCP client (an LLM agent, typically)

Everything works identically on both. Where the CLI and the MCP tool differ only in spelling, both
forms are shown.

**Read [Concepts you need first](#concepts-you-need-first) below before the command pages.** Every
command takes a frame-qualified target, and that one idea is what keeps a screenshot and a click from
disagreeing about where something is.

## Commands

| Page | Command | What it does |
|---|---|---|
| [doctor](doctor.md) | `doctor` | Self-test against the live machine. Read monitor ids here. Run it first when coordinates look wrong. |
| [launch](launch.md) | `launch` | Start a program, log its output, and get back the window it opened. |
| [windows](windows.md) | `windows list`, `windows <action>` | Find windows with DWM-accurate geometry; focus, move, resize, minimize, maximize, restore. |
| [capture](capture.md) | `capture` | Pixels of a window or monitor, with the frame rect describing their space. |
| [record](record.md) | `record` | A short burst of frames to disk, for motion a single capture cannot show. |
| [snapshot](snapshot.md) | `snapshot` | The UI as a tree of named elements. The **default** way to see what is on screen. |
| [input](input.md) | `input` | Mouse and keyboard as one atomic batch. The step grammar lives here. |
| [mcp](mcp.md) | `mcp` | Serve the Model Context Protocol over stdio. |

**Where to start:** `doctor` to check the machine, then `windows list` to find your target — or
`launch` if the program is not running yet, which starts it and hands back the window in one step.
Then `snapshot` to see what is in it. Reach for `capture` only when there is no element tree —
canvases, video, games, remote desktop.

---

## Concepts you need first

**Targets are frame-qualified.** Everything you point at names the coordinate frame it lives in, so
an image and a click can never disagree about which space they mean:

| Target | Means |
|---|---|
| `monitor:<id>` | A monitor. Get ids from [`doctor`](doctor.md). |
| `win:<hwnd>` | A window. Get hwnds from [`windows`](windows.md). |
| `elem:<handle>` | A UI element. Get handles from [`snapshot`](snapshot.md). |
| `img:<handle>` | A captured image. Minted by [`capture`](capture.md); a point in it is a pixel coordinate read off that image, and windeskctl applies the capture's scale. |

**A point is `<frame>@<x>,<y>`**, or just `<frame>` on its own for that frame's centre. So
`win:12345@400,200` is 400,200 measured from that window's top-left; `elem:btn-save` is the centre of
that element. Coordinates inside a frame are always measured from the frame's top-left and are never
negative.

**Two coordinate spaces exist and windeskctl hides the harder one.** A *frame point* is measured from
its frame's top-left. An *absolute screen point* is measured from the primary monitor, and goes
negative for monitors above or left of primary. You always work in frame points; windeskctl converts.
The two happen to be equal on single-monitor and simple side-by-side layouts, which is exactly why
hand-computed coordinates work on your desk and break on someone else's. Let windeskctl do it.

**Run [`doctor`](doctor.md) first when coordinates behave unexpectedly.** Display topology, DPI, and
drag thresholds are measured on the live machine, never assumed.

**Element and image handles are process-scoped.** `elem:` handles are minted by
[`snapshot`](snapshot.md), `img:` handles by [`capture`](capture.md), and both live only in the
process that minted them. One MCP session is one process, so handles persist across tool calls.
Each CLI invocation is its own process, so a handle from `windeskctl snapshot` is already dead when
a separate `windeskctl input` runs — use `input --snapshot <target>` to do both in one process.

---

## Exit codes and errors

The CLI exits `0` on success and `1` on failure. A **refusal** — a caller mistake like a malformed
target or a bad step — is part of the contract: its message is the payload. The CLI prints that
message and exits `1`; the MCP server raises it as an `McpException` the client can read. An
unexpected internal error keeps its full stack trace instead, so the two are easy to tell apart.

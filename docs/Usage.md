# Usage

windeskctl is a single Windows binary that captures the screen, reads UI, and sends input. It has two
surfaces over the same set of commands:

- a **CLI** — `windeskctl <command>`, one process per invocation
- an **MCP stdio server** — `windeskctl mcp`, one long-lived process that exposes the same commands as
  tools to an MCP client (an LLM agent, typically)

Everything below works identically on both. Where the CLI and the MCP tool differ only in spelling,
both forms are shown.

## Concepts you need first

**Targets are frame-qualified.** Everything you point at names the coordinate frame it lives in, so
an image and a click can never disagree about which space they mean:

| Target | Means |
|---|---|
| `monitor:<id>` | A monitor. Get ids from `doctor`. |
| `win:<hwnd>` | A window. Get hwnds from `windows`. |
| `elem:<handle>` | A UI element. Get handles from `snapshot`. |

**A point is `<frame>@<x>,<y>`**, or just `<frame>` on its own for that frame's centre. So
`win:12345@400,200` is 400,200 measured from that window's top-left; `elem:btn-save` is the centre of
that element. Coordinates inside a frame are always measured from the frame's top-left and are never
negative.

**Two coordinate spaces exist and windeskctl hides the harder one.** A *frame point* is measured from
its frame's top-left. An *absolute screen point* is measured from the primary monitor, and goes
negative for monitors above or left of primary. You always work in frame points; windeskctl converts.
The two happen to be equal on single-monitor and simple side-by-side layouts, which is exactly why
hand-computed coordinates work on your desk and break on someone else's. Let windeskctl do it.

**Run `doctor` first when coordinates behave unexpectedly.** Display topology, DPI, and drag
thresholds are measured on the live machine, never assumed.

---

## doctor — self-test the machine

Reports virtual-desktop bounds, every monitor's origin / size / DPI, and the result of each internal
check. This is where you read monitor ids for `capture`, and the first thing to run when anything
coordinate-related looks wrong.

```powershell
windeskctl doctor              # human-readable summary
windeskctl doctor --json       # the raw report as JSON
windeskctl doctor --intrusive  # add checks that disturb the desktop (may steal focus, pop Start)
```

MCP: tool `doctor`, argument `includeIntrusive` (default `false`).

The `--intrusive` checks actually inject input to prove input works end-to-end, so they can steal
focus. Leave them off unless a plain `doctor` passes but real input still misbehaves. Exit code is
`0` when every check passes, `1` otherwise.

---

## windows — find windows

Lists windows with **DWM-accurate geometry** — the rect as actually drawn, not the oversized one
`GetWindowRect` reports. The hwnd it returns is what you feed to `capture`, `snapshot`, and `input`
as `win:<hwnd>`.

```powershell
windeskctl windows list                       # every window
windeskctl windows list --title notepad       # title contains "notepad" (case-insensitive)
windeskctl windows list --process chrome      # from the chrome process
windeskctl windows list --json                # JSON instead of a table
```

In the table, a `*` marks the foreground window. Filter rather than dumping everything when you know
what you want.

MCP: tool `windows`, arguments `titleContains`, `processName`, `includeMinimized` (default `true`).

---

## windows &lt;action&gt; / window_action — move and manage windows

Focus, move, resize, minimize, maximize, or restore a window. Move and resize coordinates address
the **visible** edges of the window — windeskctl converts to the raw rect Win32 wants, so what you ask
for is what you see.

```powershell
windeskctl windows focus 12345
windeskctl windows move 12345 --x 100 --y -1000        # visible top-left corner
windeskctl windows resize 12345 --width 800 --height 600
windeskctl windows minimize 12345
windeskctl windows maximize 12345
windeskctl windows restore 12345
```

MCP collapses these into one tool `window_action` with an `action` argument
(`focus | move | resize | minimize | maximize | restore`) plus optional `x`, `y`, `width`, `height`
for move/resize.

Both surfaces **re-read the window afterwards and report its actual state**, which can differ from
what you asked for because Windows clamps to screen bounds and snaps to edges. Trust the reported
rect, not the request.

---

## capture — get pixels

Captures a window or monitor as an image, including when the window is occluded or
hardware-accelerated (games, video, GPU canvases). Returns the image **together with the frame rect
describing its coordinate space** — feed that rect back when clicking so the click lands where you
saw it.

```powershell
windeskctl capture --target monitor:1 --out shot.png
windeskctl capture --target win:12345 --region 0,0,400,300 --out crop.png   # sub-rect: x,y,w,h
windeskctl capture --target win:12345 --max-width 1280 --out small.png       # downscale
windeskctl capture --target win:12345 --format jpeg --quality 75 --out shot.jpg
```

Without `--out`, the CLI prints the frame rect and does **not** print the image — an image whose
frame you never see is the bug this design prevents.

MCP: tool `capture`, arguments `target`, `region`, `maxWidth`, `maxHeight`, `format`, `quality`. It
returns the image as a viewable image block plus the rect as text.

**Prefer `snapshot` over `capture` when the surface has an element tree** — you get named, clickable
elements instead of pixels to reason about spatially. Reach for `capture` for canvases, video, games,
and remote-desktop sessions that have no tree.

**Downscaling is the lever that matters.** Image cost tracks pixel dimensions, not bytes, so
`--max-width` / `--max-height` genuinely save an agent's tokens while `--format jpeg` saves nothing —
PNG is lossless and keeps small text readable. Any downscale records the factor in the returned
rect's `scale` rather than rescaling silently. Use JPEG only when bytes literally cost something,
like writing to disk.

---

## record — capture motion as a burst of frames

Captures a short burst of frames to disk, one file per frame, so a caller can see motion a single
`capture` cannot show — a spinner turning, a progress bar filling, a video playing, a transition.
It's `capture` run on a schedule; every option `capture` takes, `record` takes too. It writes ordered
zero-padded files (`frame_0.png`, `frame_1.png`, …) and returns the **list of paths, not the images**.

```powershell
windeskctl record --target win:12345 --out-dir ./burst                       # default preset (fast)
windeskctl record --target win:12345 --out-dir ./burst --preset slow
windeskctl record --target win:12345 --out-dir ./burst --region 1720,945,400,400   # crop to the moving part
windeskctl record --target monitor:1 --out-dir ./burst --preset slow --format jpeg --quality 75
```

`--out-dir` is required and is created if missing. The CLI prints the frame rect and a frame count,
then one file path per line.

MCP: tool `record`, arguments `target`, `outputDir`, `preset`, `region`, `maxWidth`, `maxHeight`,
`format`, `quality`. It returns the file list as text plus guidance — never inline images. Open the
**first, a middle, and the last** frame to read the motion, and pull more only if you need finer
detail.

**Rate and duration come as presets, not free numbers**, so no request can flood the disk or an
agent's context — every preset caps the burst at 30 frames:

| Preset | Rate | Duration | Frames | For |
|---|---|---|---|---|
| `slow` | 3 fps | 10 s | 30 | Long, slow changes — a download bar, a multi-second transition. |
| `medium` | 6 fps | 5 s | 30 | General motion when you don't know the timescale. |
| `fast` (default) | 9 fps | 1 s | 9 | A quick, repeating animation — a spinner, a pulse. |
| `instant` | 12 fps | 0.5 s | 6 | A brief flash or the first moment after a click. |

**Crop to the moving part with `--region`.** A throbber is a few hundred pixels inside a 4K window;
cropping to it makes each frame small and the motion legible instead of a speck. The returned rect
reports the crop's own coordinate space, same as `capture`.

**If every frame looks identical, the region is static — or you sampled it at its own cycle.** An
animation whose period lines up with the sampling rate lands on the same phase each frame and reads as
frozen. Re-run with a different preset to break the alignment.

---

## snapshot — read the UI as a tree

Reads a window's UI as a tree of named, typed elements. This is the **default** way to see what's on
screen. Each element gets an opaque `elem:<handle>` you can act on directly with `input` — no
coordinates, so you cannot miss.

```powershell
windeskctl snapshot win:12345                      # interactive elements, as a tree
windeskctl snapshot win:12345 --all                # include non-interactive layout elements
windeskctl snapshot win:12345 --max-depth 6        # walk shallower
windeskctl snapshot win:12345 --json               # JSON instead of a tree
windeskctl snapshot elem:panel-3                   # scope to a subtree
```

MCP: tool `snapshot`, arguments `target`, `maxDepth` (default `12`), `all` (default `false`).

Handles are minted here and scoped to the process that minted them — **do not construct or guess
them**, and re-run `snapshot` after the UI changes. The output tags each element with the automation
patterns it supports (e.g. what `invoke` or `fill` will work on) and marks disabled elements.

Handle lifetime is the one thing that differs between surfaces:

- **MCP:** one session is one process, so a `snapshot` handle stays valid for later `input` calls.
- **CLI:** each command is its own process, so a handle from `windeskctl snapshot` is dead by the time
  a separate `windeskctl input` runs. Use `input --snapshot <target>` (below) to snapshot and act in one
  process.

`snapshot --vision` deliberately does nothing but point you at `capture` — asking the tree command
for pixels is answered, not silently served the wrong shape.

---

## input — send mouse and keyboard steps

Sends a batch of steps as **one atomic operation**. Steps with no delay coalesce into a single OS
call that other input cannot interleave with — that's what makes cross-device combos (Ctrl+click) and
real drags work.

Steps are a JSON array. From the CLI, pass the array as an argument, or `-` to read it from stdin:

```powershell
windeskctl input '[{"down":{"key":"ctrl"}},{"press":{"key":"c"}},{"up":{"key":"ctrl"}}]'
windeskctl input --snapshot win:12345 '[{"invoke":{"target":"elem:btn-save"}}]'
```

`--snapshot <target>` snapshots the window first, in the same process, so `elem:` handles in the
steps resolve. It's required for `invoke` / `fill` / `waitFor` from the CLI, because handles don't
outlive a process.

MCP: tool `input`, argument `steps` (the JSON array).

### The step grammar

Each step is an object with **exactly one verb**. Two verbs in one object, or a missing/unknown verb,
is rejected loudly — a dropped step is worse than a rejected batch, because you'd believe it ran.

| Step | Example | Does |
|---|---|---|
| `down` | `{"down":{"key":"ctrl"}}` | Press and hold a key or button. |
| `up` | `{"up":{"key":"ctrl"}}` | Release a held key or button. |
| `press` | `{"press":{"key":"c"}}` | Tap (down then up). |
| `press` (click) | `{"press":{"button":"left","to":"elem:row-1"}}` | Move to a point, then click. |
| `move` | `{"move":{"to":"win:123@400,200","over":"250ms","ease":"easeOut"}}` | Move the pointer. |
| `scroll` | `{"scroll":{"dy":-3,"at":"elem:list"}}` | Scroll (`dy`/`dx` in notches). |
| `text` | `{"text":"hello"}` | Type a literal Unicode string, layout-independent. |
| `invoke` | `{"invoke":{"target":"elem:btn-save"}}` | Activate an element via its automation pattern. |
| `fill` | `{"fill":{"target":"elem:txt-search","value":"hello"}}` | Set an element's value directly. |
| `waitFor` | `{"waitFor":{"target":"elem:dialog","timeout":"5s"}}` | Poll until an element exists, or time out. |
| `delay` | `{"delay":{"duration":"200ms"}}` | Wait between steps. |

Rules that bite:

- **`down`/`up`/`press` take either `key` or `button`, never both, and the tag is required** —
  `left` and `right` are both mouse buttons and arrow keys, so the shape has to say which. Buttons:
  `left`, `right`, `middle`, `x1`, `x2`.
- **Durations are strings with a unit** — `"250ms"`, `"5s"`. A bare number is rejected because it's
  ambiguous between milliseconds and seconds, and guessing wrong is a 1000× error.
- **A `move` with no `over` teleports.** That's right for positioning but wrong for dragging: Windows
  starts no drag until the pointer travels past its drag threshold with a button down. A drag needs a
  *timed* move between the `down` and the `up`.
- **`ease`** is `linear` (default), `easeIn`, `easeOut`, or `easeInOut`, for the motion curve of a
  timed move.

### Drag example

The middle move is timed on purpose — without it the app sees a click, not a drag:

```json
[{"move":{"to":"elem:tab-3"}},
 {"down":{"button":"left"}},
 {"move":{"to":"win:123@400,200","over":"250ms","ease":"easeOut"}},
 {"up":{"button":"left"}}]
```

### Held input is always released

Anything a batch leaves held is released automatically, **newest first**, and reported back in
`released`. That release is real input — a dangling `win` key opens the Start menu and steals focus —
so the report is how you discover why focus moved. Emit your own `up` steps to control the order. A
batch that throws part-way unwinds the same way and names what it released in the error.

The result also reports `reResolved`: elements whose cached reference had died and were matched again
by selector. That's a heuristic — it finds *an* element matching the selector, not provably the one
you snapshotted — so re-run `snapshot` if it matters.

---

## mcp — run as a server

```powershell
windeskctl mcp
```

Serves the Model Context Protocol over stdio. Register it with an MCP client by pointing the client
at `windeskctl.exe` with the `mcp` argument. It exposes seven tools: `doctor`, `windows`,
`window_action`, `capture`, `record`, `snapshot`, `input`.

There is no port and no socket — the transport is stdio, so the OS process boundary is the security
model. Logs go to stderr; stdout is the protocol.

---

## Exit codes and errors

The CLI exits `0` on success and `1` on failure. A **refusal** — a caller mistake like a malformed
target or a bad step — is part of the contract: its message is the payload. The CLI prints that
message and exits `1`; the MCP server raises it as an `McpException` the client can read. An
unexpected internal error keeps its full stack trace instead, so the two are easy to tell apart.

[← Usage index](Usage.md)

# input — send mouse and keyboard steps

Sends a batch of steps as **one atomic operation**. Steps with no delay coalesce into a single OS
call that other input cannot interleave with — that's what makes cross-device combos (Ctrl+click) and
real drags work.

Steps are a JSON array. From the CLI, pass the array as an argument, or `-` to read it from stdin:

```powershell
windeskctl input '[{"down":{"key":"ctrl"}},{"press":{"key":"c"}},{"up":{"key":"ctrl"}}]'
windeskctl input --snapshot win:12345 '[{"invoke":{"target":"elem:btn-save"}}]'
```

`--snapshot <target>` runs [`snapshot`](snapshot.md) on the window first, in the same process, so
`elem:` handles in the steps resolve. It's required for `invoke` / `fill` / `waitFor` from the CLI, because handles don't
outlive a process.

MCP: tool `input`, argument `steps` (the JSON array).

## The step grammar

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
| `capture` | `{"capture":{"target":"win:123","path":"C:/t/a.png"}}` | Screenshot to a file, mid-batch. |
| `record` | `{"record":{"target":"win:123","outputDir":"C:/t/b","background":true}}` | Frame burst to a directory, mid-batch. |

## Shared property types

Three property types recur across the steps below. They are strict on purpose.

**Point** (`to`, `at`) — `<frame>@<x>,<y>`, or a bare `<frame>` for that frame's centre. See
[Concepts you need first](Usage.md#concepts-you-need-first). A bare frame is the safer form: it needs no
arithmetic from you and cannot drift when the element moves.

**Duration** (`over`, `timeout`, `duration`) — a **string with an explicit unit**: `"250ms"`,
`"1.5s"`. A bare number like `250` is rejected, because it is ambiguous between milliseconds and
seconds and guessing wrong is a 1000x error in whichever direction hurts.

**Target** (`key` / `button`) — `down`, `up`, and `press` need exactly one of these, and the tag is
required because `left`, `right`, `up`, and `down` are all *both* mouse buttons and arrow keys.
Supplying both is rejected.

- `button` — one of `left`, `right`, `middle`, `x1`, `x2`. Case-insensitive.
- `key` — a named key, case-insensitive. Modifiers `ctrl` (`control`), `shift`, `alt` (`menu`),
  `win` (`lwin`, `rwin`); `enter` (`return`), `esc` (`escape`), `tab`, `space`, `backspace` (`bksp`),
  `delete` (`del`), `insert` (`ins`); arrows `left`, `up`, `right`, `down`; `home`, `end`,
  `pageup` (`pgup`), `pagedown` (`pgdn`); `f1`–`f24`; letters `a`–`z`; digits `0`–`9`; `capslock`,
  `numlock`, `scrolllock`, `printscreen` (`prtsc`), `pause`, `apps` (`menukey`). An unknown name is
  rejected with the three closest matches suggested.

Key names resolve to virtual-key codes, which is what ordinary apps read. Games reading raw scan
codes via DirectInput will not see them. To type characters, use `text`, not a chain of `press`.

## Each step, property by property

### `down` / `up` — hold and release

| Property | Type | Required | Notes |
|---|---|---|---|
| `key` | key name | one of `key`/`button` | Held in the batch's held-set until released. |
| `button` | button name | one of `key`/`button` | Same, for mouse buttons. |

```json
[{"down":{"key":"ctrl"}}, {"press":{"key":"a"}}, {"up":{"key":"ctrl"}}]
```

`up` removes from the held-set at any position, not just the end, so `[down ctrl, down shift, up
ctrl, up shift]` is legal. Anything still held when the batch ends is released for you — see
[Held input is always released](#held-input-is-always-released).

### `press` — tap, or click at a point

| Property | Type | Required | Notes |
|---|---|---|---|
| `key` | key name | one of `key`/`button` | Sends down then up as one unit. |
| `button` | button name | one of `key`/`button` | |
| `to` | point | no | Move here first. Omit to act wherever the pointer already is. |

```json
{"press":{"key":"enter"}}
{"press":{"button":"left","to":"elem:btn-save"}}
{"press":{"button":"right","to":"win:12345@400,200"}}
```

`to` makes this a click-at-a-place in one step, which is both shorter and safer than a `move`
followed by a `press` — nothing can move the window in between.

### `move` — position the pointer

| Property | Type | Required | Notes |
|---|---|---|---|
| `to` | point | **yes** | Destination. |
| `over` | duration | no | Travel time. **Omitted means teleport.** |
| `ease` | `linear` / `easeIn` / `easeOut` / `easeInOut` | no | Motion curve. Default `linear`. Only meaningful with `over`. |

```json
{"move":{"to":"elem:canvas"}}
{"move":{"to":"win:12345@400,200","over":"250ms","ease":"easeOut"}}
```

A teleport is correct for positioning and **wrong for dragging**: Windows starts no drag until the
pointer travels past `SM_CXDRAGWIDTH` with a button down. A drag needs a *timed* move between the
`down` and the `up`.

### `scroll` — wheel notches

| Property | Type | Required | Notes |
|---|---|---|---|
| `dy` | integer | no | Vertical notches. Negative scrolls **down** (content moves up). Default `0`. |
| `dx` | integer | no | Horizontal notches. Positive scrolls **right**. Default `0`. |
| `at` | point | no | Move here first. Omit to scroll wherever the pointer is. |

```json
{"scroll":{"dy":-3,"at":"elem:list"}}
{"scroll":{"dx":2}}
```

Both `dy` and `dx` may be set in one step; each non-zero axis emits its own wheel event. A step with
both at `0` emits nothing.

### `text` — type a literal string

| Property | Type | Required | Notes |
|---|---|---|---|
| *(body)* | string | **yes** | A bare string, not an object. |

```json
{"text":"hello, wörld"}
```

The only step whose body is not an object. Sent via `KEYEVENTF_UNICODE`, so it is layout-independent
and handles any character — no modifier juggling, no dead keys. It names no frame, so it lands in
whatever holds the foreground; see
[`focused`](#focused-tells-you-whether-the-desktop-fought-back).

### `invoke` — activate an element

| Property | Type | Required | Notes |
|---|---|---|---|
| `target` | `elem:<handle>` | **yes** | Must be an element. A coordinate has nothing to invoke. |

```json
{"invoke":{"target":"elem:btn-save"}}
```

Uses the element's UI Automation pattern instead of synthesizing a click, so it cannot miss, cannot
race a moving window, needs no focus, and works while the window is occluded. Prefer it over
`press` + `to` whenever the element exposes a pattern.

### `fill` — set a value directly

| Property | Type | Required | Notes |
|---|---|---|---|
| `target` | `elem:<handle>` | **yes** | |
| `value` | string | **yes** | Replaces the existing value; not appended. |

```json
{"fill":{"target":"elem:txt-search","value":"windeskctl"}}
```

One call rather than a keystroke per character. Faster, and immune to autocomplete stealing your
input mid-type — but some apps only fire their change handlers on real keystrokes, so fall back to
`press` + `text` if a filled field does not take effect.

### `waitFor` — poll until an element exists

| Property | Type | Required | Notes |
|---|---|---|---|
| `target` | `elem:<handle>` | **yes** | |
| `timeout` | duration | **yes** | Fails the batch when it expires. |

```json
{"waitFor":{"target":"elem:dialog-confirm","timeout":"5s"}}
```

Replaces sleeping a guessed interval: it returns as soon as the element appears, and fails loudly
rather than letting later steps click at nothing. Use this after any step that triggers a load.

### `delay` — wait

| Property | Type | Required | Notes |
|---|---|---|---|
| `duration` | duration | **yes** | |

```json
{"delay":{"duration":"200ms"}}
```

A `delay` also **breaks the batch's atomicity**: steps before and after it go out as separate OS
calls, so other input can interleave. That is exactly what you want between two windows, and exactly
what you do not want in the middle of a modifier combo.

### `capture` — screenshot mid-batch

Sees the desktop between two steps — before a click, after a fill — without ending the batch. The
image goes to a **file, never into the response**; the result's `captured` list reports each file
with its `img:` frame and rect, so you (or a later step in the same batch) can click what it shows
by image coordinates. See [`capture`](capture.md) for the shared options.

| Property | Type | Required | Notes |
|---|---|---|---|
| `target` | `win:<hwnd>` / `monitor:<id>` | **yes** | Fixed at parse; not a point, no `@`. |
| `path` | string | **yes** | File to write. Directories are created as needed. |
| `region` | `"x,y,w,h"` | no | Sub-rectangle, in the target's units. |
| `maxWidth` | integer | no | Downscale cap. **Defaults to 1200** when neither cap is set; the rect's `scale` always reports the applied factor. |
| `maxHeight` | integer | no | Downscale cap. |
| `format` | `png` / `jpeg` | no | Default `png`. |
| `quality` | integer | no | JPEG quality 1–100, default 90. Ignored for PNG. |
| `ocr` | boolean | no | Recognize text; runs on the full-resolution pixels, rects come back in image coordinates. See [capture](capture.md#ocr). |
| `ocrFilter` | string or array | no | Return only OCR lines containing any of these, case-insensitively. Implies `ocr`. |

```json
{"capture":{"target":"win:123","path":"C:/t/before.png","ocr":true}}
```

Blocks only for the screenshot itself. Capturing does **not** focus the target — it can see
occluded windows, and a mid-drag capture must not move the foreground.

### `record` — film the batch's own steps

Writes a burst of frames to a directory, mid-batch. Two modes:

- **`background: false`** (default) — the full burst runs before the next step. For watching the
  app react to the step *before* it: click, then record the animation the click started.
- **`background: true`** — the burst starts and the batch continues, so the frames film what the
  *following* steps do: a drag in progress, a hover state, an animation being driven. The batch
  waits for the burst to finish before returning — and when the batch throws, the burst is still
  allowed to finish, because its frames are exactly what shows why the batch failed.

| Property | Type | Required | Notes |
|---|---|---|---|
| `target` | `win:<hwnd>` / `monitor:<id>` | **yes** | |
| `outputDir` | string | **yes** | Created if absent. Frames are `frame_NNN` files in capture order. |
| `preset` | `slow` / `medium` / `fast` / `instant` | no | Rate/duration pairing, ≤30 frames. Default `fast`. See [record](record.md). |
| `background` | boolean | no | Default `false`. |
| `region` / `maxWidth` / `maxHeight` / `format` / `quality` | | no | As on `capture`, except **no default width cap** — burst frames keep source resolution unless capped. |

```json
[{"record":{"target":"win:123","outputDir":"C:/t/drag","preset":"medium","background":true}},
 {"move":{"to":"elem:slider"}},
 {"down":{"button":"left"}},
 {"move":{"to":"win:123@600,200","over":"1.5s"}},
 {"up":{"button":"left"}}]
```

The result's `recorded` list reports each burst's files, rect, and `img:` frame — one frame for
the whole burst, because every frame shares the same rect.

## Rules that bite

- **A `move` with no `over` teleports** — see `move` above. This is the most common cause of "my drag
  registered as a click".
- **Durations need a unit.** `"250ms"`, not `250`.
- **A `win:` or `elem:` target focuses that window, and keeps re-asserting it.** Naming a window is
  taken as intent to interact with it, so no separate `windows focus` call is needed. Points in
  `virtual:` or `monitor:` frames name no window and focus nothing. Pass `--no-focus` (CLI) or
  `focus: false` (MCP) to leave the foreground alone — right for hovering or scrolling a background
  window on purpose, or for reaching a window that refuses activation, since a refused activation
  fails the whole batch.
- **If a batch names two different windows without a `delay` between them**, both are focused before
  any event is sent: clicks still land correctly because mouse events route by cursor position, but
  typing goes to whichever window was focused last. Put a `delay` between them.
- **`invoke` / `fill` / `waitFor` need `elem:` handles**, which come from `snapshot`. From the CLI
  that means `input --snapshot <target>`, because handles do not outlive a process.

## Drag example

The middle move is timed on purpose — without it the app sees a click, not a drag:

```json
[{"move":{"to":"elem:tab-3"}},
 {"down":{"button":"left"}},
 {"move":{"to":"win:123@400,200","over":"250ms","ease":"easeOut"}},
 {"up":{"button":"left"}}]
```

## Held input is always released

Anything a batch leaves held is released automatically, **newest first**, and reported back in
`released`. That release is real input — a dangling `win` key opens the Start menu and steals focus —
so the report is how you discover why focus moved. Emit your own `up` steps to control the order. A
batch that throws part-way unwinds the same way and names what it released in the error.

The result also reports `reResolved`: elements whose cached reference had died and were matched again
by selector. That's a heuristic — it finds *an* element matching the selector, not provably the one
you snapshotted — so re-run `snapshot` if it matters.

## `focused` tells you whether the desktop fought back

`focused` lists the windows the batch had to pull to the foreground, in order, with repeats intact.
Windows that were already focused are absent, so an empty list means nothing had to move.

**The same handle appearing twice is the interesting case.** It means something took the foreground
away mid-batch — a notification, an installer, a game — and windeskctl took it back. Any steps that
ran in between went to the thief, not to your target. That failure is invisible in a screenshot of
the app you meant to drive: the app simply looks like nothing happened, which reads as a broken tool
rather than a stolen foreground. Check `focused` before concluding your steps were wrong.

Steps that name no frame — `text`, and `down`/`up`/`press` with a `key` — go wherever the foreground
is, so they are the ones this protects. The window most recently named by a *targeted* step is
re-asserted before every flush.

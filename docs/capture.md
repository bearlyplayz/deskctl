[← Usage index](Usage.md)

# capture — get pixels

Captures a window or monitor as an image, including when the window is occluded or
hardware-accelerated (games, video, GPU canvases). Returns the image together with an **`img:`
frame and the rect describing its coordinate space** — so what you see on the image is clickable
by its image coordinates, without converting pixels yourself.

```powershell
windeskctl capture --target monitor:1 --out shot.png
windeskctl capture --target win:12345 --region 0,0,400,300 --out crop.png   # sub-rect: x,y,w,h
windeskctl capture --target win:12345 --max-width 1920 --out full.png        # raise the default cap
windeskctl capture --target win:12345 --ocr --out shot.png                   # text + positions
windeskctl capture --target win:12345 --ocr-filter Save --ocr-filter Cancel --out shot.png
windeskctl capture --target win:12345 --format jpeg --quality 75 --out shot.jpg
```

Without `--out`, the CLI prints the frame rect and does **not** print the image — an image whose
frame you never see is the bug this design prevents.

MCP: tool `capture`, arguments `target`, `region`, `maxWidth`, `maxHeight`, `format`, `quality`,
`ocr`, `ocrFilter`. It returns the image as a viewable image block plus the frame info as text.

**Prefer [`snapshot`](snapshot.md) over `capture` when the surface has an element tree** — you get named, clickable
elements instead of pixels to reason about spatially. Reach for `capture` for canvases, video, games,
and remote-desktop sessions that have no tree. For motion across several frames, see
[`record`](record.md). To capture between the steps of an input batch, see the `capture` step in
[`input`](input.md#capture--screenshot-mid-batch).

## Click what you see: `img:` frames

Every capture mints an `img:` frame whose coordinate space **is the image**. A point read off the
image — "the button is at (200, 100)" — is sent back verbatim as `img:<handle>@200,100` in the
[`input`](input.md) tool, and windeskctl applies the capture's recorded origin and scale. Never
convert image pixels to screen pixels yourself; that arithmetic is the tool's job, and doing it by
hand on a downscaled image is the classic way clicks land off-target.

Two caveats:

- **Session-scoped.** The handle lives in the process that minted it. Under MCP that is the whole
  client session; a CLI run is one process per command, so a handle printed there cannot be used
  by a later run — use the printed rect instead.
- **A snapshot in time.** The frame records where the target *was*. If the window moves or its
  content scrolls after the capture, clicks land where the pixels used to be — the same caveat
  `elem:` handles carry. Re-capture after anything moves.

## Downscaling, and the default cap

**Downscaling is the lever that matters.** Image cost tracks pixel dimensions, not bytes, so
`--max-width` / `--max-height` genuinely save an agent's tokens while `--format jpeg` saves nothing —
PNG is lossless and keeps small text readable. Use JPEG only when bytes literally cost something,
like writing to disk.

When neither cap is given, **capture applies `--max-width 1200` by default**: with `img:` frames
doing the scale arithmetic, a downscaled image clicks exactly as accurately as a full-size one, so
there is no reason to pay full price by accident. The default is never silent — the returned
rect's `w`/`h` are the image's actual pixel dimensions and `scale` is the applied factor, whether
the cap was defaulted or explicit. Pass a larger `--max-width` for full resolution. `record` keeps
no default cap.

## OCR

`--ocr` (CLI) / `ocr: true` (MCP) recognizes text in the capture using the OS engine — no
dependency, no network. Each line and word comes back with its bounding rect **in image
coordinates**, directly usable as `img:` points: click a word by the centre of its rect. Word
rects matter when one line spans several targets — a menu bar reads as a single line, and only the
word's own rect clicks the right entry.

Recognition runs on the **full-resolution** pixels regardless of any downscale, so a cheap 1200px
image still gets text read from the original. This is the tool for windows `snapshot` cannot see
into (GPU canvases, games, remote desktops); where a real element tree exists, `elem:` targets
remain more reliable than OCR. Requires a Windows language pack with OCR support, which standard
installs have.

**Filter when you know what you're hunting.** Whole-window OCR of a text-dense surface is hundreds
of lines. `--ocr-filter <text>` (repeatable) / `ocrFilter: [...]` (MCP) returns only the lines
containing any given string, case-insensitively — matched lines come back whole, words included, so
the click rect is still there. A filter implies OCR; no separate `ocr` flag needed. An **empty
result is an answer**: that text is not on screen. Filters must be non-empty text.

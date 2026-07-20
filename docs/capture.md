[← Usage index](Usage.md)

# capture — get pixels

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

**Prefer [`snapshot`](snapshot.md) over `capture` when the surface has an element tree** — you get named, clickable
elements instead of pixels to reason about spatially. Reach for `capture` for canvases, video, games,
and remote-desktop sessions that have no tree. For motion across several frames, see
[`record`](record.md).

**Downscaling is the lever that matters.** Image cost tracks pixel dimensions, not bytes, so
`--max-width` / `--max-height` genuinely save an agent's tokens while `--format jpeg` saves nothing —
PNG is lossless and keeps small text readable. Any downscale records the factor in the returned
rect's `scale` rather than rescaling silently. Use JPEG only when bytes literally cost something,
like writing to disk.

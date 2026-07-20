[← Usage index](Usage.md)

# record — capture motion as a burst of frames

Captures a short burst of frames to disk, one file per frame, so a caller can see motion a single
`capture` cannot show — a spinner turning, a progress bar filling, a video playing, a transition.
It's [`capture`](capture.md) run on a schedule; every option `capture` takes, `record` takes too. It writes ordered
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

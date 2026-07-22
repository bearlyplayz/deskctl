# Capture and record as input steps, image frames, and OCR

Date: 2026-07-22
Status: approved design, pending implementation plan

## Problem

An agent driving the desktop cannot see what happens *during* a batch of input. Today it must
launch `record` as a separate command, then race to send an `input` batch inside the recording
window — a timing dance that fails often. It also cannot screenshot between two steps of the same
batch (e.g. before a click, mid-drag).

Separately, agents reliably fail to click targets they located on a downscaled capture. A capture
taken with `maxWidth` returns a rect with `scale < 1`, and the agent is expected to convert the
image-pixel coordinates it read off the picture back into frame units by hand. LLMs botch that
ratio arithmetic constantly. The codebase already owns the correct conversion
(`FrameRect.Scale`, `Translate.To`) — it just never lets the caller use it for this.

## Decisions

1. **Capture and record become steps in the `input` grammar.** No new "automation" command: the
   input batch already is the orchestration surface (ordering, timing, targets, unwind), and a
   second composing command would duplicate it and drift. Launch and window actions stay outside
   the batch — nothing time-sensitive happens at those boundaries, so the agent calls those
   commands before/after.
2. **Both save to disk only.** No image bytes in any response. The result reports paths and
   coordinate rects.
3. **Every capture mints an `img:` frame** so agents click in image coordinates and windeskctl
   does the scale math.
4. **OCR is an opt-in flag on capture only** (standalone command and step), backed by the
   in-box `Windows.Media.Ocr` engine. No new dependency. Not on record — 30 OCR passes per burst
   has no use case yet.
5. **No separate OCR region.** Capture's existing `Region` already scopes both the image and the
   OCR that runs on it. One knob.

## Step grammar (`Core/Input/Steps.cs`)

Two new members of the closed `Step` union:

```
Step.Capture(Frame Target, string Path, CropBox? Region, int? MaxWidth, int? MaxHeight,
             ImageFormat Format, int Quality, bool Ocr = false)
Step.Record (Frame Target, string OutputDir, RecordPreset Preset, bool Background = false,
             CropBox? Region, int? MaxWidth, int? MaxHeight, ImageFormat Format, int Quality)
```

Shapes mirror `CaptureInput` / `RecordInput` plus the output location, the OCR flag, and the
record mode:

- `Capture` writes one image and blocks (a screenshot is effectively instant).
- `Record` with `Background: false` runs the full preset burst before the next step executes —
  for watching the app react to the step before it.
- `Record` with `Background: true` starts the burst and lets the batch continue — the frames
  capture the drag/animation the *following* steps perform. The batch joins all outstanding
  bursts before returning.

Presets stay the only rate/duration control, so a burst remains bounded (max 30 frames) and a
batch can never fill a disk. There is no stop-record step; bursts self-terminate.

## Planner (`Core/Input/BatchPlanner.cs`)

Both steps flush pending sends and become `PlannedOp.Semantic`, exactly like `Invoke` / `Fill` /
`WaitFor`. No new op kinds. Background-record concurrency is the executor's concern; the planner
stays pure and unit-testable.

## Executor (`Platform/Commands/InputCommand.cs`)

- `Capture` semantic op: run the existing capture pipeline, write the file, mint the `img:` frame.
- `Record` semantic op: run the existing burst pipeline inline, or on a `Task` when
  `Background: true`.
- Outstanding background bursts are joined in a `finally`: a batch that throws mid-drag still
  flushes its frames — which are precisely the frames a caller wants when diagnosing the throw.
  The held-set unwind (existing behaviour) is not delayed by the join; input release comes first.

## Result (`Core/Input/Steps.cs`, `InputResult`)

Two new lists:

- `Captured`: `{ path, rect }` per capture step, in batch order. `rect` is the minted `img:`
  frame's `FrameRect` (origin, size in image pixels, scale).
- `Recorded`: `{ rect, files }` per record step — the same shape `RecordResult` has today.

## Image frames (`Core/Frames/Frame.cs`, minting in Platform)

New closed-hierarchy case:

```
Frame.Image(string Handle)   →  "img:<handle>"
```

Every capture — standalone command or step — mints a session-scoped handle (same pattern
`HandleMinter` uses for `elem:`) and registers the capture's `FrameRect` against it, scale
included. The agent reads a coordinate off the picture and clicks `img:<handle>@x,y`;
`Translate.To` converts image pixels → physical pixels → target frame. The agent never does
ratio arithmetic.

Staleness caveat (documented, not solved): the rect is where the window was at capture time. If
the window moves afterwards, the click lands where the pixels used to be — the same caveat
`elem:` handles already carry. Handles are session-scoped and callers must not fabricate them.

## OCR (`Platform`, `Windows.Media.Ocr`)

`ocr: true` on capture adds a `text` list to the capture response/result:

- Runs on the **full-resolution** bitmap, after `Region` cropping but **before** downscaling —
  compression and downscale degrade recognition, and the tool gets to look at the original.
- Rects are reported in the capture's output frame units (i.e. directly usable as
  `img:<handle>@x,y` targets), converted through the same scale the image was written with.
- Shape follows the engine's native structure: lines, each with `text` + `rect` and nested
  `words` (`text` + `rect` each). Line text is compact for reading; word rects give a correct
  click point when a line spans multiple targets ("File Edit View Help").
- OCR is the fallback for windows UIA cannot see into (GPU-canvas UIs). For apps `snapshot` can
  read, `elem:` targets remain the better tool; the usage guide says so.

## Plumbing that bites

- `StepJsonConverter`: parse/serialize the two new step shapes.
- `WinDeskCtlJsonContext`: every new type crossing JSON registered **in the same commit** —
  NativeAOT has no reflection fallback and an unlisted type takes the MCP server down at startup.
- `Frame.TryParse` / `Parse`: new `img:` prefix, error message updated.
- Usage guide: `input.md` documents the new steps; `capture.md` documents `ocr` and `img:`
  frames; both explain the staleness caveat.
- CLI and MCP adapters parse/forward only; no logic in adapters.

## Testing

Core is pure and fully testable: planner placement of the new semantic ops, step JSON round-trip,
`Frame.TryParse` for `img:`, and `Translate` through a downscaled image rect (already covered,
extended with an `img:` case). Platform (WIC, WinRT OCR, burst threading) stays untested by
design; `doctor` is its runtime check.

## Out of scope

- Launch / window-action steps in the batch.
- Free-running or stoppable recording.
- OCR on record bursts.
- A separate OCR region distinct from the capture region.
- Any image bytes in responses.

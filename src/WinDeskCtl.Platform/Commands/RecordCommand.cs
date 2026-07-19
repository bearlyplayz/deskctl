using System.Diagnostics;
using WinDeskCtl.Core.Capture;
using WinDeskCtl.Core.Commands;
using WinDeskCtl.Core.Frames;

namespace WinDeskCtl.Platform.Commands;

/// <summary>
/// A short burst of captures written to disk, one file per frame — pixels sampled over time so a
/// caller can perceive motion a single frame cannot show. Delegates each frame to
/// <see cref="CaptureCommand"/>; the burst is that command run on a schedule, not a second capture
/// path.
/// </summary>
public sealed class RecordCommand : ICommand<RecordInput, RecordResult>, IDisposable
{
    // One capture instance for the whole burst so its D3D device is created once, not per frame.
    private readonly CaptureCommand _capture = new();

    public async Task<RecordResult> RunAsync(RecordInput input, CancellationToken ct)
    {
        (int fps, _) = input.Preset.Timing();
        int frames = input.Preset.FrameCount();
        double interval = 1.0 / fps;

        Directory.CreateDirectory(input.OutputDir);

        CaptureInput shot = new(
            input.Target, input.Region, input.MaxWidth, input.MaxHeight, input.Format, input.Quality);
        string ext = input.Format == ImageFormat.Jpeg ? "jpg" : "png";
        int pad = (frames - 1).ToString().Length;

        List<string> files = new(frames);

        // Every preset yields at least six frames, so the loop always runs and assigns this; the
        // compiler cannot see that FrameCount is positive.
        FrameRect? rect = null;

        // Each frame is scheduled against a start timestamp rather than by sleeping a fixed
        // interval after each capture, so the time a capture and encode take does not accumulate
        // into ever-widening gaps. If a capture overruns its slot the wait is skipped and frames
        // come as fast as the hardware allows — best-effort pacing, not a hard clock.
        // ponytail: per-frame WGC session setup caps the real rate near ~10fps; if Instant (12fps)
        // proves unreachable, hold one capture session open across the burst.
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < frames; i++)
        {
            double wait = (i * interval) - Stopwatch.GetElapsedTime(start).TotalSeconds;
            if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait), ct);

            CaptureResult frame = await _capture.RunAsync(shot, ct);
            rect = frame.Rect;

            string path = Path.Combine(input.OutputDir, $"frame_{i.ToString().PadLeft(pad, '0')}.{ext}");
            await File.WriteAllBytesAsync(path, frame.Bytes, ct);
            files.Add(path);
        }

        return new RecordResult(rect!, input.Format, files);
    }

    public void Dispose() => _capture.Dispose();
}

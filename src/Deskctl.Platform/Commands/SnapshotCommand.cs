using System.Runtime.InteropServices;
using Deskctl.Core.Commands;
using Deskctl.Core.Frames;
using Deskctl.Core.Uia;
using Deskctl.Platform.Displays;
using Deskctl.Platform.Uia;
using Deskctl.Platform.Windows;

namespace Deskctl.Platform.Commands;

/// <summary>
/// The default way to perceive the screen. The element tree, not pixels: a full-desktop
/// capture on a wide display is megabytes a vision model downscales into uselessness, while the
/// tree is a few dozen lines naming exactly what can be acted on.
/// </summary>
public sealed class SnapshotCommand : ICommand<SnapshotInput, SnapshotResult>
{
    public async Task<SnapshotResult> RunAsync(SnapshotInput input, CancellationToken ct)
    {
        DisplayEnumerator.EnsurePerMonitorV2();

        if (input.Vision)
        {
            // Answered rather than silently served a tree: the element tree is the default and
            // pixels are the opt-in, so a caller asking snapshot for pixels has the
            // right intent and the wrong command. Redirected instead of aliased because capture
            // returns an image and a frame rect, which is not this command's result shape.
            throw new NotSupportedException(
                $"snapshot returns an element tree, not pixels. Run: deskctl capture --target {input.Target}");
        }

        Frame target = Frame.Parse(input.Target);

        (nint rootAbi, FrameRect rect) = await ResolveTargetAsync(target, ct);

        List<WalkedElement> flat = [];
        try
        {
            ElementNode? root;
            bool truncated;
            (root, flat, truncated) = await UiaSession.CallAsync(
                () => TreeWalker.Walk(rootAbi, input.MaxDepth, input.InteractiveOnly), ct);

            // Register takes its own references; this method owns the ones the walk produced.
            HandleRegistry.Register(flat, rootAbi, input.MaxDepth);

            return new SnapshotResult(rect, root, flat.Count, truncated);
        }
        finally
        {
            // The walk holds a reference per kept element, and the root is held separately by
            // ResolveTargetAsync. Both are ours to release; the registry has its own.
            foreach (WalkedElement e in flat)
            {
                if (e.Abi != rootAbi) Marshal.Release(e.Abi);
            }
            Marshal.Release(rootAbi);
        }
    }

    private static async Task<(nint Abi, FrameRect Rect)> ResolveTargetAsync(Frame target, CancellationToken ct)
    {
        switch (target)
        {
            case Frame.Window w:
            {
                nint abi = await UiaSession.CallAsync(() =>
                {
                    nint element;
                    try
                    {
                        UiaSession.Current.Automation.ElementFromHandle((nint)w.Hwnd, out element);
                    }
                    catch (COMException ex)
                    {
                        // A window that closed between being listed and being snapshotted throws
                        // rather than returning null, and a raw COM stack trace tells the caller
                        // nothing they can act on.
                        throw new InvalidOperationException(
                            $"No UI Automation element for window {w.Hwnd}. The window has most likely " +
                            "closed — re-run the windows tool to get a current handle.", ex);
                    }

                    if (element == 0)
                    {
                        throw new InvalidOperationException(
                            $"No UI Automation element for window {w.Hwnd}. The window may have closed.");
                    }

                    return element;
                }, ct);

                return (abi, WindowGeometry.GetRect((nint)w.Hwnd));
            }

            case Frame.Element e:
            {
                (nint abi, Resolution _) = HandleRegistry.Resolve(e.Handle);
                return (abi, await UiaSession.CallAsync(() => ElementRect.Of(abi, target), ct));
            }

            case Frame.Virtual:
                // Deliberate. The desktop root's tree is every window in the session, which is
                // exactly the unusable wall of elements the semantic tier exists to avoid.
                // Callers list windows and snapshot the one they mean.
                throw new NotSupportedException(
                    "Snapshotting the whole desktop would return every element in every window. " +
                    "Use the windows tool to find a window, then snapshot 'win:<hwnd>'.");

            case Frame.Monitor:
                throw new NotSupportedException(
                    "A monitor is a region of pixels, not a UI tree. Snapshot a window, or use capture.");

            default:
                throw new ArgumentOutOfRangeException(nameof(target), $"Unhandled frame '{target}'.");
        }
    }
}

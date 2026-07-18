using System.ComponentModel;
using System.Text.Json;
using Deskctl.Core.Capture;
using Deskctl.Core.Commands;
using Deskctl.Core.Frames;
using Deskctl.Core.Input;
using Deskctl.Core.Json;
using Deskctl.Core.Uia;
using Deskctl.Core.Windows;
using Deskctl.Platform.Commands;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Deskctl.Mcp;

/// <summary>
/// The MCP surface. Like the CLI, these are adapters: each method delegates to the same
/// ICommand and holds no logic.
/// </summary>
/// <remarks>
/// Not a static class: <c>WithTools&lt;T&gt;</c> takes the tool type as a type argument, which C#
/// forbids for static types. The tools themselves are static, so this is never instantiated.
/// </remarks>
[McpServerToolType]
internal sealed class DeskctlTools
{
    private DeskctlTools() { }

    /// <summary>
    /// Runs a command, reporting a caller's mistake as a message the client can read.
    /// </summary>
    /// <remarks>
    /// The SDK renders an escaped exception as "An error occurred invoking '&lt;tool&gt;'" and logs
    /// the detail to stderr, which the client never sees — sound by default, since an arbitrary
    /// exception may carry internals. McpException is the SDK's opt-in for the case where the
    /// message *is* the contract: a refusal names the frame that was wrong or the modifiers a
    /// failed batch auto-released, and a caller told only that "an error occurred" learns nothing
    /// and re-sends the same broken script. The classifier is shared with the
    /// CLI, so a refusal reads the same on both surfaces.
    /// </remarks>
    private static async Task<T> ReportingCallerErrors<T>(Func<Task<T>> run)
    {
        try
        {
            return await run().ConfigureAwait(false);
        }
        catch (Exception ex) when (CallerError.Is(ex))
        {
            throw new McpException(ex.Message, ex);
        }
    }

    [McpServerTool(Name = "doctor")]
    [Description(
        "Self-test deskctl against the live machine. Reports virtual-desktop bounds, every " +
        "monitor's origin/size/DPI, and the result of each check. Run this first when " +
        "coordinates behave unexpectedly.")]
    public static async Task<DoctorReport> DoctorAsync(
        [Description("Include checks that disturb the desktop and may steal focus.")]
        bool includeIntrusive = false,
        CancellationToken ct = default)
        => await ReportingCallerErrors(() => new DoctorCommand().RunAsync(new DoctorInput(includeIntrusive), ct));

    [McpServerTool(Name = "capture")]
    [Description(
        "Capture pixels of a window or monitor, including when it is occluded or hardware-" +
        "accelerated. Returns the image together with the frame rect describing its coordinate " +
        "space — feed that rect back when clicking so the click lands where you saw it. Use this " +
        "for canvases, video, and games that have no element tree.")]
    public static async Task<CallToolResult> CaptureAsync(
        [Description("What to capture: 'monitor:<id>' or 'win:<hwnd>'. Get monitor ids from the doctor tool.")]
        string target,
        [Description("Optional sub-rectangle within the target, as x,y,w,h.")]
        string? region = null,
        [Description("Downscale so width does not exceed this. The result's scale records the factor.")]
        int? maxWidth = null,
        [Description("Downscale so height does not exceed this. The result's scale records the factor.")]
        int? maxHeight = null,
        [Description(
            "'png' (default, lossless — what you want; cost tracks pixels not bytes) or 'jpeg'. " +
            "Downscale with maxWidth to spend less; use jpeg only when bytes genuinely cost.")]
        string format = "png",
        [Description("JPEG quality, 1-100. Ignored for PNG.")]
        int quality = 90,
        CancellationToken ct = default)
        => await ReportingCallerErrors(async () =>
        {
            using CaptureCommand command = new();
            CaptureResult result = await command.RunAsync(
                new CaptureInput(
                    Frame.Parse(target),
                    string.IsNullOrEmpty(region) ? null : CropBox.Parse(region),
                    maxWidth,
                    maxHeight,
                    ParseFormat(format),
                    quality),
                ct);

            string rect = DeskctlJson.Serialize(result.Rect);

            // An image block rather than the result object: serializing CaptureResult would send
            // the pixels as a base64 string inside JSON text, which a vision model cannot see —
            // and an unviewable image is the whole point of the tool missed. The rect rides
            // alongside as its own text block, because an image without its coordinate frame is
            // the failure this design exists to prevent.
            return new CallToolResult
            {
                Content =
                [
                    ImageContentBlock.FromBytes(result.Bytes, result.MimeType),
                    new TextContentBlock { Text = rect },
                ],
                StructuredContent = JsonDocument.Parse(rect).RootElement,
            };
        });

    [McpServerTool(Name = "record")]
    [Description(
        "Capture a burst of frames to disk to SEE motion a single capture cannot — animating, " +
        "loading, frozen, or done. Writes ordered files (frame_000.png…) to outputDir and returns " +
        "their filenames, not the images; read them with your image tools (first/middle/last is " +
        "usually enough). Crop to the moving area with 'region'. The preset picks rate/duration " +
        "(≤30 frames); identical frames mean static or mis-sampled — retry another preset.")]
    public static async Task<CallToolResult> RecordAsync(
        [Description("What to capture: 'monitor:<id>' or 'win:<hwnd>'. Get monitor ids from the doctor tool.")]
        string target,
        [Description("Directory to write the frames into. Created if it does not exist.")]
        string outputDir,
        [Description(
            "Rate/duration to match the motion: slow (3fps/10s, loading), medium (6fps/5s, " +
            "general), fast (9fps/1s, spinners — default), instant (12fps/0.5s, very fast).")]
        RecordPreset preset = RecordPreset.Fast,
        [Description("Optional sub-rectangle within the target, as x,y,w,h. Crop to the animated area.")]
        string? region = null,
        [Description("Downscale each frame so width does not exceed this.")]
        int? maxWidth = null,
        [Description("Downscale each frame so height does not exceed this.")]
        int? maxHeight = null,
        [Description("'png' (default) or 'jpeg'. PNG is lossless; downscale with maxWidth to spend less.")]
        string format = "png",
        [Description("JPEG quality, 1-100. Ignored for PNG.")]
        int quality = 90,
        CancellationToken ct = default)
        => await ReportingCallerErrors(async () =>
        {
            using RecordCommand command = new();
            RecordResult result = await command.RunAsync(
                new RecordInput(
                    Frame.Parse(target),
                    outputDir,
                    preset,
                    string.IsNullOrEmpty(region) ? null : CropBox.Parse(region),
                    maxWidth,
                    maxHeight,
                    ParseFormat(format),
                    quality),
                ct);

            // Report frames as bare filenames against the caller's own outputDir, not absolute
            // paths: the shared directory prefix repeated once per frame is the bulk of this
            // result's size, and the caller already passed outputDir in. Filenames stay in
            // capture order, matching the frame_NNN names on disk.
            IReadOnlyList<string> names = result.Files.Select(Path.GetFileName).ToList()!;
            string structured = DeskctlJson.Serialize(result with { Files = names });

            // Guidance as text, not the images: the frames live on disk so the caller pulls only
            // the ones it needs into context. A caller told nothing about ordering would read
            // frames blind, so the reading strategy rides along with the manifest.
            string guidance =
                $"Recorded {names.Count} frames to {outputDir} in capture order, " +
                $"from {names[0]} to {names[^1]}. " +
                "Read the FIRST, a MIDDLE, and the LAST frame to see the motion; open more only if " +
                "you need finer detail. If they look identical, the region is static or was sampled " +
                "at its own cycle — re-run with a different preset.";

            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = guidance }],
                StructuredContent = JsonDocument.Parse(structured).RootElement,
            };
        });

    /// <summary>
    /// Rejects an unrecognized format rather than defaulting to PNG, so a caller that asks for
    /// something this does not encode is told, not quietly handed a different format.
    /// </summary>
    private static ImageFormat ParseFormat(string? value) => value?.ToLowerInvariant() switch
    {
        "png" or null => ImageFormat.Png,
        "jpeg" => ImageFormat.Jpeg,
        _ => throw new ArgumentException($"Unknown format '{value}'. Use 'png' or 'jpeg'."),
    };

    [McpServerTool(Name = "windows")]
    [Description(
        "List windows with their DWM-accurate geometry — the rect as drawn, not the oversized one " +
        "Win32 reports. Use the returned hwnd as 'win:<hwnd>' with the capture, input, and " +
        "snapshot tools. Filter rather than listing everything when you know what you want.")]
    public static async Task<WindowListResult> WindowsAsync(
        [Description("Only windows whose title contains this (case-insensitive).")]
        string? titleContains = null,
        [Description("Only windows belonging to this process, e.g. 'chrome'.")]
        string? processName = null,
        [Description("Include minimized windows.")]
        bool includeMinimized = true,
        CancellationToken ct = default)
        => await ReportingCallerErrors(() => new WindowListCommand().RunAsync(
            new WindowListInput(titleContains, processName, includeMinimized), ct));

    [McpServerTool(Name = "window_action")]
    [Description(
        "Focus, move, resize, minimize, maximize, or restore a window. Coordinates address the " +
        "VISIBLE edges — deskctl converts to the raw rect Win32 wants, so what you ask for is " +
        "what you see. Returns the window's actual state afterwards, which may differ from the " +
        "request because Windows clamps and snaps.")]
    public static async Task<WindowActionResult> WindowActionAsync(
        [Description("The window handle from the windows tool.")] long hwnd,
        [Description("focus | move | resize | minimize | maximize | restore")] WindowAction action,
        [Description("Left edge of the visible window. Move/resize only.")] int? x = null,
        [Description("Top edge of the visible window. Move/resize only.")] int? y = null,
        [Description("Visible width. Move/resize only.")] int? width = null,
        [Description("Visible height. Move/resize only.")] int? height = null,
        CancellationToken ct = default)
        => await ReportingCallerErrors(() => new WindowActionCommand().RunAsync(
            new WindowActionInput(hwnd, action, x, y, width, height), ct));

    [McpServerTool(Name = "snapshot")]
    [Description("""
        Read a window's UI as a tree of named, typed elements. This is the DEFAULT way to see
        what is on screen — prefer it over capture, which returns pixels you then have to
        reason about spatially.

        Each element gets a handle. Use it as "elem:<handle>" with the input tool to act on the
        element directly:
          {"invoke":{"target":"elem:btn-save"}}          press it — no coordinates, cannot miss
          {"fill":{"target":"elem:txt-search","value":"hello"}}

        Handles are opaque and minted here. Do not construct or guess them, and re-run snapshot
        after the UI changes.

        Use capture instead when a surface has no element tree: canvases, video, games, and
        remote-desktop sessions.
        """)]
    public static async Task<SnapshotResult> SnapshotAsync(
        [Description("'win:<hwnd>' from the windows tool, or 'elem:<handle>' to scope to a subtree.")]
        string target,
        [Description("How deep to walk. Lower this if the tree is truncated and you only need the top.")]
        int maxDepth = 12,
        [Description("Include non-interactive elements. Off by default — a raw tree is mostly layout scaffolding.")]
        bool all = false,
        CancellationToken ct = default)
        => await ReportingCallerErrors(() => new SnapshotCommand().RunAsync(
            new SnapshotInput(target, maxDepth, Vision: false, InteractiveOnly: !all), ct));

    [McpServerTool(Name = "input")]
    [Description("""
        Send mouse/keyboard as one atomic JSON-array batch; zero-delay steps coalesce into a single
        OS call, which is what makes real drags and cross-device combos work. Each step is one verb:
          {"down":{"key":"ctrl"}} / {"up":{...}} / {"press":{"button":"left","to":"elem:row-1"}}
          {"move":{"to":"win:123@400,200","over":"250ms","ease":"easeOut"}}
          {"scroll":{"dy":-3,"at":"elem:list"}}  {"text":"hi"}  {"invoke":{"target":"elem:x"}}
          {"fill":{"target":"elem:x","value":"y"}}  {"waitFor":{...}}  {"delay":{...}}
        down/up/press need "key" OR "button" (tag required — 'left'/'right' are both). A point is
        "<frame>@<x>,<y>", or "<frame>" for its centre. A drag's move must have a timed "over" or the
        app sees a click. Anything left held auto-releases newest-first and is reported in 'released'
        — a dangling "win" opens the Start menu, so emit your own 'up'.
        """)]
    public static async Task<InputResult> InputAsync(
        [Description("The JSON array of steps.")] JsonElement steps,
        CancellationToken ct = default)
        => await ReportingCallerErrors(() =>
        {
            // A client may send the array as-is or as a JSON string holding the array text;
            // an untyped tool-schema parameter leaves the choice to the client. Unwrap the
            // string form so both reach the parser as array text.
            string raw = steps.ValueKind == JsonValueKind.String ? steps.GetString()! : steps.GetRawText();

            // Re-serialized rather than JsonElement.Deserialize'd: that overload needs a
            // JsonTypeInfo under NativeAOT, and the source-generated context is reachable only
            // by type.
            List<Step>? parsed = DeskctlJson.Deserialize<List<Step>>(raw);
            if (parsed is null || parsed.Count == 0)
            {
                throw new ArgumentException("No steps to run.", nameof(steps));
            }

            return new InputCommand().RunAsync(new InputRequest(parsed), ct);
        });
}

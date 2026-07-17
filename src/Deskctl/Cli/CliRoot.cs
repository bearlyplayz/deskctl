using System.CommandLine;
using System.Text.Json;
using Deskctl.Core.Capture;
using Deskctl.Core.Commands;
using Deskctl.Core.Frames;
using Deskctl.Core.Input;
using Deskctl.Core.Json;
using Deskctl.Core.Uia;
using Deskctl.Core.Windows;
using Deskctl.Platform.Commands;

namespace Deskctl.Cli;

/// <summary>
/// The CLI surface. Each subcommand parses flags and delegates to the same ICommand the MCP
/// server calls — the adapter holds no logic of its own, which is what keeps the surfaces from
/// drifting.
/// </summary>
internal static class CliRoot
{
    /// <summary>
    /// Rejects an unrecognized format rather than defaulting to PNG. Silently substituting would
    /// hand back a different format than the one asked for — and "--format JPEG" quietly
    /// producing a PNG is a bug report, not a preference.
    /// </summary>
    private static ImageFormat ParseFormat(string? value) => value?.ToLowerInvariant() switch
    {
        "png" or null => ImageFormat.Png,
        "jpeg" => ImageFormat.Jpeg,
        _ => throw new ArgumentException($"Unknown format '{value}'. Use 'png' or 'jpeg'."),
    };

    internal static async Task<int> InvokeAsync(string[] args)
    {
        RootCommand root = new("deskctl — desktop capture, input, and UI automation");
        root.Subcommands.Add(BuildDoctor());
        root.Subcommands.Add(BuildCapture());
        root.Subcommands.Add(BuildWindows());
        root.Subcommands.Add(BuildInput());
        root.Subcommands.Add(BuildSnapshot());
        root.Subcommands.Add(BuildMcp());

        // System.CommandLine's default handler prints "Unhandled exception:" plus a full stack
        // trace and swallows the exception, so a deliberate refusal reads like a crash and the one
        // line written for the caller is buried. Disabling it lets deskctl classify its own errors.
        InvocationConfiguration config = new() { EnableDefaultExceptionHandler = false };

        try
        {
            return await root.Parse(args).InvokeAsync(config);
        }
        catch (Exception ex) when (CallerError.Is(ex))
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static Command BuildDoctor()
    {
        Option<bool> jsonOption = new("--json")
        {
            Description = "Emit the raw report as JSON instead of a human-readable summary",
        };
        Option<bool> intrusiveOption = new("--intrusive")
        {
            Description = "Include checks that disturb the desktop (may steal focus)",
        };

        Command doctor = new("doctor", "Self-test against the live machine") { jsonOption, intrusiveOption };

        doctor.SetAction(async (parseResult, ct) =>
        {
            DoctorInput input = new(IncludeIntrusive: parseResult.GetValue(intrusiveOption));
            DoctorReport report = await new DoctorCommand().RunAsync(input, ct);

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(DeskctlJson.Serialize(report));
            }
            else
            {
                WriteSummary(report);
            }

            return report.Ok ? 0 : 1;
        });

        return doctor;
    }

    private static Command BuildCapture()
    {
        Option<string> targetOption = new("--target")
        {
            Description = "What to capture: monitor:<id> or win:<hwnd>",
            Required = true,
        };
        Option<string?> regionOption = new("--region")
        {
            Description = "Sub-rectangle within the target, as x,y,w,h",
        };
        Option<int?> maxWidthOption = new("--max-width")
        {
            Description = "Downscale so width does not exceed this. Sets scale in the result.",
        };
        Option<int?> maxHeightOption = new("--max-height")
        {
            Description = "Downscale so height does not exceed this. Sets scale in the result.",
        };
        Option<string> formatOption = new("--format")
        {
            Description = "png (default) or jpeg. PNG is lossless on UI content.",
            DefaultValueFactory = _ => "png",
        };
        Option<int> qualityOption = new("--quality")
        {
            Description = "JPEG quality 1-100. Ignored for PNG.",
            DefaultValueFactory = _ => 90,
        };
        Option<FileInfo?> outOption = new("--out")
        {
            Description = "Write the image here. Without it, the frame rect is printed and the image is not.",
        };

        Command capture = new("capture", "Capture pixels of a window or monitor")
        {
            targetOption, regionOption, maxWidthOption, maxHeightOption, formatOption, qualityOption, outOption,
        };

        capture.SetAction(async (parseResult, ct) =>
        {
            string? region = parseResult.GetValue(regionOption);

            CaptureInput input = new(
                Target: Frame.Parse(parseResult.GetValue(targetOption)!),
                Region: string.IsNullOrEmpty(region) ? null : CropBox.Parse(region),
                MaxWidth: parseResult.GetValue(maxWidthOption),
                MaxHeight: parseResult.GetValue(maxHeightOption),
                Format: ParseFormat(parseResult.GetValue(formatOption)),
                Quality: parseResult.GetValue(qualityOption));

            using CaptureCommand command = new();
            CaptureResult result = await command.RunAsync(input, ct);

            FileInfo? outFile = parseResult.GetValue(outOption);
            if (outFile is not null)
            {
                await File.WriteAllBytesAsync(outFile.FullName, result.Bytes, ct);
            }

            // The rect always goes to stdout, with or without --out: an image whose frame the
            // caller never sees is the bug this design exists to prevent.
            Console.WriteLine(
                $"frame {result.Rect.Frame}  origin {result.Rect.OriginX},{result.Rect.OriginY}  " +
                $"size {result.Rect.W}x{result.Rect.H}  scale {result.Rect.Scale}  " +
                $"{result.Format.ToString().ToLowerInvariant()} {result.Bytes.Length} bytes");

            return 0;
        });

        return capture;
    }

    private static Command BuildWindows()
    {
        Command windows = new("windows", "List and manipulate windows");
        windows.Subcommands.Add(BuildWindowsList());
        windows.Subcommands.Add(BuildWindowsAction("focus", WindowAction.Focus, geometry: false));
        windows.Subcommands.Add(BuildWindowsAction("move", WindowAction.Move, geometry: true));
        windows.Subcommands.Add(BuildWindowsAction("resize", WindowAction.Resize, geometry: true));
        windows.Subcommands.Add(BuildWindowsAction("minimize", WindowAction.Minimize, geometry: false));
        windows.Subcommands.Add(BuildWindowsAction("maximize", WindowAction.Maximize, geometry: false));
        windows.Subcommands.Add(BuildWindowsAction("restore", WindowAction.Restore, geometry: false));
        return windows;
    }

    private static Command BuildWindowsList()
    {
        Option<string?> titleOption = new("--title") { Description = "Only windows whose title contains this" };
        Option<string?> processOption = new("--process") { Description = "Only windows from this process" };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON instead of a table" };

        Command list = new("list", "List windows") { titleOption, processOption, jsonOption };

        list.SetAction(async (parseResult, ct) =>
        {
            WindowListResult result = await new WindowListCommand().RunAsync(
                new WindowListInput(parseResult.GetValue(titleOption), parseResult.GetValue(processOption)),
                ct);

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(DeskctlJson.Serialize(result));
                return 0;
            }

            foreach (WindowInfo w in result.Windows)
            {
                string marker = w.IsForeground ? "*" : " ";
                Console.WriteLine(
                    $"{marker} win:{w.Hwnd,-8}  {w.Rect.OriginX,6},{w.Rect.OriginY,-6} {w.Rect.W,5}x{w.Rect.H,-5} " +
                    $"{w.State,-9} {w.ProcessName,-20} {w.Title}");
            }
            return 0;
        });

        return list;
    }

    private static Command BuildWindowsAction(string name, WindowAction action, bool geometry)
    {
        Argument<long> hwndArgument = new("hwnd") { Description = "The window handle, as reported by 'windows list'" };

        Option<int?> xOption = new("--x") { Description = "Left edge of the visible window" };
        Option<int?> yOption = new("--y") { Description = "Top edge of the visible window" };
        Option<int?> wOption = new("--width") { Description = "Visible width" };
        Option<int?> hOption = new("--height") { Description = "Visible height" };

        Command command = new(name, $"{name} a window");
        command.Arguments.Add(hwndArgument);
        if (geometry)
        {
            command.Options.Add(xOption);
            command.Options.Add(yOption);
            command.Options.Add(wOption);
            command.Options.Add(hOption);
        }

        command.SetAction(async (parseResult, ct) =>
        {
            WindowActionInput input = new(
                Hwnd: parseResult.GetValue(hwndArgument),
                Action: action,
                X: geometry ? parseResult.GetValue(xOption) : null,
                Y: geometry ? parseResult.GetValue(yOption) : null,
                W: geometry ? parseResult.GetValue(wOption) : null,
                H: geometry ? parseResult.GetValue(hOption) : null);

            WindowActionResult result = await new WindowActionCommand().RunAsync(input, ct);
            WindowInfo w = result.Window;

            // The rect is re-read, so this reports what actually happened rather than what was
            // asked for — Windows clamps and snaps.
            Console.WriteLine(
                $"win:{w.Hwnd}  {w.Rect.OriginX},{w.Rect.OriginY} {w.Rect.W}x{w.Rect.H}  {w.State}");
            return 0;
        });

        return command;
    }

    /// <summary>
    /// The steps arrive as JSON rather than as flags: the grammar is a JSON structure, and a
    /// second flag-based dialect for it would be two grammars to keep in sync.
    /// </summary>
    private static Command BuildInput()
    {
        Argument<string> stepsArgument = new("steps")
        {
            Description =
                "A JSON array of steps, e.g. '[{\"down\":{\"key\":\"ctrl\"}},{\"press\":{\"key\":\"c\"}}]'. " +
                "Use '-' to read from stdin.",
        };

        // Element handles live only as long as the process that minted them. Under
        // MCP the process is the whole client session, so a snapshot call and a later input call
        // share it. A CLI run is one process per command, so a batch naming 'elem:' handles has
        // to mint them itself or there is nothing for them to refer to.
        Option<string?> snapshotOption = new("--snapshot")
        {
            Description =
                "Snapshot this target first (win:<hwnd>) so 'elem:' handles in the steps resolve. " +
                "Required for invoke/fill/waitFor from the CLI, because handles do not outlive the process.",
        };

        Command input = new("input", "Send a batch of mouse and keyboard steps atomically")
        {
            snapshotOption,
        };
        input.Arguments.Add(stepsArgument);

        input.SetAction(async (parseResult, ct) =>
        {
            string? snapshotTarget = parseResult.GetValue(snapshotOption);
            if (!string.IsNullOrEmpty(snapshotTarget))
            {
                await new SnapshotCommand().RunAsync(new SnapshotInput(snapshotTarget), ct);
            }

            string raw = parseResult.GetValue(stepsArgument)!;
            if (raw == "-") raw = await Console.In.ReadToEndAsync(ct);

            List<Step>? steps = DeskctlJson.Deserialize<List<Step>>(raw);

            if (steps is null || steps.Count == 0)
            {
                Console.Error.WriteLine("No steps to run.");
                return 1;
            }

            InputResult result = await new InputCommand().RunAsync(new InputRequest(steps), ct);

            Console.WriteLine($"sent {result.EventsSent} event(s) in {result.Flushes} call(s)");
            if (result.Released.Count > 0)
            {
                // Never silent. Releasing a dangling 'win' opens the Start menu and steals focus,
                // so a caller who does not know it happened cannot explain what they are seeing
                //.
                Console.WriteLine(
                    $"auto-released (newest first): {string.Join(", ", result.Released)} — " +
                    "your batch left these held; add explicit 'up' steps.");
            }

            if (result.ReResolved.Count > 0)
            {
                Console.WriteLine(
                    $"re-resolved: {string.Join(", ", result.ReResolved)} — these elements had been " +
                    "destroyed and were matched again by selector; re-run snapshot to be sure.");
            }

            return 0;
        });

        return input;
    }

    private static Command BuildSnapshot()
    {
        Argument<string> targetArgument = new("target")
        {
            Description = "win:<hwnd> or elem:<handle>",
        };
        Option<int> depthOption = new("--max-depth")
        {
            Description = "How deep to walk",
            DefaultValueFactory = _ => 12,
        };
        Option<bool> allOption = new("--all")
        {
            Description = "Include non-interactive elements",
        };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON instead of a tree" };
        // The element tree is the default and pixels are the opt-in. The flag exists so
        // that asking snapshot for pixels is answered rather than silently served a tree; it
        // redirects to capture instead of aliasing it, because the two return different shapes.
        Option<bool> visionOption = new("--vision")
        {
            Description = "Ask for pixels instead of a tree. Points you at the capture command.",
        };

        Command snapshot = new("snapshot", "Read a window's UI element tree")
        {
            depthOption, allOption, jsonOption, visionOption,
        };
        snapshot.Arguments.Add(targetArgument);

        snapshot.SetAction(async (parseResult, ct) =>
        {
            SnapshotResult result = await new SnapshotCommand().RunAsync(
                new SnapshotInput(
                    parseResult.GetValue(targetArgument)!,
                    parseResult.GetValue(depthOption),
                    Vision: parseResult.GetValue(visionOption),
                    InteractiveOnly: !parseResult.GetValue(allOption)),
                ct);

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(DeskctlJson.Serialize(result));
                return 0;
            }

            if (result.Root is null)
            {
                Console.WriteLine("no elements");
                return 0;
            }

            Print(result.Root, 0);
            Console.WriteLine();
            Console.WriteLine($"{result.ElementCount} element(s){(result.Truncated ? ", TRUNCATED" : "")}");
            return 0;
        });

        return snapshot;
    }

    private static void Print(ElementNode node, int indent)
    {
        string pad = new(' ', indent * 2);
        string patterns = node.Patterns.Count > 0 ? $"  [{string.Join(",", node.Patterns)}]" : "";
        string state = node.IsEnabled ? "" : "  (disabled)";

        Console.WriteLine(
            $"{pad}{node.ControlType} '{node.Name}'  elem:{node.Handle}  " +
            $"{node.Rect.OriginX},{node.Rect.OriginY} {node.Rect.W}x{node.Rect.H}{patterns}{state}");

        foreach (ElementNode child in node.Children) Print(child, indent + 1);
    }

    private static Command BuildMcp()
    {
        Command mcp = new("mcp", "Run as an MCP server over stdio");
        mcp.SetAction(async (_, ct) => await Mcp.McpHost.RunAsync(ct));
        return mcp;
    }

    private static void WriteSummary(DoctorReport r)
    {
        Console.WriteLine(
            $"virtual desktop  origin {r.VirtualBounds.OriginX},{r.VirtualBounds.OriginY}  " +
            $"size {r.VirtualBounds.W}x{r.VirtualBounds.H}");
        Console.WriteLine();

        foreach (MonitorInfo m in r.Monitors)
        {
            string primary = m.IsPrimary ? "  [primary]" : "";
            Console.WriteLine(
                $"  monitor:{m.Id}  origin {m.Bounds.OriginX},{m.Bounds.OriginY}  " +
                $"size {m.Bounds.W}x{m.Bounds.H}  dpi {m.Dpi}{primary}");
        }
        Console.WriteLine();

        foreach (DoctorCheck c in r.Checks)
        {
            string mark = c.Status switch
            {
                DoctorStatus.Pass => "PASS",
                DoctorStatus.Fail => "FAIL",
                _ => "SKIP",
            };
            Console.WriteLine($"  [{mark}] {c.Name}  {c.Detail}");
        }

        Console.WriteLine();
        Console.WriteLine(r.Ok ? "ok" : "FAILED");
    }
}

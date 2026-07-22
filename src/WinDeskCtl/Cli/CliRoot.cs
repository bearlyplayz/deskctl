using System.CommandLine;
using System.Text.Json;
using WinDeskCtl.Core.Capture;
using WinDeskCtl.Core.Commands;
using WinDeskCtl.Core.Frames;
using WinDeskCtl.Core.Input;
using WinDeskCtl.Core.Json;
using WinDeskCtl.Core.Launch;
using WinDeskCtl.Core.Uia;
using WinDeskCtl.Core.Windows;
using WinDeskCtl.Platform.Commands;

namespace WinDeskCtl.Cli;

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

    /// <summary>
    /// Rejects an unrecognized preset rather than defaulting, so a typo is reported instead of
    /// quietly recording a different rate and duration than the one asked for.
    /// </summary>
    private static RecordPreset ParsePreset(string? value) => value?.ToLowerInvariant() switch
    {
        "slow" => RecordPreset.Slow,
        "medium" => RecordPreset.Medium,
        "fast" or null => RecordPreset.Fast,
        "instant" => RecordPreset.Instant,
        _ => throw new ArgumentException($"Unknown preset '{value}'. Use slow, medium, fast, or instant."),
    };

    internal static async Task<int> InvokeAsync(string[] args)
    {
        RootCommand root = new("windeskctl — desktop capture, input, and UI automation");
        root.Subcommands.Add(BuildDoctor());
        root.Subcommands.Add(BuildCapture());
        root.Subcommands.Add(BuildRecord());
        root.Subcommands.Add(BuildWindows());
        root.Subcommands.Add(BuildLaunch());
        root.Subcommands.Add(BuildInput());
        root.Subcommands.Add(BuildSnapshot());
        root.Subcommands.Add(BuildMcp());

        // System.CommandLine's default handler prints "Unhandled exception:" plus a full stack
        // trace and swallows the exception, so a deliberate refusal reads like a crash and the one
        // line written for the caller is buried. Disabling it lets windeskctl classify its own errors.
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
                Console.WriteLine(WinDeskCtlJson.Serialize(report));
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
            Description =
                "Downscale so width does not exceed this. Defaults to 1200 when neither cap is " +
                "set; pass a larger value for full resolution. Sets scale in the result.",
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
        Option<bool> ocrOption = new("--ocr")
        {
            Description =
                "Recognize text in the capture and print each line and word with its rect, in " +
                "image coordinates. Runs on the full-resolution pixels regardless of downscale.",
        };
        Option<string[]> ocrFilterOption = new("--ocr-filter")
        {
            Description =
                "Print only OCR lines containing this (case-insensitive). Repeatable; a line " +
                "matching any filter is kept. Implies --ocr.",
            Arity = ArgumentArity.ZeroOrMore,
        };

        Command capture = new("capture", "Capture pixels of a window or monitor")
        {
            targetOption, regionOption, maxWidthOption, maxHeightOption,
            formatOption, qualityOption, outOption, ocrOption, ocrFilterOption,
        };

        capture.SetAction(async (parseResult, ct) =>
        {
            string? region = parseResult.GetValue(regionOption);
            int? maxHeight = parseResult.GetValue(maxHeightOption);

            CaptureInput input = new(
                Target: Frame.Parse(parseResult.GetValue(targetOption)!),
                Region: string.IsNullOrEmpty(region) ? null : CropBox.Parse(region),
                MaxWidth: CaptureDefaults.Apply(parseResult.GetValue(maxWidthOption), maxHeight),
                MaxHeight: maxHeight,
                Format: ParseFormat(parseResult.GetValue(formatOption)),
                Quality: parseResult.GetValue(qualityOption),
                Ocr: parseResult.GetValue(ocrOption),
                OcrFilter: parseResult.GetValue(ocrFilterOption) is { Length: > 0 } filters ? filters : null);

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
                $"{result.Format.ToString().ToLowerInvariant()} {result.Bytes.Length} bytes  " +
                $"{result.Image}");

            foreach (OcrLine line in result.Text ?? [])
            {
                Console.WriteLine($"  {line.Rect.X},{line.Rect.Y} {line.Rect.W}x{line.Rect.H}  {line.Text}");
            }

            return 0;
        });

        return capture;
    }

    private static Command BuildRecord()
    {
        Option<string> targetOption = new("--target")
        {
            Description = "What to capture: monitor:<id> or win:<hwnd>",
            Required = true,
        };
        Option<string> outDirOption = new("--out-dir")
        {
            Description = "Directory to write the frames into (created if absent)",
            Required = true,
        };
        Option<string> presetOption = new("--preset")
        {
            Description = "slow (3fps/10s), medium (6fps/5s), fast (9fps/1s), or instant (12fps/0.5s)",
            DefaultValueFactory = _ => "fast",
        };
        Option<string?> regionOption = new("--region")
        {
            Description = "Sub-rectangle within the target, as x,y,w,h. Crop to the animated area.",
        };
        Option<int?> maxWidthOption = new("--max-width")
        {
            Description = "Downscale each frame so width does not exceed this.",
        };
        Option<int?> maxHeightOption = new("--max-height")
        {
            Description = "Downscale each frame so height does not exceed this.",
        };
        Option<string> formatOption = new("--format")
        {
            Description = "png (default) or jpeg.",
            DefaultValueFactory = _ => "png",
        };
        Option<int> qualityOption = new("--quality")
        {
            Description = "JPEG quality 1-100. Ignored for PNG.",
            DefaultValueFactory = _ => 90,
        };

        Command record = new("record", "Capture a short burst of frames to disk to see motion")
        {
            targetOption, outDirOption, presetOption, regionOption,
            maxWidthOption, maxHeightOption, formatOption, qualityOption,
        };

        record.SetAction(async (parseResult, ct) =>
        {
            string? region = parseResult.GetValue(regionOption);

            RecordInput input = new(
                Target: Frame.Parse(parseResult.GetValue(targetOption)!),
                OutputDir: parseResult.GetValue(outDirOption)!,
                Preset: ParsePreset(parseResult.GetValue(presetOption)),
                Region: string.IsNullOrEmpty(region) ? null : CropBox.Parse(region),
                MaxWidth: parseResult.GetValue(maxWidthOption),
                MaxHeight: parseResult.GetValue(maxHeightOption),
                Format: ParseFormat(parseResult.GetValue(formatOption)),
                Quality: parseResult.GetValue(qualityOption));

            using RecordCommand command = new();
            RecordResult result = await command.RunAsync(input, ct);

            Console.WriteLine(
                $"frame {result.Rect.Frame}  origin {result.Rect.OriginX},{result.Rect.OriginY}  " +
                $"size {result.Rect.W}x{result.Rect.H}  scale {result.Rect.Scale}  " +
                $"{result.Files.Count} frames  {result.Image}");
            foreach (string path in result.Files)
            {
                Console.WriteLine(path);
            }

            return 0;
        });

        return record;
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
                Console.WriteLine(WinDeskCtlJson.Serialize(result));
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

    private static Command BuildLaunch()
    {
        Argument<string> programArgument = new("program")
        {
            Description = "The executable to run. A full path, or a bare name found on PATH.",
        };

        // Everything after '--' belongs to the launched program. Passing them through a repeatable
        // option instead would make an argument that looks like a flag ambiguous between the two
        // programs, and the launched one has the better claim to it.
        Argument<string[]> argsArgument = new("args")
        {
            Description = "Arguments for the program, after a '--' separator.",
            Arity = ArgumentArity.ZeroOrMore,
        };

        Option<string[]> envOption = new("--env")
        {
            Description = "NAME=VALUE to add to the program's environment. Repeatable.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        Option<string?> cwdOption = new("--cwd")
        {
            Description = "Working directory. Defaults to the executable's own directory.",
        };
        Option<string?> logOption = new("--log")
        {
            Description = "Where to write the program's stdout and stderr. Defaults under %TEMP%\\windeskctl.",
        };
        Option<bool> appendOption = new("--append")
        {
            Description = "Append to the log instead of truncating it.",
        };
        Option<int> waitOption = new("--wait-ms")
        {
            Description = "How long to wait for the program's window. 0 to not wait at all.",
            DefaultValueFactory = _ => 60_000,
        };
        Option<int> settleOption = new("--settle-ms")
        {
            Description = "How long to keep watching after the first window appears, to see past a splash screen.",
            DefaultValueFactory = _ => 1_000,
        };
        Option<string?> titleOption = new("--title")
        {
            Description = "Prefer the window whose title contains this. A hint that ranks, not a filter.",
        };
        Option<string?> processOption = new("--process")
        {
            Description = "Prefer a window from this process. Helps when the program hands off to another.",
        };
        Option<bool> jsonOption = new("--json") { Description = "Emit JSON instead of a summary" };

        Command launch = new("launch", "Start a program, log its output, and report the window it opens")
        {
            envOption, cwdOption, logOption, appendOption,
            waitOption, settleOption, titleOption, processOption, jsonOption,
        };
        launch.Arguments.Add(programArgument);
        launch.Arguments.Add(argsArgument);

        launch.SetAction(async (parseResult, ct) =>
        {
            LaunchInput input = new(
                Path: parseResult.GetValue(programArgument)!,
                Arguments: parseResult.GetValue(argsArgument) ?? [],
                Environment: parseResult.GetValue(envOption) ?? [],
                WorkingDirectory: parseResult.GetValue(cwdOption),
                LogPath: parseResult.GetValue(logOption),
                AppendLog: parseResult.GetValue(appendOption),
                WaitForWindowMs: parseResult.GetValue(waitOption),
                SettleMs: parseResult.GetValue(settleOption),
                TitleContains: parseResult.GetValue(titleOption),
                ProcessName: parseResult.GetValue(processOption));

            LaunchResult result = await new LaunchCommand().RunAsync(input, ct);

            if (parseResult.GetValue(jsonOption))
            {
                Console.WriteLine(WinDeskCtlJson.Serialize(result));
            }
            else
            {
                WriteLaunch(result);
            }

            // A launch that started the program succeeded, whether or not a window was found —
            // the window is best-effort and the log path is the thing worth having either way.
            // Only a program that exited non-zero is reported as a failure.
            return result.ExitCode is not null and not 0 ? 1 : 0;
        });

        return launch;
    }

    private static void WriteLaunch(LaunchResult result)
    {
        string exit = result.ExitCode is int code ? $"exited {code}" : "running";
        Console.WriteLine($"pid {result.ProcessId}  {exit}");
        Console.WriteLine($"log {result.LogPath}");

        if (result.Window is WindowInfo w)
        {
            Console.WriteLine(
                $"win:{w.Hwnd}  {w.Rect.OriginX},{w.Rect.OriginY} {w.Rect.W}x{w.Rect.H}  " +
                $"{w.State}  {w.ProcessName}  {w.Title}");
        }
        else
        {
            // Never silent about this. The caller asked for a window and is not getting one, and
            // the log is where the reason will be.
            Console.WriteLine("no window found — read the log, or list windows to find it yourself");
        }

        foreach (WindowInfo other in result.OtherWindows)
        {
            Console.WriteLine(
                $"  also win:{other.Hwnd}  {other.Rect.OriginX},{other.Rect.OriginY} " +
                $"{other.Rect.W}x{other.Rect.H}  {other.Title}");
        }
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

        Option<bool> noFocusOption = new("--no-focus")
        {
            Description =
                "Do not bring a step's target window to the foreground. Input then lands wherever " +
                "focus already is — right for hovering or scrolling a background window on purpose, " +
                "wrong for typing.",
        };

        Command input = new("input", "Send a batch of mouse and keyboard steps atomically")
        {
            snapshotOption,
            noFocusOption,
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

            List<Step>? steps = WinDeskCtlJson.Deserialize<List<Step>>(raw);

            if (steps is null || steps.Count == 0)
            {
                Console.Error.WriteLine("No steps to run.");
                return 1;
            }

            InputResult result = await new InputCommand().RunAsync(
                new InputRequest(steps, Focus: !parseResult.GetValue(noFocusOption)), ct);

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

            if (result.Focused.Count > 0)
            {
                // A repeat means the window was taken back mid-batch, so the events in between
                // went somewhere else. Naming that here is the difference between debugging your
                // steps and debugging the desktop.
                string windows = string.Join(", ", result.Focused.Select(h => $"win:{h}"));
                Console.WriteLine(result.Focused.Distinct().Count() == result.Focused.Count
                    ? $"focused: {windows}"
                    : $"focused: {windows} — a repeat means something took the foreground back " +
                      "mid-batch; steps before it landed elsewhere.");
            }

            foreach (CapturedImage c in result.Captured)
            {
                Console.WriteLine(
                    $"captured {c.Path}  {c.Image}  size {c.Rect.W}x{c.Rect.H}  scale {c.Rect.Scale}" +
                    (c.Text is null ? "" : $"  {c.Text.Count} text line(s)"));
            }

            foreach (RecordResult r in result.Recorded)
            {
                Console.WriteLine(
                    $"recorded {r.Files.Count} frame(s)  {r.Image}  size {r.Rect.W}x{r.Rect.H}  " +
                    $"scale {r.Rect.Scale}  {Path.GetDirectoryName(r.Files[0])}");
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
                Console.WriteLine(WinDeskCtlJson.Serialize(result));
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

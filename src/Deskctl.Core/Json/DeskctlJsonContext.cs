using System.Text.Json;
using System.Text.Json.Serialization;
using Deskctl.Core.Capture;
using Deskctl.Core.Commands;
using Deskctl.Core.Frames;
using Deskctl.Core.Input;
using Deskctl.Core.Uia;
using Deskctl.Core.Windows;

namespace Deskctl.Core.Json;

/// <summary>
/// Every type that crosses JSON must be listed here. NativeAOT has no reflection fallback, so
/// an unlisted type does not degrade — it throws at runtime, after publish, in front of a user
///. Add new command inputs and outputs here in the same commit that introduces them.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Frame))]
[JsonSerializable(typeof(FrameRect))]
[JsonSerializable(typeof(Point))]
[JsonSerializable(typeof(DoctorInput))]
[JsonSerializable(typeof(DoctorReport))]
[JsonSerializable(typeof(DoctorCheck))]
[JsonSerializable(typeof(MonitorInfo))]
[JsonSerializable(typeof(CaptureInput))]
[JsonSerializable(typeof(CaptureResult))]
[JsonSerializable(typeof(CropBox))]
[JsonSerializable(typeof(ImageFormat))]
[JsonSerializable(typeof(WindowInfo))]
[JsonSerializable(typeof(WindowListInput))]
[JsonSerializable(typeof(WindowListResult))]
[JsonSerializable(typeof(WindowActionInput))]
[JsonSerializable(typeof(WindowActionResult))]
[JsonSerializable(typeof(WindowState))]
[JsonSerializable(typeof(WindowAction))]
[JsonSerializable(typeof(Step))]
[JsonSerializable(typeof(Step.Down))]
[JsonSerializable(typeof(Step.Up))]
[JsonSerializable(typeof(Step.Press))]
[JsonSerializable(typeof(Step.Move))]
[JsonSerializable(typeof(Step.Scroll))]
[JsonSerializable(typeof(Step.Text))]
[JsonSerializable(typeof(Step.Invoke))]
[JsonSerializable(typeof(Step.Fill))]
[JsonSerializable(typeof(Step.WaitFor))]
[JsonSerializable(typeof(Step.Delay))]
[JsonSerializable(typeof(List<Step>))]
[JsonSerializable(typeof(InputRequest))]
[JsonSerializable(typeof(InputResult))]
[JsonSerializable(typeof(ElementNode))]
[JsonSerializable(typeof(ElementSelector))]
[JsonSerializable(typeof(SnapshotInput))]
[JsonSerializable(typeof(SnapshotResult))]
[JsonSerializable(typeof(Resolution))]
// The input tool takes its step array as a raw JsonElement — the step grammar is field-tagged and
// hand-parsed, so the tool surface must accept arbitrary JSON rather than a fixed DTO. MCP builds
// each tool's parameter marshaller from this resolver at host STARTUP, so without this the server
// fails to start at all rather than failing on the first call.
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class DeskctlJsonContext : JsonSerializerContext;

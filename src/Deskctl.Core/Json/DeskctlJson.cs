using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Deskctl.Core.Frames;

namespace Deskctl.Core.Json;

/// <summary>
/// Frames cross the wire as their string form rather than as a tagged object. The consumer is
/// an LLM composing these by hand, and "monitor:1" is both readable and identical to the form
/// accepted on the CLI; a polymorphic object shape would be neither.
/// </summary>
public sealed class FrameJsonConverter : JsonConverter<Frame>
{
    public override Frame Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s is null || !Frame.TryParse(s, out Frame? frame) || frame is null)
        {
            throw new JsonException(
                $"'{s}' is not a frame. Expected 'virtual', 'monitor:<id>', 'win:<hwnd>', or 'elem:<handle>'.");
        }
        return frame;
    }

    public override void Write(Utf8JsonWriter writer, Frame value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

public static class DeskctlJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>
    /// Serializes <typeparamref name="T"/> through its source-generated metadata.
    /// </summary>
    /// <remarks>
    /// The <c>Serialize(value, options)</c> overload carries RequiresDynamicCode even when the
    /// options resolve to a source-generated context, because the compiler cannot prove the
    /// resolver covers <typeparamref name="T"/>. Resolving <see cref="JsonTypeInfo{T}"/> first
    /// keeps the call AOT-clean; a type missing from <c>DeskctlJsonContext</c> throws here
    /// rather than silently reflecting.
    /// </remarks>
    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T)));

    /// <summary>
    /// Deserializes <typeparamref name="T"/> through its source-generated metadata. The
    /// <c>Deserialize&lt;T&gt;(string, options)</c> overload carries RequiresUnreferencedCode for
    /// the same reason <see cref="Serialize{T}"/>'s does; resolving the
    /// <see cref="JsonTypeInfo{T}"/> first keeps the call AOT-clean.
    /// </summary>
    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize(json, (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T)));

    private static JsonSerializerOptions Create()
    {
        JsonSerializerOptions o = new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = DeskctlJsonContext.Default,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        o.Converters.Add(new FrameJsonConverter());
        o.Converters.Add(new Input.StepJsonConverter());
        // Enums serialize as strings via the source generator's UseStringEnumConverter, not a
        // runtime converter: the non-generic JsonStringEnumConverter needs dynamic code and is
        // unavailable under NativeAOT.
        o.MakeReadOnly();
        return o;
    }
}

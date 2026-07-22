using System.Text.Json;
using System.Text.Json.Serialization;
using WinDeskCtl.Core.Capture;
using WinDeskCtl.Core.Frames;

namespace WinDeskCtl.Core.Input;

/// <summary>
/// Reads the field-tagged step grammar. Every failure here is loud: an unrecognized verb, two
/// verbs in one step, or a down with both key and button all throw rather than picking one.
/// A dropped step is worse than a rejected batch, because the caller believes it ran.
/// </summary>
public sealed class StepJsonConverter : JsonConverter<Step>
{
    /// <summary>
    /// Claims every Step subtype, not just the abstract base. Without this the converter is
    /// bypassed whenever a caller deserializes a concrete type directly, and the source-generated
    /// metadata for that type — which cannot express "exactly one of these keys" — wins instead.
    /// </summary>
    public override bool CanConvert(Type type) => typeof(Step).IsAssignableFrom(type);

    public override Step Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A step must be an object, e.g. {\"down\":{\"key\":\"ctrl\"}}.");
        }

        List<string> verbs = [.. root.EnumerateObject().Select(p => p.Name)];
        if (verbs.Count != 1)
        {
            throw new JsonException(
                $"A step must have exactly one verb; found {verbs.Count} ({string.Join(", ", verbs)}). " +
                "Split them into separate steps.");
        }

        string verb = verbs[0];
        JsonElement body = root.GetProperty(verb);

        Step step = verb switch
        {
            "down" => new Step.Down(ReadTarget(body)),
            "up" => new Step.Up(ReadTarget(body)),
            "press" => new Step.Press(ReadTarget(body), OptString(body, "to")),
            "move" => new Step.Move(
                ReqString(body, "to", verb),
                OptDuration(body, "over"),
                OptEnum(body, "ease", Ease.Linear)),
            "scroll" => new Step.Scroll(
                OptInt(body, "dy", 0),
                OptInt(body, "dx", 0),
                OptString(body, "at")),
            "text" => new Step.Text(
                body.ValueKind == JsonValueKind.String
                    ? body.GetString()!
                    : throw new JsonException("'text' takes a bare string, e.g. {\"text\":\"hello\"}.")),
            "invoke" => new Step.Invoke(ReqString(body, "target", verb)),
            "fill" => new Step.Fill(ReqString(body, "target", verb), ReqString(body, "value", verb)),
            "waitFor" => new Step.WaitFor(
                ReqString(body, "target", verb),
                ReqDuration(body, "timeout")),
            "delay" => new Step.Delay(ReqDuration(body, "duration")),
            "capture" => new Step.Capture(
                Frame.Parse(ReqString(body, "target", verb)),
                ReqString(body, "path", verb),
                OptRegion(body),
                // The default cap is resolved at parse time rather than left null, so the step
                // records the cap it will actually run with.
                CaptureDefaults.Apply(OptNullableInt(body, "maxWidth"), OptNullableInt(body, "maxHeight")),
                OptNullableInt(body, "maxHeight"),
                OptEnum(body, "format", ImageFormat.Png),
                OptInt(body, "quality", 90),
                OptBool(body, "ocr")),
            "record" => new Step.Record(
                Frame.Parse(ReqString(body, "target", verb)),
                ReqString(body, "outputDir", verb),
                OptEnum(body, "preset", RecordPreset.Fast),
                OptBool(body, "background"),
                OptRegion(body),
                OptNullableInt(body, "maxWidth"),
                OptNullableInt(body, "maxHeight"),
                OptEnum(body, "format", ImageFormat.Png),
                OptInt(body, "quality", 90)),
            _ => throw new JsonException(
                $"'{verb}' is not a step. Expected one of: down, up, press, move, scroll, text, " +
                "invoke, fill, waitFor, delay, capture, record."),
        };

        // Deserializing a concrete type is a claim about which verb the JSON holds; honour it
        // rather than handing back a sibling the caller will fail to cast.
        if (!type.IsInstanceOfType(step))
        {
            throw new JsonException($"Expected a '{type.Name}' step but the verb was '{verb}'.");
        }

        return step;
    }

    private static InputTarget ReadTarget(JsonElement body)
    {
        bool hasKey = body.TryGetProperty("key", out JsonElement key);
        bool hasButton = body.TryGetProperty("button", out JsonElement button);

        if (hasKey && hasButton)
        {
            throw new JsonException("A step takes 'key' or 'button', never both.");
        }
        if (hasKey)
        {
            string name = key.GetString() ?? throw new JsonException("'key' must be a string.");

            // Lower-cased at the boundary because KeyRef is a record and the held-set matches by
            // value: without this, {"down":{"key":"CTRL"}} and {"up":{"key":"ctrl"}} are two
            // different entries, so the up never cancels the down. The batch would then report a
            // spurious auto-release while KeyMap — which is case-insensitive — resolved both to
            // the same VK and made the injected events look correct. Normalizing here keeps the
            // one canonical form in the set.
            return new KeyRef(name.ToLowerInvariant());
        }
        if (hasButton)
        {
            string name = button.GetString() ?? throw new JsonException("'button' must be a string.");
            return Enum.TryParse(name, ignoreCase: true, out MouseButton b)
                ? new ButtonRef(b)
                : throw new JsonException(
                    $"'{name}' is not a button. Expected: left, right, middle, x1, x2.");
        }

        throw new JsonException(
            "A down/up/press needs 'key' or 'button'. They are tagged because 'left' and 'right' " +
            "are both mouse buttons and arrow keys.");
    }

    private static string ReqString(JsonElement e, string name, string verb) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new JsonException($"'{verb}' requires a string '{name}'.");

    private static string? OptString(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int OptInt(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out JsonElement v) && v.TryGetInt32(out int i) ? i : fallback;

    private static int? OptNullableInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.TryGetInt32(out int i) ? i : null;

    private static bool OptBool(JsonElement e, string name) =>
        !e.TryGetProperty(name, out JsonElement v)
            ? false
            : v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new JsonException($"'{name}' must be a JSON boolean, not {v.ValueKind}."),
            };

    private static CropBox? OptRegion(JsonElement e) =>
        e.TryGetProperty("region", out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? CropBox.Parse(v.GetString()!)
            : null;

    private static TEnum OptEnum<TEnum>(JsonElement e, string name, TEnum fallback) where TEnum : struct, Enum
    {
        if (!e.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.String)
        {
            return fallback;
        }
        string s = v.GetString()!;
        return Enum.TryParse(s, ignoreCase: true, out TEnum result)
            ? result
            : throw new JsonException($"'{s}' is not a valid {typeof(TEnum).Name}.");
    }

    private static TimeSpan ReqDuration(JsonElement e, string name) =>
        OptDuration(e, name)
        ?? throw new JsonException($"A duration '{name}' is required, e.g. \"250ms\".");

    /// <summary>
    /// Durations are strings with an explicit unit ("250ms", "5s"), never bare numbers: a bare
    /// 250 is ambiguous between milliseconds and seconds, and an LLM composing a batch by hand
    /// will eventually guess wrong — by three orders of magnitude, silently.
    /// </summary>
    /// <remarks>
    /// Parsed here rather than through a JsonConverter&lt;TimeSpan&gt;: the element is already
    /// materialized, and JsonElement.Deserialize needs a JsonTypeInfo under NativeAOT, which the
    /// source-generated context does not carry for TimeSpan.
    /// </remarks>
    private static TimeSpan? OptDuration(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out JsonElement v)) return null;

        if (v.ValueKind != JsonValueKind.String)
        {
            throw new JsonException(
                $"'{name}' must be a string with a unit, e.g. \"250ms\" or \"5s\". A bare number is " +
                "ambiguous between milliseconds and seconds.");
        }

        string s = v.GetString()!;

        if (s.EndsWith("ms", StringComparison.Ordinal) && double.TryParse(s[..^2], out double ms))
        {
            return TimeSpan.FromMilliseconds(ms);
        }
        if (s.EndsWith('s') && double.TryParse(s[..^1], out double sec))
        {
            return TimeSpan.FromSeconds(sec);
        }

        throw new JsonException($"'{s}' is not a duration. Expected a number followed by 'ms' or 's'.");
    }

    public override void Write(Utf8JsonWriter writer, Step value, JsonSerializerOptions options) =>
        throw new NotSupportedException("Steps are an input grammar; windeskctl never emits them.");
}

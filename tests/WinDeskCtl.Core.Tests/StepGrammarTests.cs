using System.Text.Json;
using WinDeskCtl.Core.Input;
using WinDeskCtl.Core.Json;

namespace WinDeskCtl.Core.Tests;

public class StepGrammarTests
{
    private static T Parse<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, WinDeskCtlJson.Options)!;

    [Fact]
    public void Down_WithKey_IsAKeyboardStep()
    {
        Step.Down s = Assert.IsType<Step.Down>(Parse<Step>("""{"down":{"key":"ctrl"}}"""));
        KeyRef k = Assert.IsType<KeyRef>(s.Target);
        Assert.Equal("ctrl", k.Name);
    }

    [Fact]
    public void Down_WithButton_IsAMouseStep()
    {
        Step.Down s = Assert.IsType<Step.Down>(Parse<Step>("""{"down":{"button":"left"}}"""));
        ButtonRef b = Assert.IsType<ButtonRef>(s.Target);
        Assert.Equal(MouseButton.Left, b.Button);
    }

    [Fact]
    public void LeftIsAmbiguous_AndTheTagResolvesIt()
    {
        // The reason down/up/press are field-tagged: 'left' is a mouse button AND an arrow key
        // (VK_LEFT). Value-based inference breaks on the two most common tokens in the API
        //.
        Assert.IsType<ButtonRef>(Parse<Step.Down>("""{"down":{"button":"left"}}""").Target);
        Assert.IsType<KeyRef>(Parse<Step.Down>("""{"down":{"key":"left"}}""").Target);
    }

    [Fact]
    public void KeyNames_AreNormalizedToLowercase()
    {
        // KeyRef is a record and the held-set matches by value, so a down and its up must produce
        // an EQUAL KeyRef or the up never cancels the down. KeyMap resolves both cases to the
        // same VK, which would make the injected events look right while the held-set leaked.
        Assert.Equal(
            Parse<Step.Down>("""{"down":{"key":"ctrl"}}""").Target,
            Parse<Step.Down>("""{"down":{"key":"CTRL"}}""").Target);
    }

    [Fact]
    public void Down_WithBothKeyAndButton_Throws()
    {
        Assert.ThrowsAny<JsonException>(
            () => Parse<Step>("""{"down":{"key":"ctrl","button":"left"}}"""));
    }

    [Fact]
    public void Down_WithNeitherKeyNorButton_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => Parse<Step>("""{"down":{}}"""));
    }

    [Fact]
    public void Press_WithTo_IsAClick()
    {
        Step.Press s = Assert.IsType<Step.Press>(
            Parse<Step>("""{"press":{"button":"left","to":"elem:row-1"}}"""));
        Assert.IsType<ButtonRef>(s.Target);
        Assert.Equal("elem:row-1", s.To);
    }

    [Fact]
    public void Move_InfersMouse_AndParsesDurationAndEase()
    {
        Step.Move s = Assert.IsType<Step.Move>(
            Parse<Step>("""{"move":{"to":"win:100@400,200","over":"250ms","ease":"easeOut"}}"""));
        Assert.Equal("win:100@400,200", s.To);
        Assert.Equal(TimeSpan.FromMilliseconds(250), s.Over);
        Assert.Equal(Ease.EaseOut, s.Ease);
    }

    [Fact]
    public void Move_WithoutOver_IsInstant()
    {
        Assert.Null(Assert.IsType<Step.Move>(Parse<Step>("""{"move":{"to":"monitor:1@10,10"}}""")).Over);
    }

    [Fact]
    public void Text_IsAKeyboardStep_TakingABareString()
    {
        Assert.Equal("hello world", Assert.IsType<Step.Text>(Parse<Step>("""{"text":"hello world"}""")).Value);
    }

    [Fact]
    public void Scroll_InfersMouse()
    {
        Step.Scroll s = Assert.IsType<Step.Scroll>(Parse<Step>("""{"scroll":{"dy":-3,"at":"elem:list"}}"""));
        Assert.Equal(-3, s.Dy);
        Assert.Equal("elem:list", s.At);
    }

    private static string WaitForWithTimeout(string timeoutJson) =>
        """{"waitFor":{"target":"elem:d","timeout":""" + timeoutJson + "}}";

    [Theory]
    [InlineData("\"250ms\"", 250)]
    [InlineData("\"5s\"", 5000)]
    [InlineData("\"1500ms\"", 1500)]
    public void Duration_AcceptsMsAndS(string json, int expectedMs)
    {
        Step.WaitFor s = Parse<Step.WaitFor>(WaitForWithTimeout(json));
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), s.Timeout);
    }

    [Theory]
    [InlineData("\"250\"")]      // no unit
    [InlineData("\"250m\"")]     // wrong unit
    [InlineData("250")]          // bare number
    public void Duration_WithoutAValidUnit_Throws(string json)
    {
        Assert.ThrowsAny<JsonException>(() => Parse<Step.WaitFor>(WaitForWithTimeout(json)));
    }

    [Fact]
    public void UnknownVerb_Throws()
    {
        // Silently ignoring an unrecognized verb would drop a step the caller believed ran.
        Assert.ThrowsAny<JsonException>(() => Parse<Step>("""{"teleport":{"to":"monitor:1@0,0"}}"""));
    }

    [Fact]
    public void Step_WithTwoVerbs_Throws()
    {
        Assert.ThrowsAny<JsonException>(
            () => Parse<Step>("""{"down":{"key":"ctrl"},"up":{"key":"ctrl"}}"""));
    }

    [Fact]
    public void Request_ParsesAnArrayOfMixedSteps()
    {
        InputRequest r = Parse<InputRequest>("""
        {"steps":[
          {"down":{"key":"ctrl"}},
          {"press":{"button":"left","to":"elem:row-1"}},
          {"up":{"key":"ctrl"}}
        ]}
        """);

        Assert.Collection(r.Steps,
            s => Assert.IsType<Step.Down>(s),
            s => Assert.IsType<Step.Press>(s),
            s => Assert.IsType<Step.Up>(s));
    }

    [Fact]
    public void Capture_Parses_EveryField()
    {
        Step.Capture s = Assert.IsType<Step.Capture>(Parse<Step>("""
            {"capture":{"target":"win:100","path":"C:/t/a.png","region":"10,20,300,200",
                        "maxWidth":800,"format":"jpeg","quality":70,"ocr":true}}
            """));

        Assert.Equal(new Frames.Frame.Window(100), s.Target);
        Assert.Equal("C:/t/a.png", s.Path);
        Assert.Equal(new Capture.CropBox(10, 20, 300, 200), s.Region);
        Assert.Equal(800, s.MaxWidth);
        Assert.Equal(Capture.ImageFormat.Jpeg, s.Format);
        Assert.Equal(70, s.Quality);
        Assert.True(s.Ocr);
    }

    [Fact]
    public void Capture_WithNoCap_DefaultsTheWidthCap()
    {
        // Resolved at parse time so the step records the cap it will actually run with — a
        // defaulted cap that only materialized at execution would be invisible to the planner's
        // tests and to anyone reading the parsed batch.
        Step.Capture s = Assert.IsType<Step.Capture>(
            Parse<Step>("""{"capture":{"target":"win:100","path":"C:/t/a.png"}}"""));

        Assert.Equal(Capture.CaptureDefaults.MaxWidth, s.MaxWidth);
        Assert.False(s.Ocr);
    }

    [Fact]
    public void Capture_WithAHeightCapOnly_GetsNoWidthDefault()
    {
        Step.Capture s = Assert.IsType<Step.Capture>(
            Parse<Step>("""{"capture":{"target":"win:100","path":"C:/t/a.png","maxHeight":600}}"""));

        Assert.Null(s.MaxWidth);
        Assert.Equal(600, s.MaxHeight);
    }

    [Fact]
    public void Capture_WithoutAPath_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => Parse<Step>("""{"capture":{"target":"win:100"}}"""));
    }

    [Fact]
    public void Capture_WithANonBooleanOcr_Throws()
    {
        // A silently-false "true"-the-string would run without OCR while the caller believes
        // it asked for it.
        Assert.ThrowsAny<JsonException>(() => Parse<Step>(
            """{"capture":{"target":"win:100","path":"C:/t/a.png","ocr":"true"}}"""));
    }

    [Fact]
    public void Record_Parses_PresetAndBackground()
    {
        Step.Record s = Assert.IsType<Step.Record>(Parse<Step>("""
            {"record":{"target":"win:100","outputDir":"C:/t/burst","preset":"slow","background":true}}
            """));

        Assert.Equal(Capture.RecordPreset.Slow, s.Preset);
        Assert.True(s.Background);
    }

    [Fact]
    public void Record_StaysUncappedByDefault()
    {
        // The capture default cap is deliberate about NOT applying here: burst frames are
        // already bounded by preset, and sampling motion wants the source resolution unless the
        // caller says otherwise.
        Step.Record s = Assert.IsType<Step.Record>(
            Parse<Step>("""{"record":{"target":"win:100","outputDir":"C:/t/burst"}}"""));

        Assert.Null(s.MaxWidth);
        Assert.Null(s.MaxHeight);
        Assert.False(s.Background);
        Assert.Equal(Capture.RecordPreset.Fast, s.Preset);
    }

    [Fact]
    public void Record_WithoutAnOutputDir_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => Parse<Step>("""{"record":{"target":"win:100"}}"""));
    }
}

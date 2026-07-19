using WinDeskCtl.Core.Input;

namespace WinDeskCtl.Core.Tests;

public class KeyMapTests
{
    [Theory]
    [InlineData("ctrl", 0x11)]
    [InlineData("control", 0x11)]     // alias
    [InlineData("shift", 0x10)]
    [InlineData("alt", 0x12)]
    [InlineData("win", 0x5B)]
    [InlineData("enter", 0x0D)]
    [InlineData("return", 0x0D)]      // alias
    [InlineData("esc", 0x1B)]
    [InlineData("escape", 0x1B)]      // alias
    [InlineData("tab", 0x09)]
    [InlineData("space", 0x20)]
    [InlineData("backspace", 0x08)]
    [InlineData("delete", 0x2E)]
    [InlineData("left", 0x25)]        // the arrow key, NOT the mouse button
    [InlineData("up", 0x26)]
    [InlineData("right", 0x27)]
    [InlineData("down", 0x28)]
    [InlineData("home", 0x24)]
    [InlineData("end", 0x23)]
    [InlineData("pageup", 0x21)]
    [InlineData("pagedown", 0x22)]
    [InlineData("f1", 0x70)]
    [InlineData("f12", 0x7B)]
    [InlineData("a", 0x41)]
    [InlineData("z", 0x5A)]
    [InlineData("0", 0x30)]
    [InlineData("9", 0x39)]
    public void Resolve_MapsKnownNames(string name, int expected)
    {
        Assert.Equal((ushort)expected, KeyMap.Resolve(name));
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        Assert.Equal(KeyMap.Resolve("ctrl"), KeyMap.Resolve("CTRL"));
        Assert.Equal(KeyMap.Resolve("f1"), KeyMap.Resolve("F1"));
    }

    [Fact]
    public void Resolve_UnknownName_ThrowsWithSuggestions()
    {
        // The consumer is an LLM. "'contrl' is not a key" teaches it nothing; naming the near
        // match lets it fix the batch without another round trip.
        ArgumentException ex = Assert.Throws<ArgumentException>(() => KeyMap.Resolve("contrl"));
        Assert.Contains("contrl", ex.Message);
        Assert.Contains("ctrl", ex.Message);
    }

    [Fact]
    public void Resolve_EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() => KeyMap.Resolve(""));
    }

    [Fact]
    public void LeftArrow_AndMouseLeft_AreNotConfused()
    {
        // KeyMap only knows keys. The mouse button named "left" never reaches it — the step
        // grammar's field tag routes it to ButtonRef instead.
        Assert.Equal((ushort)0x25, KeyMap.Resolve("left"));
    }

    [Fact]
    public void KnownNames_IncludesEveryAlias()
    {
        Assert.Contains("ctrl", KeyMap.KnownNames);
        Assert.Contains("control", KeyMap.KnownNames);
        Assert.Contains("f24", KeyMap.KnownNames);
    }
}

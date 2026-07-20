using WinDeskCtl.Core.Launch;

namespace WinDeskCtl.Core.Tests;

public class CommandLineTests
{
    [Fact]
    public void PlainArguments_AreNotQuoted()
    {
        Assert.Equal("app.exe --flag value", CommandLine.Build("app.exe", ["--flag", "value"]));
    }

    [Fact]
    public void SpacesForceQuoting()
    {
        Assert.Equal(
            "\"C:\\Program Files\\App\\app.exe\" \"two words\"",
            CommandLine.Build(@"C:\Program Files\App\app.exe", ["two words"]));
    }

    [Fact]
    public void TrailingBackslashIsDoubledBeforeTheClosingQuote()
    {
        // Left alone, the backslash escapes the closing quote and the next argument is swallowed
        // into this one — the failure this whole escaper exists to prevent.
        Assert.Equal(@"app.exe ""C:\some dir\\"" next", CommandLine.Build("app.exe", [@"C:\some dir\", "next"]));
    }

    [Fact]
    public void InteriorBackslashesAreNotEscaped()
    {
        // Backslashes only act as escapes in front of a quote, so a path's separators pass through.
        Assert.Equal(@"app.exe C:\a\b\c", CommandLine.Build("app.exe", [@"C:\a\b\c"]));
    }

    [Fact]
    public void QuotesAreEscaped()
    {
        Assert.Equal(@"app.exe ""say \""hi\""""", CommandLine.Build("app.exe", ["say \"hi\""]));
    }

    [Fact]
    public void BackslashesBeforeAQuoteAreDoubledAndTheQuoteEscaped()
    {
        Assert.Equal(@"app.exe ""a\\\""b""", CommandLine.Build("app.exe", [@"a\""b"]));
    }

    [Fact]
    public void EmptyArgumentSurvivesAsAQuotedPair()
    {
        // An unquoted empty argument would vanish in the split, shifting every later one.
        Assert.Equal("app.exe \"\" after", CommandLine.Build("app.exe", ["", "after"]));
    }

    [Fact]
    public void ShellMetacharactersArePassedThroughUntouched()
    {
        // Nothing re-parses this line, so an ampersand is data rather than a command separator.
        Assert.Equal(
            "app.exe https://x/?a=1&b=2%PATH%",
            CommandLine.Build("app.exe", ["https://x/?a=1&b=2%PATH%"]));
    }

    [Fact]
    public void NoArguments_IsJustTheProgram()
    {
        Assert.Equal("app.exe", CommandLine.Build("app.exe", []));
    }
}

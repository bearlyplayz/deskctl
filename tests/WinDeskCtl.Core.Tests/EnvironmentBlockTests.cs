using WinDeskCtl.Core.Launch;

namespace WinDeskCtl.Core.Tests;

public class EnvironmentBlockTests
{
    private static readonly KeyValuePair<string, string>[] Inherited =
    [
        new("PATH", @"C:\windows"),
        new("HOME", @"C:\users\me"),
    ];

    [Fact]
    public void NoAssignments_YieldsNoBlock()
    {
        // Null tells CreateProcess to hand the child this process's environment as-is, which is
        // not the same as handing it a rebuilt copy.
        Assert.Null(EnvironmentBlock.Build(Inherited, []));
    }

    [Fact]
    public void AssignmentsAreLayeredOverTheInheritedSet()
    {
        string block = EnvironmentBlock.Build(Inherited, ["EXTRA=1"])!;

        Assert.Equal("EXTRA=1\0HOME=C:\\users\\me\0PATH=C:\\windows\0\0", block);
    }

    [Fact]
    public void AnAssignmentOverridesTheInheritedValue()
    {
        string block = EnvironmentBlock.Build(Inherited, [@"PATH=C:\custom"])!;

        Assert.Contains("PATH=C:\\custom\0", block, StringComparison.Ordinal);
        Assert.DoesNotContain("PATH=C:\\windows", block, StringComparison.Ordinal);
    }

    [Fact]
    public void OverrideMatchesTheInheritedNameCaseInsensitively()
    {
        // Windows environment names are case-insensitive, so two spellings must not both appear —
        // a duplicate name leaves which one wins up to whoever reads the block.
        string block = EnvironmentBlock.Build(Inherited, ["path=C:\\custom"])!;

        Assert.Single(block.Split('\0'), e => e.StartsWith("path=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BlockIsSortedAndDoubleNullTerminated()
    {
        string block = EnvironmentBlock.Build([new("Z", "1"), new("A", "2")], ["M=3"])!;

        Assert.Equal("A=2\0M=3\0Z=1\0\0", block);
    }

    [Fact]
    public void ValueMayContainEqualsSigns()
    {
        Assert.Equal(
            new KeyValuePair<string, string>("CONN", "a=b;c=d"),
            EnvironmentBlock.ParseAssignment("CONN=a=b;c=d"));
    }

    [Fact]
    public void EmptyValueIsAllowed()
    {
        Assert.Equal(
            new KeyValuePair<string, string>("EMPTY", ""),
            EnvironmentBlock.ParseAssignment("EMPTY="));
    }

    [Theory]
    [InlineData("NOEQUALS")]
    [InlineData("=novalue")]
    [InlineData("")]
    public void MalformedAssignmentIsRefused(string assignment)
    {
        Assert.Throws<ArgumentException>(() => EnvironmentBlock.ParseAssignment(assignment));
    }
}

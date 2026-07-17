using Deskctl.Core.Uia;

namespace Deskctl.Core.Tests;

public class HandleMinterTests
{
    [Fact]
    public void Mint_UsesControlTypeAndName()
    {
        // Handles are readable, so scripts say elem:btn-save rather than elem:7.
        Assert.Equal("btn-save", new HandleMinter().Mint("button", "Save", null));
    }

    [Fact]
    public void Mint_AbbreviatesCommonControlTypes()
    {
        HandleMinter m = new();
        Assert.Equal("btn-ok", m.Mint("button", "OK", null));
        Assert.Equal("txt-search", m.Mint("edit", "Search", null));
        Assert.Equal("chk-remember", m.Mint("checkbox", "Remember", null));
    }

    [Fact]
    public void Mint_Collision_GetsANumericSuffix()
    {
        HandleMinter m = new();
        Assert.Equal("btn-save", m.Mint("button", "Save", null));
        Assert.Equal("btn-save-2", m.Mint("button", "Save", null));
        Assert.Equal("btn-save-3", m.Mint("button", "Save", null));
    }

    [Fact]
    public void Mint_PrefersAutomationId_WhenTheNameIsEmpty()
    {
        // An unnamed element is very common — icon buttons, canvas hosts. AutomationId is the
        // developer's own identifier and is more stable than a position.
        Assert.Equal("btn-close-btn", new HandleMinter().Mint("button", "", "closeBtn"));
    }

    [Fact]
    public void Mint_WithNeitherNameNorAutomationId_FallsBackToAnIndex()
    {
        HandleMinter m = new();
        Assert.Equal("btn-1", m.Mint("button", null, null));
        Assert.Equal("btn-2", m.Mint("button", null, null));
    }

    [Fact]
    public void Mint_IsDeterministic_ForTheSameSequence()
    {
        // Two snapshots of an unchanged UI must produce the same handles, or a script written
        // against one snapshot cannot be re-run.
        static string[] Run()
        {
            HandleMinter m = new();
            return [m.Mint("button", "Save", null), m.Mint("button", "Save", null), m.Mint("edit", "Name", null)];
        }

        Assert.Equal(Run(), Run());
    }

    [Theory]
    [InlineData("Save", "save")]
    [InlineData("Save As...", "save-as")]
    [InlineData("  Trim  Me  ", "trim-me")]
    [InlineData("File/Edit", "file-edit")]
    [InlineData("Ctrl+S", "ctrl-s")]
    [InlineData("100%", "100")]
    [InlineData("---", "")]
    [InlineData("", "")]
    [InlineData("closeBtn", "close-btn")]     // an AutomationId's only word boundary is its hump
    [InlineData("OK", "ok")]                  // an acronym is one word, not "o-k"
    [InlineData("Save 2", "save-2")]
    public void Slug_IsLowercaseKebab(string input, string expected)
    {
        Assert.Equal(expected, HandleMinter.Slug(input));
    }

    [Fact]
    public void Slug_TruncatesALongName()
    {
        // Element names can be an entire paragraph — a list item's name is often its full text.
        // An unbounded handle would be unusable in a script and would bloat every snapshot.
        string slug = HandleMinter.Slug(new string('a', 200));
        Assert.True(slug.Length <= 40, $"slug was {slug.Length} chars");
    }

    [Fact]
    public void Slug_DoesNotEndWithASeparator()
    {
        Assert.Equal("save", HandleMinter.Slug("Save..."));
        Assert.Equal("save", HandleMinter.Slug("Save "));
    }

    [Fact]
    public void Mint_NameThatSlugsToNothing_FallsBackToAnIndex()
    {
        // A separator menu item is named "---" and slugs to empty.
        Assert.Equal("menuitem-1", new HandleMinter().Mint("menuitem", "---", null));
    }

    [Fact]
    public void Mint_NeverReturnsAnEmptyHandle()
    {
        HandleMinter m = new();
        foreach (string? name in new[] { null, "", "   ", "---" })
        {
            Assert.NotEmpty(m.Mint("custom", name, null));
        }
    }

    [Fact]
    public void Mint_CollisionSuffix_DoesNotCollideWithARealName()
    {
        // The nasty one: two "Save" buttons mint btn-save and btn-save-2 by suffixing, and a
        // button genuinely named "Save 2" also slugs to btn-save-2. A minter that reserves only
        // the candidate and not the name it handed out issues that handle twice.
        HandleMinter m = new();
        Assert.Equal("btn-save", m.Mint("button", "Save", null));
        Assert.Equal("btn-save-2", m.Mint("button", "Save", null));
        Assert.NotEqual("btn-save-2", m.Mint("button", "Save 2", null));
    }

    [Fact]
    public void Mint_NeverReturnsTheSameHandleTwice()
    {
        // The invariant the whole registry rests on: a duplicate handle makes one of the two
        // elements permanently unreachable. Exercises the paths that collide by construction —
        // a real name, an anonymous fallback, and an automationId — in one minter.
        HandleMinter m = new();
        List<string> minted =
        [
            m.Mint("button", "Save", null),
            m.Mint("button", "Save", null),
            m.Mint("button", null, null),
            m.Mint("button", null, null),
            m.Mint("button", "", "save"),
            m.Mint("menuitem", "---", null),
            m.Mint("menuitem", null, null),
        ];

        Assert.Equal(minted.Count, minted.Distinct().Count());
    }
}

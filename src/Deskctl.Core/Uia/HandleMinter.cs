using System.Text;

namespace Deskctl.Core.Uia;

/// <summary>
/// Mints opaque element handles that a human can still read.
/// </summary>
/// <remarks>
/// Handles are derived from control type and name so scripts say elem:btn-save rather than
/// elem:7. They remain opaque to the caller: the readability is for the reader, and
/// a caller must not construct or parse one.
///
/// One minter per snapshot. Collision suffixes depend on document order, so a minter is
/// deterministic for a given tree walk and meaningless across walks.
/// </remarks>
public sealed class HandleMinter
{
    private const int MaxSlugLength = 40;

    private readonly Dictionary<string, int> _counts = [];
    private readonly HashSet<string> _issued = [];
    private readonly Dictionary<string, int> _anonymous = [];

    /// <summary>
    /// Short prefixes for the control types that dominate a real tree. An unlisted type uses its
    /// own slugged name, which stays readable at the cost of length.
    /// </summary>
    private static readonly Dictionary<string, string> Prefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["button"] = "btn",
        ["edit"] = "txt",
        ["document"] = "doc",
        ["checkbox"] = "chk",
        ["radiobutton"] = "rad",
        ["combobox"] = "cmb",
        ["list"] = "lst",
        ["listitem"] = "item",
        ["menuitem"] = "menuitem",
        ["tabitem"] = "tab",
        ["hyperlink"] = "link",
        ["text"] = "lbl",
        ["image"] = "img",
        ["tree"] = "tree",
        ["treeitem"] = "node",
        ["window"] = "win",
        ["pane"] = "pane",
        ["group"] = "grp",
        ["table"] = "tbl",
        ["slider"] = "sld",
    };

    public string Mint(string controlType, string? name, string? automationId)
    {
        string prefix = Prefixes.TryGetValue(controlType, out string? p) ? p : Slug(controlType);
        if (prefix.Length == 0) prefix = "elem";

        // Name first: it is what the user sees and what an LLM reasoning about the UI will reach
        // for. AutomationId is the developer's identifier — more stable, but often absent and
        // rarely meaningful to a reader.
        string label = Slug(name ?? "");
        if (label.Length == 0) label = Slug(automationId ?? "");

        if (label.Length == 0)
        {
            // Nothing to name it after. Fall back to a per-prefix counter — positional and
            // fragile, but every element must be addressable.
            int n = _anonymous.GetValueOrDefault(prefix) + 1;
            _anonymous[prefix] = n;
            return Reserve($"{prefix}-{n}");
        }

        return Reserve($"{prefix}-{label}");
    }

    /// <summary>Appends -2, -3, ... on collision. Two "Save" buttons is ordinary, and silently
    /// handing both the same handle would make one unreachable.</summary>
    /// <remarks>
    /// Every handle actually issued is recorded, not just the candidate asked for. Recording only
    /// the candidate would let a suffixed handle collide with a real name: two "Save" buttons take
    /// btn-save and btn-save-2, and a button named "Save 2" then slugs to the already-issued
    /// btn-save-2. The loop re-checks for the same reason — the suffixed form can itself be taken.
    /// </remarks>
    private string Reserve(string candidate)
    {
        int taken = _counts.GetValueOrDefault(candidate);
        string handle = taken == 0 ? candidate : $"{candidate}-{taken + 1}";

        // _issued is the authority on what has actually gone out; the counter only picks the
        // next suffix to try. They diverge exactly when a suffixed handle meets a real name.
        while (!_issued.Add(handle))
        {
            taken++;
            handle = $"{candidate}-{taken + 1}";
        }

        _counts[candidate] = taken + 1;
        return handle;
    }

    /// <summary>Lowercase kebab-case, truncated. Non-alphanumerics collapse to a single hyphen,
    /// and a camelCase hump becomes a word boundary.</summary>
    /// <remarks>
    /// The camelCase split exists for AutomationIds, which are identifiers rather than prose:
    /// "closeBtn" is the developer's name for the element, and slugging it to "closebtn" would
    /// throw away the only word boundary it has.
    /// </remarks>
    public static string Slug(string s)
    {
        StringBuilder sb = new(Math.Min(s.Length, MaxSlugLength));
        bool pendingSeparator = false;
        char previous = '\0';

        foreach (char c in s)
        {
            // Compared against the ORIGINAL previous character, not the lowercased one already
            // appended. An acronym must not shatter: "OK" is one word, while "closeBtn" is two.
            if (char.IsUpper(c) && (char.IsLower(previous) || char.IsDigit(previous)))
            {
                pendingSeparator = true;
            }
            previous = c;

            if (char.IsLetterOrDigit(c))
            {
                // Deferred so a run of separators collapses to one and a trailing run to none,
                // which is why "Save..." slugs to "save" rather than "save-".
                if (pendingSeparator && sb.Length > 0) sb.Append('-');
                pendingSeparator = false;

                if (sb.Length >= MaxSlugLength) break;
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return sb.ToString();
    }
}

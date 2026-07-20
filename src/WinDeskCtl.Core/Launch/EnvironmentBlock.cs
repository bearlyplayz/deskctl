using System.Text;

namespace WinDeskCtl.Core.Launch;

/// <summary>
/// Turns <c>KEY=VALUE</c> assignments layered over an existing environment into the block
/// CreateProcess consumes.
/// </summary>
/// <remarks>
/// The block is <c>KEY=VALUE\0KEY=VALUE\0\0</c>. Names are case-insensitive and must be unique,
/// which is why the caller's assignments are folded into the inherited set rather than appended
/// to it — a duplicate name leaves which one wins up to whoever reads the block.
///
/// Entries are sorted by name. Windows tolerates an unsorted block, but the sort makes the
/// result deterministic for a given input, which is what lets it be tested at all.
/// </remarks>
public static class EnvironmentBlock
{
    /// <summary>
    /// Splits one <c>KEY=VALUE</c> assignment. The value may contain '=' and may be empty; the
    /// name may not, and an assignment without '=' at all is a caller mistake rather than a
    /// variable with no value.
    /// </summary>
    public static KeyValuePair<string, string> ParseAssignment(string assignment)
    {
        int split = assignment.IndexOf('=');
        if (split <= 0)
        {
            throw new ArgumentException(
                $"Environment entry '{assignment}' is not NAME=VALUE. The name must be non-empty and " +
                "come before the first '='.", nameof(assignment));
        }

        return new KeyValuePair<string, string>(assignment[..split], assignment[(split + 1)..]);
    }

    /// <summary>
    /// Layers <paramref name="assignments"/> over <paramref name="inherited"/> and renders the
    /// block. Returns null when there is nothing to override, which tells CreateProcess to hand
    /// the child this process's environment unchanged.
    /// </summary>
    public static string? Build(
        IEnumerable<KeyValuePair<string, string>> inherited,
        IReadOnlyList<string> assignments)
    {
        if (assignments.Count == 0) return null;

        Dictionary<string, string> merged = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string value) in inherited) merged[name] = value;
        foreach (string assignment in assignments)
        {
            (string name, string value) = ParseAssignment(assignment);
            merged[name] = value;
        }

        StringBuilder block = new();
        foreach ((string name, string value) in merged.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            block.Append(name).Append('=').Append(value).Append('\0');
        }

        return block.Append('\0').ToString();
    }
}

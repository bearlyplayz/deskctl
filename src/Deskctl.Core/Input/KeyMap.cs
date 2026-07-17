namespace Deskctl.Core.Input;

/// <summary>
/// Key names to virtual-key codes.
/// </summary>
/// <remarks>
/// Named keys resolve to VK codes, which is what normal apps read. Literal text does NOT come
/// through here — a <c>text</c> step uses KEYEVENTF_UNICODE, which is layout-independent and can
/// emit any character, whereas a VK's meaning depends on the active layout. Scan codes are not
/// emitted: apps that read raw scan codes (notably games via DirectInput) will not see these.
/// </remarks>
public static class KeyMap
{
    private static readonly Dictionary<string, ushort> Map = BuildMap();

    public static IReadOnlyCollection<string> KnownNames => Map.Keys;

    private static Dictionary<string, ushort> BuildMap()
    {
        Dictionary<string, ushort> m = new(StringComparer.OrdinalIgnoreCase)
        {
            // Modifiers. These are the un-sided VKs: Windows accepts them for injection and
            // apps that care about which side use the sided variants themselves.
            ["ctrl"] = 0x11, ["control"] = 0x11,
            ["shift"] = 0x10,
            ["alt"] = 0x12, ["menu"] = 0x12,
            ["win"] = 0x5B, ["lwin"] = 0x5B, ["rwin"] = 0x5C,

            ["enter"] = 0x0D, ["return"] = 0x0D,
            ["esc"] = 0x1B, ["escape"] = 0x1B,
            ["tab"] = 0x09,
            ["space"] = 0x20,
            ["backspace"] = 0x08, ["bksp"] = 0x08,
            ["delete"] = 0x2E, ["del"] = 0x2E,
            ["insert"] = 0x2D, ["ins"] = 0x2D,

            ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
            ["home"] = 0x24, ["end"] = 0x23,
            ["pageup"] = 0x21, ["pgup"] = 0x21,
            ["pagedown"] = 0x22, ["pgdn"] = 0x22,

            ["capslock"] = 0x14,
            ["numlock"] = 0x90,
            ["scrolllock"] = 0x91,
            ["printscreen"] = 0x2C, ["prtsc"] = 0x2C,
            ["pause"] = 0x13,
            ["apps"] = 0x5D, ["menukey"] = 0x5D,
        };

        // F1-F24 are contiguous from VK_F1.
        for (int i = 1; i <= 24; i++) m[$"f{i}"] = (ushort)(0x70 + i - 1);

        // A-Z and 0-9 map to their ASCII values by definition of the VK table.
        for (char c = 'a'; c <= 'z'; c++) m[c.ToString()] = (ushort)char.ToUpperInvariant(c);
        for (char c = '0'; c <= '9'; c++) m[c.ToString()] = c;

        return m;
    }

    public static bool TryResolve(string name, out ushort vk) => Map.TryGetValue(name, out vk);

    public static ushort Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A key name is required.", nameof(name));
        }
        if (TryResolve(name, out ushort vk)) return vk;

        throw new ArgumentException(
            $"'{name}' is not a key. Did you mean: {string.Join(", ", NearMatches(name).Take(3))}?",
            nameof(name));
    }

    /// <summary>
    /// The three closest known names by edit distance.
    /// </summary>
    /// <remarks>
    /// Edit distance rather than prefix matching: the typo that motivates this is a dropped
    /// letter mid-word ("contrl"), where the prefix shared with the intended "ctrl" is a single
    /// character and prefix ranking never surfaces it. The table is ~90 entries and this runs
    /// only on the error path, so the quadratic inner loop costs nothing worth avoiding.
    /// </remarks>
    private static IEnumerable<string> NearMatches(string name)
    {
        string lower = name.ToLowerInvariant();

        return
            from k in Map.Keys
            let d = EditDistance(k, lower)
            orderby d, k
            select k;
    }

    private static int EditDistance(string a, string b)
    {
        // Two rows rather than the full matrix: only the previous row is ever read.
        int[] previous = [.. Enumerable.Range(0, b.Length + 1)];
        int[] current = new int[b.Length + 1];

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}

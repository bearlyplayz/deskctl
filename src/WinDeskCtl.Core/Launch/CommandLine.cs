using System.Text;

namespace WinDeskCtl.Core.Launch;

/// <summary>
/// Builds the single command-line string CreateProcess takes from a program and its argument
/// list.
/// </summary>
/// <remarks>
/// Win32 has no argv — it passes one string and lets the program split it, which for anything
/// built on the C runtime or .NET means CommandLineToArgvW's rules. Those rules are not
/// "wrap it in quotes": backslashes are only escapes when they precede a quote, so a trailing
/// <c>C:\dir\</c> inside quotes escapes the closing quote and swallows the next argument.
///
/// Quoting here rather than handing the line to <c>cmd /c</c> is what keeps an argument
/// containing <c>&amp;</c>, <c>|</c>, <c>^</c>, or <c>%VAR%</c> from being re-interpreted as
/// shell syntax. Arguments reach windeskctl from callers that did not write them, so a shell in
/// the path is a command-injection surface, not a quoting inconvenience.
/// </remarks>
public static class CommandLine
{
    private static readonly char[] NeedsQuoting = [' ', '\t', '\n', '\v', '"'];

    /// <summary>
    /// Joins a program path and its arguments into one command line. The program goes first and
    /// is quoted by the same rules, so a path through "Program Files" survives.
    /// </summary>
    public static string Build(string program, IReadOnlyList<string> arguments)
    {
        StringBuilder line = new();
        Append(line, program);

        foreach (string argument in arguments)
        {
            line.Append(' ');
            Append(line, argument);
        }

        return line.ToString();
    }

    private static void Append(StringBuilder line, string argument)
    {
        // An empty argument still has to reach the program, and the only way to express one is a
        // pair of quotes — the unquoted form would vanish in the split.
        if (argument.Length > 0 && argument.IndexOfAny(NeedsQuoting) < 0)
        {
            line.Append(argument);
            return;
        }

        line.Append('"');

        for (int i = 0; i < argument.Length; i++)
        {
            int backslashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                i++;
                backslashes++;
            }

            if (i == argument.Length)
            {
                // Trailing backslashes precede the closing quote, so they become escapes unless
                // doubled. This is the case that silently merges two arguments when missed.
                line.Append('\\', backslashes * 2);
                break;
            }

            if (argument[i] == '"')
            {
                line.Append('\\', (backslashes * 2) + 1).Append('"');
            }
            else
            {
                line.Append('\\', backslashes).Append(argument[i]);
            }
        }

        line.Append('"');
    }
}

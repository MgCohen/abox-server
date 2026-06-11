using System.Text.RegularExpressions;

namespace RemoteAgents.Infrastructure.CommandLine;

public static class AnsiHelpers
{
    private static readonly Regex AnsiPattern = new(
        "\x1b\\[[0-9;?]*[A-Za-z]|\x1b\\]0;[^\x07]*\x07|\x1b[=>]|\x1b\\][^\x07]*\x07|\x1b[PX^_].*?\x1b\\\\|\x1b\\][0-9]+;[^\x07]*\x07",
        RegexOptions.Compiled);

    public static string StripAnsi(string s) => AnsiPattern.Replace(s, "");
}

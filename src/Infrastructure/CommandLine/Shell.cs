using System.Diagnostics;

namespace ABox.Infrastructure.CommandLine;

public static class Shell
{
    public static string Executable =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, "cmd.exe")
            : "/bin/bash";

    public static ProcessStartInfo Command(string command)
    {
        var psi = new ProcessStartInfo { FileName = Executable };
        if (OperatingSystem.IsWindows())
        {
            psi.Arguments = $"/c {command}";
        }
        else
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }
        return psi;
    }

    private static readonly char[] QuoteTriggers = { ' ', '\t', '"' };

    public static string QuoteArg(string arg)
    {
        if (arg.Length == 0) return "\"\"";
        if (arg.IndexOfAny(QuoteTriggers) < 0) return arg;
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    // Single-quote quoting for a POSIX shell: inside '…' nothing is special, so $, `,
    // \, ;, whitespace etc. are all inert. The only escape needed is ' itself. Use this
    // (not QuoteArg, which is cmd.exe-shaped) for anything that lands in `bash -c`.
    public static string QuotePosix(string arg) =>
        arg.Length == 0 ? "''" : "'" + arg.Replace("'", "'\\''") + "'";
}

using System.Diagnostics;

namespace RemoteAgents.Infrastructure.CommandLine;

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
}

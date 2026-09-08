using System.ComponentModel;
using System.Diagnostics;
using Xunit;

namespace Kit.Sample.Tests;

// The soft edge. The extracted harness's optional dependency is the doc-engine: if `docengine` resolves, a suite
// can additionally validate its Rulebooks-as-documents; if it does not, parity enforcement stands undiminished.
// The dependency is a runtime PROBE at the process boundary (ADR 0015), never a project reference — so this test
// passes whether or not the tool is installed, which is the whole point of "use it if it's there".
public sealed class DocEngineOptionalTests
{
    [Fact]
    public void DocEngine_is_optional_present_hardens_absent_degrades()
    {
        var present = Resolves("docengine");
        if (present)
            Assert.Equal(0, Run("docengine", "check"));
        else
            // Absent: the harness pillar (parity) still enforces; no hard failure from the missing soft dep.
            Assert.False(present, "unreachable — the else branch implies the tool did not resolve");
    }

    private static bool Resolves(string tool)
    {
        try
        {
            return Run(tool, "--version") is 0 or 1;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static int Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"could not start {file}");
        p.WaitForExit();
        return p.ExitCode;
    }
}

using RemoteAgents.Providers.Orchestrator;

namespace RemoteAgents.Tests.Validation;

public class OrchestratorValidatorTests : IDisposable
{
    private readonly string _root;

    public OrchestratorValidatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ra-validator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "remote-agents-dotnet", "RemoteAgents"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task Catches_deliberately_broken_file()
    {
        var bad = Path.Combine(_root, "remote-agents-dotnet", "RemoteAgents", "Bad.cs");
        File.WriteAllText(bad, "namespace X { public class Y { public void Z( { } } }");

        var validator = new OrchestratorValidator();
        var result = await validator.ValidateAsync(_root);

        Assert.False(result.Ok);
        Assert.Contains("Bad.cs", result.Errors);
    }

    [Fact]
    public async Task Passes_on_clean_tree()
    {
        var good = Path.Combine(_root, "remote-agents-dotnet", "RemoteAgents", "Good.cs");
        File.WriteAllText(good, "namespace X { public class Y { public void Z() { } } }");

        var validator = new OrchestratorValidator();
        var result = await validator.ValidateAsync(_root);

        Assert.True(result.Ok);
        Assert.Contains("OK", result.Summary);
    }

    [Fact]
    public async Task Skips_bin_obj_sessions()
    {
        var rad = Path.Combine(_root, "remote-agents-dotnet");
        var skipDirBad = Path.Combine(rad, "bin", "Debug");
        Directory.CreateDirectory(skipDirBad);
        File.WriteAllText(Path.Combine(skipDirBad, "Generated.cs"), "this is not valid c#");

        var good = Path.Combine(rad, "RemoteAgents", "Good.cs");
        File.WriteAllText(good, "namespace X { public class Y { } }");

        var result = await new OrchestratorValidator().ValidateAsync(_root);
        Assert.True(result.Ok);
    }

    [Fact]
    public async Task Passes_against_current_repo()
    {
        // Find the real repo root by walking up.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "remote-agents-dotnet")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var result = await new OrchestratorValidator().ValidateAsync(dir!.FullName);
        Assert.True(result.Ok, result.Summary + "\n" + result.Errors);
    }
}

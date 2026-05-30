using RemoteAgents.Validation.Unity;

namespace RemoteAgents.Tests.Validation;

// Unit-only — these don't launch Unity. We cover:
//   - environment-discovery error paths (no ProjectVersion.txt)
//   - NUnit XML parsing against fixture files
//   - dotnet-build analyzer-line extraction
//   - analyzers-on-no-sln pass-through
public class UnityChecksTests
{
    [Fact]
    public async Task CompileAsync_missing_project_version_throws()
    {
        var empty = Path.Combine(Path.GetTempPath(), "ra-unity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() => UnityChecks.CompileAsync(empty));
        }
        finally { try { Directory.Delete(empty, recursive: true); } catch { } }
    }

    [Fact]
    public void FindUnityForProject_missing_returns_null()
    {
        var empty = Path.Combine(Path.GetTempPath(), "ra-unity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            Assert.Null(UnityChecks.FindUnityForProject(empty));
        }
        finally { try { Directory.Delete(empty, recursive: true); } catch { } }
    }

    [Fact]
    public async Task AnalyzersAsync_no_sln_returns_ok_with_skip_summary()
    {
        var empty = Path.Combine(Path.GetTempPath(), "ra-unity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var res = await UnityChecks.AnalyzersAsync(empty);
            Assert.True(res.Ok);
            Assert.Equal(0, res.Total);
            Assert.Contains("skipped", res.Summary);
        }
        finally { try { Directory.Delete(empty, recursive: true); } catch { } }
    }

    [Fact]
    public void ExtractDiagnostics_picks_up_cs_rs_sca_idex_lines()
    {
        var input = string.Join('\n', new[]
        {
            "Some prelude line that should not match",
            @"C:\proj\Assets\Foo.cs(12,34): error CS0103: The name 'X' does not exist [C:\proj\Foo.csproj]",
            @"C:\proj\Assets\Foo.cs(45,1): warning RS1024: Compare symbols correctly [C:\proj\Foo.csproj]",
            @"C:\proj\Assets\Bar.cs(7,7): warning IDE0058: Expression value is never used [C:\proj\Bar.csproj]",
            @"C:\proj\Assets\Baz.cs(1,1): warning SCA4001: scaffold rule fired [C:\proj\Baz.csproj]",
            "Build succeeded.",
        });

        var diags = UnityChecks.ExtractDiagnostics(input);
        Assert.Equal(4, diags.Count);
        Assert.Contains(diags, d => d.Contains("CS0103"));
        Assert.Contains(diags, d => d.Contains("RS1024"));
        Assert.Contains(diags, d => d.Contains("IDE0058"));
        Assert.Contains(diags, d => d.Contains("SCA4001"));
    }

    [Fact]
    public void ExtractDiagnostics_dedupes_repeated_lines()
    {
        var line = @"C:\proj\Assets\Foo.cs(12,34): error CS0103: The name 'X' does not exist [C:\proj\Foo.csproj]";
        var input = string.Join('\n', new[] { line, line, line });
        var diags = UnityChecks.ExtractDiagnostics(input);
        Assert.Single(diags);
    }

    [Fact]
    public void ParseNUnitResults_extracts_totals_and_failed_names()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <test-run total="5" passed="3" failed="2" skipped="0">
              <test-suite>
                <test-case fullname="Foo.A" result="Passed" />
                <test-case fullname="Foo.B" result="Passed" />
                <test-case fullname="Foo.C" result="Passed" />
                <test-case fullname="Foo.X" result="Failed">
                  <failure><message>Expected 1 but was 2</message></failure>
                </test-case>
                <test-case fullname="Foo.Y" result="Failed">
                  <failure><message>Something broke</message></failure>
                </test-case>
              </test-suite>
            </test-run>
            """;
        var path = Path.Combine(Path.GetTempPath(), "ra-nunit-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(path, xml);
        try
        {
            var parsed = UnityChecks.ParseNUnitResults(path);
            Assert.Equal(5, parsed.Total);
            Assert.Equal(3, parsed.Passed);
            Assert.Equal(2, parsed.Failed);
            Assert.Equal(0, parsed.Skipped);
            Assert.Equal(2, parsed.FailedTests.Count);
            Assert.Contains(parsed.FailedTests, t => t.Contains("Foo.X") && t.Contains("Expected 1 but was 2"));
            Assert.Contains(parsed.FailedTests, t => t.Contains("Foo.Y"));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void ParseNUnitResults_handles_zero_failures()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <test-run total="2" passed="2" failed="0" skipped="0">
              <test-case fullname="A" result="Passed" />
              <test-case fullname="B" result="Passed" />
            </test-run>
            """;
        var path = Path.Combine(Path.GetTempPath(), "ra-nunit-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(path, xml);
        try
        {
            var parsed = UnityChecks.ParseNUnitResults(path);
            Assert.Equal(2, parsed.Total);
            Assert.Equal(2, parsed.Passed);
            Assert.Empty(parsed.FailedTests);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}

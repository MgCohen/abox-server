using RemoteAgents.Validation.Dotnet;

namespace RemoteAgents.Tests.Providers;

public class DotnetValidatorTests
{
    [Fact]
    public async Task Validate_explicit_target_missing_reports_clean_summary()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ra-dotnet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var validator = new DotnetValidator(new DotnetValidatorOptions(Target: "Missing.csproj"));
            var res = await validator.ValidateAsync(tmp);
            Assert.False(res.Ok);
            Assert.Contains("not found", res.Summary);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Validate_ambiguous_target_reports_clean_summary()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ra-dotnet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tmp, "a.slnx"), "<Solution />");
            await File.WriteAllTextAsync(Path.Combine(tmp, "b.slnx"), "<Solution />");

            var validator = new DotnetValidator();
            var res = await validator.ValidateAsync(tmp);
            Assert.False(res.Ok);
            Assert.Contains("ambiguous", res.Summary);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }
}

using RemoteAgents.Validation.Unity;

namespace RemoteAgents.Tests.Validation;

public class UnityBatchValidatorTests
{
    [Fact]
    public void Default_constructor_leaves_path_null_for_per_project_discovery()
    {
        var validator = new UnityBatchValidator();
        Assert.Null(validator.UnityExePath);
    }

    [Fact]
    public void Explicit_path_is_preserved()
    {
        var fakePath = @"C:\fake\Unity.exe";
        var validator = new UnityBatchValidator(fakePath);
        Assert.Equal(fakePath, validator.UnityExePath);
    }

    [Fact]
    public async Task Missing_project_version_throws_clearly()
    {
        var empty = Path.Combine(Path.GetTempPath(), "ra-unity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var validator = new UnityBatchValidator();
            await Assert.ThrowsAsync<FileNotFoundException>(async () =>
                await validator.ValidateAsync(empty));
        }
        finally { Directory.Delete(empty, recursive: true); }
    }
}

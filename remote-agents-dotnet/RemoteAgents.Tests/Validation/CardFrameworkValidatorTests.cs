using RemoteAgents.Validation.CardFramework;

namespace RemoteAgents.Tests.Validation;

public class CardFrameworkValidatorTests
{
    [Fact]
    public void Auto_discovers_unity_under_hub_when_available()
    {
        // This test only runs meaningfully on a machine with Unity Hub.
        // If the hub root doesn't exist, skip silently — the validator is
        // Windows / Unity-Hub-specific by design.
        var hubRoot = @"C:\Program Files\Unity\Hub\Editor";
        if (!Directory.Exists(hubRoot))
        {
            // No Hub on this machine — auto-discovery should throw.
            Assert.Throws<FileNotFoundException>(() => new CardFrameworkValidator());
            return;
        }

        var validator = new CardFrameworkValidator();
        Assert.True(File.Exists(validator.UnityExePath), $"discovered path doesn't exist: {validator.UnityExePath}");
        Assert.EndsWith("Unity.exe", validator.UnityExePath);
    }

    [Fact]
    public void Explicit_path_overrides_auto_discovery()
    {
        var fakePath = @"C:\fake\Unity.exe";
        var validator = new CardFrameworkValidator(fakePath);
        Assert.Equal(fakePath, validator.UnityExePath);
    }
}

using ABox.Domain.Agents;

namespace ABox.Tests.Unit.Tests;

public class SandboxSettingsTests
{
    [Rule("A credentialed box with no confining network or proxy → refused before it opens")]
    [Theory]
    [InlineData(null, "http://abox-egress-proxy:8888")]
    [InlineData("abox-boxnet", null)]
    [InlineData(null, null)]
    public void A_credentialed_box_without_confinement_is_refused(string? network, string? proxy)
    {
        var settings = new SandboxSettings("abox-claude:latest", network, proxy, SetupToken: "tok-123");

        Assert.Throws<InvalidOperationException>(settings.EnsureCredentialConfined);
    }

    [Rule("A credentialed box behind the egress network and proxy → permitted to open")]
    [Fact]
    public void A_credentialed_box_behind_egress_is_permitted()
    {
        var settings = new SandboxSettings(
            "abox-claude:latest", "abox-boxnet", "http://abox-egress-proxy:8888", SetupToken: "tok-123");

        Assert.Null(Record.Exception(settings.EnsureCredentialConfined));
    }

    [Rule("A box with no credential → permitted to open without egress confinement")]
    [Fact]
    public void An_unbilled_box_needs_no_confinement()
    {
        var settings = new SandboxSettings("abox-claude:latest");

        Assert.Null(Record.Exception(settings.EnsureCredentialConfined));
    }
}

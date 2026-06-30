namespace ABox.Governance.Hooks.Tests.Unit;

public sealed class HookCatalogTests
{
    [Rule("HookCatalog discovers .hook files under its scan roots and reports a malformed one instead of throwing")]
    [Fact]
    public void Discovers_valid_hooks_and_reports_malformed()
    {
        var root = Directory.CreateTempSubdirectory("hookcat-").FullName;
        try
        {
            var feat = Directory.CreateDirectory(Path.Combine(root, "feat")).FullName;
            File.WriteAllText(Path.Combine(feat, "good.hook"), "on: [TurnEnded]\nrun: echo hi\n");
            File.WriteAllText(Path.Combine(feat, "bad.hook"), "mode: notify\n");

            var reports = new List<string>();
            var manifests = new HookCatalog([root], reports.Add).Scan();

            Assert.Single(manifests);
            Assert.Equal(new HookAction.Run("echo hi"), manifests[0].Action);
            Assert.Single(reports);
            Assert.Contains("bad.hook", reports[0]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

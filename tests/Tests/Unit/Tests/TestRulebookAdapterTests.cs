using ABox.Domain.Agents.Judging;

namespace ABox.Tests.Unit.Tests;

public class TestRulebookAdapterTests
{
    [Rule("TestRulebookAdapter given a test path → a JudgeRequest with rubric criteria and labeled context")]
    [Fact]
    public void Adapt_builds_request_with_labeled_context_and_rubric()
    {
        var testPath = Path.Combine("a", "Unit", "Tests", "FooTests.cs");
        var rulebookDir = TestRulebookAdapter.RulebookDir(testPath);
        var files = new Dictionary<string, string>
        {
            [testPath] = "TEST BODY",
            [Path.Combine(rulebookDir, "rules.md")] = "RULES",
            [Path.Combine(rulebookDir, "template.md")] = "TEMPLATE",
        };
        var adapter = new TestRulebookAdapter(p => files[p]);

        var request = adapter.Adapt(testPath);

        Assert.Contains("## Test under review", request.Context);
        Assert.Contains("TEST BODY", request.Context);
        Assert.Contains("## Rulebook", request.Context);
        Assert.Contains("RULES", request.Context);
        Assert.Equal(["cites_rule", "namespace", "derived", "faithful"], request.Criteria.Select(c => c.Id));
        Assert.Equal([testPath], request.Files);
    }

    [Rule("TestRulebookAdapter given a test path → resolves the sibling Rulebook folder")]
    [Fact]
    public void RulebookDir_resolves_sibling_of_the_type_folder()
    {
        var dir = TestRulebookAdapter.RulebookDir(Path.Combine("a", "Unit", "Tests", "FooTests.cs"));

        Assert.Equal(Path.Combine("a", "Unit", "Rulebook"), dir);
    }
}

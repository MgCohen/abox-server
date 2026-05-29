using RemoteAgents.Primitives;

namespace RemoteAgents.Tests.Primitives;

public class DiffBudgetTests
{
    private const string TwoFileDiff =
        "diff --git a/one.cs b/one.cs\n" +
        "index 111..222 100644\n" +
        "--- a/one.cs\n" +
        "+++ b/one.cs\n" +
        "@@ -1,1 +1,1 @@\n" +
        "-old\n" +
        "+new\n" +
        "diff --git a/two.cs b/two.cs\n" +
        "index 333..444 100644\n" +
        "--- a/two.cs\n" +
        "+++ b/two.cs\n" +
        "@@ -1,1 +1,1 @@\n" +
        "-old2\n" +
        "+new2\n";

    [Fact]
    public void Trim_within_budget_returns_input_intact()
    {
        var res = DiffBudget.Trim(TwoFileDiff);
        Assert.False(res.Truncated);
        Assert.Equal(2, res.FilesIncluded);
        Assert.Equal(0, res.FilesElided);
        Assert.Contains("one.cs", res.Diff);
        Assert.Contains("two.cs", res.Diff);
    }

    [Fact]
    public void Trim_max_files_elides_extras()
    {
        var res = DiffBudget.Trim(TwoFileDiff, new DiffBudgetOptions(MaxFiles: 1));
        Assert.True(res.Truncated);
        Assert.Equal(1, res.FilesIncluded);
        Assert.Equal(1, res.FilesElided);
        Assert.Contains("one.cs", res.Diff);
        Assert.DoesNotContain("+new2", res.Diff);
        Assert.Contains("1 more file elided", res.Diff);
    }

    [Fact]
    public void Trim_per_file_line_cap_keeps_header_truncates_hunks()
    {
        // Big hunk: 200 +/- lines after the @@.
        var big = new System.Text.StringBuilder();
        big.Append("diff --git a/big.cs b/big.cs\n");
        big.Append("index aaa..bbb 100644\n");
        big.Append("--- a/big.cs\n");
        big.Append("+++ b/big.cs\n");
        big.Append("@@ -1,200 +1,200 @@\n");
        for (var i = 0; i < 200; i++) big.Append($"-line{i}\n");
        for (var i = 0; i < 200; i++) big.Append($"+line{i}-new\n");

        var res = DiffBudget.Trim(big.ToString(), new DiffBudgetOptions(MaxLinesPerFile: 50));
        Assert.True(res.Truncated);
        Assert.Equal(1, res.FilesIncluded);
        Assert.Contains("diff --git a/big.cs", res.Diff);
        Assert.Contains("@@ -1,200 +1,200 @@", res.Diff);
        Assert.Contains("hunk lines elided", res.Diff);
    }

    [Fact]
    public void Trim_empty_diff_returns_empty()
    {
        var res = DiffBudget.Trim("");
        Assert.Equal(0, res.FilesIncluded);
        Assert.False(res.Truncated);
    }
}

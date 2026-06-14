using ABox.Domain.Projects;

namespace ABox.Tests.Unit.Tests;

public sealed class ProjectTests
{
    [Rule("Project.Create mints a project with a trimmed, non-blank name")]
    [Fact]
    public void Create_trims_the_name_and_assigns_an_id()
    {
        var project = Project.Create("  Card Framework  ");

        Assert.Equal("Card Framework", project.Name);
        Assert.NotEqual(Guid.Empty, project.Id);
    }

    [Theory]
    [Rule("Project.Create mints a project with a trimmed, non-blank name")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name) =>
        Assert.Throws<ArgumentException>(() => Project.Create(name));

    [Rule("Project.Rename returns a renamed project with a trimmed, non-blank name")]
    [Fact]
    public void Rename_trims_the_name_and_keeps_the_id()
    {
        var original = Project.Create("Scaffold");

        var renamed = original.Rename("  Card Framework ");

        Assert.Equal("Card Framework", renamed.Name);
        Assert.Equal(original.Id, renamed.Id);
    }

    [Theory]
    [Rule("Project.Rename returns a renamed project with a trimmed, non-blank name")]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_rejects_a_blank_name(string name)
    {
        var project = Project.Create("Scaffold");

        Assert.Throws<ArgumentException>(() => project.Rename(name));
    }
}

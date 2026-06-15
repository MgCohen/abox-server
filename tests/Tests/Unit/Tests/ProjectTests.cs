using ABox.Domain.Projects;

namespace ABox.Tests.Unit.Tests;

public sealed class ProjectTests
{
    [Rule("Project.Create → a project with a trimmed, non-blank name")]
    [Fact]
    public void Create_trims_the_name_and_assigns_an_id()
    {
        var project = Project.Create("  Card Framework  ", "C:/work/cards");

        Assert.Equal("Card Framework", project.Name);
        Assert.NotEqual(Guid.Empty, project.Id);
    }

    [Theory]
    [Rule("Project.Create → a project with a trimmed, non-blank name")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name) =>
        Assert.Throws<ArgumentException>(() => Project.Create(name, "C:/work/cards"));

    [Rule("Project.Create → a project whose required path is stored absolute")]
    [Fact]
    public void Create_absolutizes_a_relative_path()
    {
        var project = Project.Create("Cards", "work/cards");

        Assert.True(Path.IsPathRooted(project.Path));
        Assert.Equal(Path.GetFullPath("work/cards"), project.Path);
    }

    [Theory]
    [Rule("Project.Create → a project whose required path is stored absolute")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_path(string path) =>
        Assert.Throws<ArgumentException>(() => Project.Create("Cards", path));

    [Rule("Project.Rename → a renamed project with a trimmed, non-blank name")]
    [Fact]
    public void Rename_trims_the_name_and_keeps_the_id()
    {
        var original = Project.Create("Scaffold", "C:/work/scaffold");

        var renamed = original.Rename("  Card Framework ");

        Assert.Equal("Card Framework", renamed.Name);
        Assert.Equal(original.Id, renamed.Id);
        Assert.Equal(original.Path, renamed.Path);
    }

    [Theory]
    [Rule("Project.Rename → a renamed project with a trimmed, non-blank name")]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_rejects_a_blank_name(string name)
    {
        var project = Project.Create("Scaffold", "C:/work/scaffold");

        Assert.Throws<ArgumentException>(() => project.Rename(name));
    }

    [Rule("Project.MoveTo → a relocated project with an absolutized path")]
    [Fact]
    public void MoveTo_absolutizes_the_path_and_keeps_id_and_name()
    {
        var original = Project.Create("Cards", "C:/work/old");

        var moved = original.MoveTo("work/new");

        Assert.Equal(Path.GetFullPath("work/new"), moved.Path);
        Assert.Equal(original.Id, moved.Id);
        Assert.Equal(original.Name, moved.Name);
    }

    [Theory]
    [Rule("Project.MoveTo → a relocated project with an absolutized path")]
    [InlineData("")]
    [InlineData("   ")]
    public void MoveTo_rejects_a_blank_path(string path)
    {
        var project = Project.Create("Cards", "C:/work/old");

        Assert.Throws<ArgumentException>(() => project.MoveTo(path));
    }
}

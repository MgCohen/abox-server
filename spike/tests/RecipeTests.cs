using Xunit;

namespace Spike.Tests;

// The recipe tier (M2): a named, parameterized recipe produces a declaration-tier artifact. These
// prove parameterization — the same recipe with different inputs yields different, correct types.
public class RecipeTests
{
    [Fact]
    public void CreateModel_is_parameterized_not_hardcoded()
    {
        var fav = TypeEmitter.Emit(new CreateModel("FavoriteArtist", new Field<Guid>("Id")).Build());
        var playlist = TypeEmitter.Emit(new CreateModel("Playlist", new Field<string>("Name")).Build());

        Assert.Contains("public record FavoriteArtist(Guid Id);", fav);
        Assert.Contains("public record Playlist(string Name);", playlist);
    }

    [Fact]
    public void CreateModel_builds_a_record_artifact()
    {
        var decl = new CreateModel("FavoriteArtist", new Field<Guid>("Id")).Build();

        Assert.IsType<RecordNode>(decl);
        Assert.Equal("FavoriteArtist", decl.Name);
    }

    [Fact]
    public void CreateEnum_builds_a_compiling_enum()
    {
        var code = TypeEmitter.Emit(new CreateEnum("FavoriteSource", "Search", "Profile").Build());

        var type = Runtime.CompileType(code, "FavoriteSource");
        Assert.True(type.IsEnum);
        Assert.Equal(["Search", "Profile"], Enum.GetNames(type));
    }

    [Fact]
    public void Recipe_exposes_catalog_metadata_for_the_matcher()
    {
        IRecipe recipe = new CreateModel("FavoriteArtist");

        Assert.Equal("create-model", recipe.Name);
        Assert.False(string.IsNullOrWhiteSpace(recipe.Description));
    }
}

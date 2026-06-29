using Xunit;

namespace Spike.Tests;

// The declaration tier (M1): a recipe emits a whole owned type. The gate is compile + reflect-shape
// (a bare type has nothing to Run()). These pin the four basic kinds — record/class/struct/enum.
public class DeclarationTests
{
    [Fact]
    public void Record_emits_a_positional_record_with_typed_fields()
    {
        var code = TypeEmitter.Emit(new RecordNode("FavoriteArtist",
            new Field<Guid>("Id"), new Field<string>("ArtistId"), new Field<DateTime>("FavoritedAt")));

        Assert.Equal(Normalize("""
            public record FavoriteArtist(System.Guid Id, System.String ArtistId, System.DateTime FavoritedAt);
            """), Normalize(code));

        var type = Runtime.CompileType(code, "FavoriteArtist");
        Assert.False(type.IsValueType);
        Assert.Equal(["ArtistId", "FavoritedAt", "Id"], Props(type));
    }

    [Fact]
    public void Generic_field_lowers_to_a_named_type_ref()
    {
        Field generic = new Field<Guid>("Id");

        Assert.Equal("Id", generic.Name);
        Assert.Equal(new TypeRef("System.Guid"), generic.Type);
    }

    [Fact]
    public void String_field_names_a_forward_reference_to_a_not_yet_generated_type()
    {
        var code = TypeEmitter.Emit(new ClassNode("FavoriteArtistService",
            new Field("repo", "IFavoriteArtistRepository")));

        Assert.Contains("public IFavoriteArtistRepository repo { get; set; }", code);
    }

    [Fact]
    public void Class_emits_settable_properties()
    {
        var code = TypeEmitter.Emit(new ClassNode("FavoriteArtist",
            new Field<Guid>("Id"), new Field<string>("ArtistId")));

        var type = Runtime.CompileType(code, "FavoriteArtist");
        Assert.False(type.IsValueType);
        Assert.True(type.GetProperty("ArtistId")!.CanWrite);
    }

    [Fact]
    public void Struct_is_a_value_type()
    {
        var code = TypeEmitter.Emit(new StructNode("FavoriteKey",
            new Field<Guid>("UserId"), new Field<string>("ArtistId")));

        var type = Runtime.CompileType(code, "FavoriteKey");
        Assert.True(type.IsValueType);
        Assert.False(type.IsEnum);
        Assert.Equal(["ArtistId", "UserId"], Props(type));
    }

    [Fact]
    public void Enum_carries_named_constants()
    {
        var code = TypeEmitter.Emit(new EnumNode("FavoriteSource", "Search", "Profile", "Recommendation"));

        var type = Runtime.CompileType(code, "FavoriteSource");
        Assert.True(type.IsEnum);
        Assert.Equal(["Search", "Profile", "Recommendation"], Enum.GetNames(type));
    }

    static string[] Props(Type t) => t.GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();

    static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd();
}

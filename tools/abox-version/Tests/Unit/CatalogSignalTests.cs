namespace ABox.Versioning.Tests.Unit;

public sealed class CatalogSignalTests
{
    [Rule("CatalogSignal reports a byte-different or newly-present catalog as changed and an identical or absent pair as unchanged")]
    [Fact]
    public void Changed_tracks_catalog_content()
    {
        var a = WriteTemp("{\"catalogVersion\":\"1\"}");
        var b = WriteTemp("{\"catalogVersion\":\"1\"}");
        var c = WriteTemp("{\"catalogVersion\":\"2\"}");
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.False(CatalogSignal.Changed(a, b));
        Assert.False(CatalogSignal.Changed(null, null));
        Assert.False(CatalogSignal.Changed(missing, missing));
        Assert.True(CatalogSignal.Changed(a, c));
        Assert.True(CatalogSignal.Changed(missing, a));
        Assert.True(CatalogSignal.Changed(a, null));
    }

    [Rule("CatalogSignal lifts a None surface to Additive when the catalog changed and never downgrades a real surface change")]
    [Fact]
    public void Combine_lifts_none_only()
    {
        Assert.Equal(Compat.Additive, CatalogSignal.Combine(Compat.None, catalogChanged: true));
        Assert.Equal(Compat.None, CatalogSignal.Combine(Compat.None, catalogChanged: false));

        Assert.Equal(Compat.Additive, CatalogSignal.Combine(Compat.Additive, catalogChanged: true));
        Assert.Equal(Compat.Breaking, CatalogSignal.Combine(Compat.Breaking, catalogChanged: true));
        Assert.Equal(Compat.Breaking, CatalogSignal.Combine(Compat.Breaking, catalogChanged: false));
    }

    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, content);
        return path;
    }
}

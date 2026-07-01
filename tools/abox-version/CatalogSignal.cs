using System.Security.Cryptography;

namespace ABox.Versioning;

public static class CatalogSignal
{
    public static bool Changed(string? beforePath, string? afterPath)
    {
        if (beforePath is null && afterPath is null) return false;
        return Hash(beforePath) != Hash(afterPath);
    }

    // The catalog is shipped vocabulary, not compiled API, so a change to it never removes a member from
    // the surface — it only ever adds to what a client can render. Classify it Additive, never Breaking; a
    // genuine block/doctype removal is the owner's call to hand-cut a higher bump.
    public static Compat Combine(Compat surface, bool catalogChanged) =>
        surface == Compat.None && catalogChanged ? Compat.Additive : surface;

    private static string? Hash(string? path) =>
        path is not null && File.Exists(path)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
            : null;
}

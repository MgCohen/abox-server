namespace ABox.Versioning;

public sealed record SemVer(int Major, int Minor, int Patch) : IComparable<SemVer>
{
    public static SemVer Parse(string s)
    {
        var core = s.Trim().TrimStart('v', 'V').Split('-', 2)[0].Split('+', 2)[0];
        var p = core.Split('.');
        if (p.Length < 3 ||
            !int.TryParse(p[0], out var ma) || !int.TryParse(p[1], out var mi) || !int.TryParse(p[2], out var pa))
            throw new FormatException($"not a semver: '{s}'");
        return new SemVer(ma, mi, pa);
    }

    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;
        var byMajor = Major.CompareTo(other.Major);
        if (byMajor != 0) return byMajor;
        var byMinor = Minor.CompareTo(other.Minor);
        return byMinor != 0 ? byMinor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public string Tag => $"v{this}";
}

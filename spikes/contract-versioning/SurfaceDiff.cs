namespace ContractVersioning;

public enum ChangeCase { None, AssemblyAdded, AssemblyRemoved, SurfaceChanged, BinaryOnlyChanged }

public sealed record MemberDelta(string Assembly, string What, SurfaceMember Member);

public sealed record DiffReport(
    IReadOnlyList<string> AssembliesAdded,
    IReadOnlyList<string> AssembliesRemoved,
    IReadOnlyList<string> BinaryOnlyChanged,
    IReadOnlyList<MemberDelta> MembersAdded,
    IReadOnlyList<MemberDelta> MembersRemoved,
    IReadOnlyList<MemberDelta> MembersChanged)
{
    public IReadOnlyList<ChangeCase> Cases()
    {
        var cases = new List<ChangeCase>();
        if (AssembliesAdded.Count > 0) cases.Add(ChangeCase.AssemblyAdded);
        if (AssembliesRemoved.Count > 0) cases.Add(ChangeCase.AssemblyRemoved);
        if (MembersAdded.Count + MembersRemoved.Count + MembersChanged.Count > 0) cases.Add(ChangeCase.SurfaceChanged);
        if (BinaryOnlyChanged.Count > 0) cases.Add(ChangeCase.BinaryOnlyChanged);
        return cases.Count == 0 ? [ChangeCase.None] : cases;
    }
}

public static class SurfaceDiff
{
    public static DiffReport Compare(
        IReadOnlyDictionary<string, AssemblySurface> before,
        IReadOnlyDictionary<string, AssemblySurface> after)
    {
        var added = after.Keys.Where(k => !before.ContainsKey(k)).OrderBy(k => k).ToList();
        var removed = before.Keys.Where(k => !after.ContainsKey(k)).OrderBy(k => k).ToList();

        var binaryOnly = new List<string>();
        var memAdded = new List<MemberDelta>();
        var memRemoved = new List<MemberDelta>();
        var memChanged = new List<MemberDelta>();

        foreach (var name in before.Keys.Where(after.ContainsKey).OrderBy(k => k))
        {
            var b = before[name];
            var a = after[name];

            var bByKey = b.Members.ToDictionary(m => m.Key);
            var aByKey = a.Members.ToDictionary(m => m.Key);

            var thisAdded = aByKey.Where(kv => !bByKey.ContainsKey(kv.Key))
                .Select(kv => new MemberDelta(name, "added", kv.Value)).ToList();
            var thisRemoved = bByKey.Where(kv => !aByKey.ContainsKey(kv.Key))
                .Select(kv => new MemberDelta(name, "removed", kv.Value)).ToList();
            var thisChanged = aByKey.Where(kv => bByKey.TryGetValue(kv.Key, out var bm) && bm.Signature != kv.Value.Signature)
                .Select(kv => new MemberDelta(name, $"{bByKey[kv.Key].Signature}  =>  {kv.Value.Signature}", kv.Value)).ToList();

            memAdded.AddRange(thisAdded);
            memRemoved.AddRange(thisRemoved);
            memChanged.AddRange(thisChanged);

            var surfaceSame = thisAdded.Count + thisRemoved.Count + thisChanged.Count == 0;
            if (surfaceSame && b.Hash != a.Hash && a.Members.Count > 0) binaryOnly.Add(name);
        }

        return new DiffReport(added, removed, binaryOnly, memAdded, memRemoved, memChanged);
    }
}

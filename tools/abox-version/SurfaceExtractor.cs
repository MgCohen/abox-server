using System.Reflection;
using System.Security.Cryptography;

namespace ABox.Versioning;

public static class SurfaceExtractor
{
    private static readonly HashSet<string> RecordPlumbing =
        new(StringComparer.Ordinal) { "Equals", "GetHashCode", "ToString", "Deconstruct", "PrintMembers", "<Clone>$" };

    public static IReadOnlyDictionary<string, AssemblySurface> SnapshotDir(string dir)
    {
        var dlls = Directory.GetFiles(dir, "*.Api.dll");
        if (dlls.Length == 0)
            throw new InvalidOperationException($"No *.Api.dll under '{dir}' — build the rollup first (dotnet build src/Api).");

        var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var paths = Directory.GetFiles(coreDir, "*.dll")
            .Concat(Directory.GetFiles(dir, "*.dll"))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        using var mlc = new MetadataLoadContext(new PathAssemblyResolver(paths), "System.Private.CoreLib");

        var result = new Dictionary<string, AssemblySurface>(StringComparer.Ordinal);
        foreach (var dll in dlls.OrderBy(p => p, StringComparer.Ordinal))
        {
            var asm = mlc.LoadFromAssemblyPath(dll);
            var name = asm.GetName().Name!;
            result[name] = new AssemblySurface(name, HashFile(dll), Members(asm).ToList());
        }
        return result;
    }

    private static IEnumerable<SurfaceMember> Members(Assembly asm)
    {
        foreach (var type in asm.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var typeName = type.FullName!;
            yield return new SurfaceMember(typeName, "type", typeName, TypeKind(type));

            if (type.IsEnum)
            {
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    yield return new SurfaceMember(typeName, "enum-value", f.Name, $"{f.Name} = {f.GetRawConstantValue()}");
                continue;
            }

            const BindingFlags pub = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var p in type.GetProperties(pub).OrderBy(p => p.Name, StringComparer.Ordinal))
                yield return new SurfaceMember(typeName, "property", p.Name, $"{Pretty(p.PropertyType)} {p.Name} {{ {Accessors(p)} }}");

            foreach (var f in type.GetFields(pub).Where(f => !f.IsSpecialName).OrderBy(f => f.Name, StringComparer.Ordinal))
                yield return new SurfaceMember(typeName, "field", f.Name, $"{Pretty(f.FieldType)} {f.Name}");

            foreach (var c in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                yield return new SurfaceMember(typeName, "ctor", ".ctor", $".ctor({Parameters(c)})");

            foreach (var m in type.GetMethods(pub).Where(IsSurfaceMethod).OrderBy(m => m.Name, StringComparer.Ordinal))
                yield return new SurfaceMember(typeName, "method", m.Name, $"{Pretty(m.ReturnType)} {m.Name}({Parameters(m)})");
        }
    }

    private static bool IsSurfaceMethod(MethodInfo m) =>
        !m.IsSpecialName && !m.Name.StartsWith('<') && !RecordPlumbing.Contains(m.Name) && m.DeclaringType?.Name != "Object";

    private static string TypeKind(Type t) =>
        t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" : "class";

    private static string Accessors(PropertyInfo p)
    {
        var parts = new List<string>();
        if (p.GetMethod is { IsPublic: true }) parts.Add("get");
        if (p.SetMethod is { IsPublic: true })
            parts.Add(IsInitOnly(p.SetMethod) ? "init" : "set");
        return string.Join("; ", parts);
    }

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static string Parameters(MethodBase m) =>
        string.Join(", ", m.GetParameters().Select(p => $"{Pretty(p.ParameterType)} {p.Name}"));

    private static string Pretty(Type t) =>
        t.IsGenericType
            ? $"{t.Name[..t.Name.IndexOf('`')]}<{string.Join(", ", t.GetGenericArguments().Select(Pretty))}>"
            : t.Name;

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))[..16];
}

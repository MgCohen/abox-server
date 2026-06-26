// Resolve an asmdef's PROJECT-SOURCE closure from Unity's primed .csproj graph.
// Walks <ProjectReference> edges (sibling asmdefs compiled FROM SOURCE) and
// collects <Reference> HintPaths (engine/package binaries), translating Windows
// paths to container paths. Emits closure.props for closure.csproj to import.
//
// Project-source deps -> compiled from source (no prime needed when they change).
// Binary deps (engine baked, packages on mount) -> prime/rebuild only when THEY change.
//
// usage: dotnet run resolve-closure.cs -- <projectRoot> <targetAsm> <outProps>

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var projectRoot = args[0];
var target = args[1];
var outProps = args[2];

var asmdefDir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
foreach (var f in Directory.EnumerateFiles(Path.Combine(projectRoot, "Assets"), "*.asmdef", SearchOption.AllDirectories))
{
    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(f));
        if (doc.RootElement.TryGetProperty("name", out var n))
            asmdefDir[n.GetString()!] = Path.GetDirectoryName(f)!.Replace('\\', '/');
    }
    catch { }
}

var closure = new List<string>();
var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var queue = new Queue<string>();
var hintPaths = new List<string>();
string? defines = null;
string? installRoot = null;
var installRx = new Regex(@"^(?<root>.*[\\/]Editor)[\\/]Data[\\/]", RegexOptions.IgnoreCase);

queue.Enqueue(target);
while (queue.Count > 0)
{
    var name = queue.Dequeue();
    if (!seen.Add(name)) continue;
    closure.Add(name);

    var csproj = Path.Combine(projectRoot, name + ".csproj");
    if (!File.Exists(csproj)) { Console.Error.WriteLine($"[resolve] warn: no csproj for {name} (treated as leaf)"); continue; }

    var xd = XDocument.Load(csproj);
    foreach (var pr in xd.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
    {
        var inc = pr.Attribute("Include")?.Value;
        if (inc != null) queue.Enqueue(Path.GetFileNameWithoutExtension(inc.Replace('\\', '/')));
    }
    foreach (var hp in xd.Descendants().Where(e => e.Name.LocalName == "HintPath"))
    {
        hintPaths.Add(hp.Value);
        if (installRoot == null) { var m = installRx.Match(hp.Value); if (m.Success) installRoot = m.Groups["root"].Value; }
    }
    if (name.Equals(target, StringComparison.OrdinalIgnoreCase))
        defines = xd.Descendants().FirstOrDefault(e => e.Name.LocalName == "DefineConstants")?.Value;
}

bool KeepBinary(string winPath)
{
    var p = winPath.Replace('\\', '/');
    if (p.Contains("/NetStandard/") || p.Contains("/MonoBleedingEdge/") || Regex.IsMatch(p, "/Mono/", RegexOptions.IgnoreCase))
        return false; // BCL facades -> provided by Microsoft.NETFramework.ReferenceAssemblies
    if (p.Contains("/ScriptAssemblies/") && closure.Contains(Path.GetFileNameWithoutExtension(p)))
        return false; // compiled from source in this build, not as a stale binary
    return true;
}

string ToContainer(string winPath)
{
    var p = winPath.Trim();
    if (installRoot != null && p.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
        p = "/unity-editor" + p.Substring(installRoot.Length);
    else if (p.Length > 1 && p[1] == ':')
        p = "/winabs/" + p.Substring(3); // uncommon absolute path outside the editor root
    else
        p = "/project/" + p; // project-relative (Library\..., Assets\...)
    return p.Replace('\\', '/');
}

var refs = hintPaths.Where(KeepBinary).Select(ToContainer)
    .GroupBy(p => p, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
    .OrderBy(p => p).ToList();

var sb = new StringBuilder();
sb.AppendLine("<Project>");
sb.AppendLine("  <PropertyGroup>");
sb.AppendLine($"    <DefineConstants>{defines};$(ExtraDefines)</DefineConstants>");
sb.AppendLine("  </PropertyGroup>");
sb.AppendLine("  <ItemGroup>");
foreach (var name in closure)
    if (asmdefDir.TryGetValue(name, out var dir))
        sb.AppendLine($"    <Compile Include=\"{dir}/**/*.cs\" />");
foreach (var r in refs)
    sb.AppendLine($"    <Reference Include=\"{Path.GetFileNameWithoutExtension(r)}\" Private=\"false\"><HintPath>{r}</HintPath></Reference>");
sb.AppendLine("  </ItemGroup>");
sb.AppendLine("</Project>");
File.WriteAllText(outProps, sb.ToString());

Console.Error.WriteLine($"[resolve] target={target}");
Console.Error.WriteLine($"[resolve] source closure ({closure.Count}): {string.Join(", ", closure)}");
Console.Error.WriteLine($"[resolve] binary refs kept: {refs.Count} (install root: {installRoot ?? "n/a"})");

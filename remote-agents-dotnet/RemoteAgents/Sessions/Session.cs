using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using RemoteAgents.Primitives;

namespace RemoteAgents.Sessions;

public sealed record StartSessionRequest(
    string? ProjectDir,
    string? ProjectName,
    string? UserPrompt,
    string? FlowName);

// A per-run session folder under remote-agents-dotnet/sessions/<id>/.
// Owns three files: prompt.txt, meta.json, transcript.jsonl. Holds no
// running state beyond paths; sinks write transcript entries, the agent
// lifecycle owns meta finalization.
public sealed class Session
{
    private static readonly Regex SlugStrip = new("[^a-z0-9]+", RegexOptions.Compiled);
    private readonly Stopwatch _sw;

    public string Id { get; }
    public string Dir { get; }
    public string PromptFile { get; }
    public string TranscriptFile { get; }
    public string MetaFile { get; }
    public SessionMeta Meta { get; private set; }

    private Session(string id, string dir, SessionMeta meta)
    {
        Id = id;
        Dir = dir;
        PromptFile = Path.Combine(dir, "prompt.txt");
        TranscriptFile = Path.Combine(dir, "transcript.jsonl");
        MetaFile = Path.Combine(dir, "meta.json");
        Meta = meta;
        _sw = Stopwatch.StartNew();
    }

    public static Session Start(StartSessionRequest req, string? sessionsRoot = null)
    {
        sessionsRoot ??= DefaultSessionsRoot();
        Directory.CreateDirectory(sessionsRoot);

        var slug = Slugify(req.FlowName ?? req.ProjectName ?? "run");
        var id = $"{TsForFilename()}-{slug}";
        var dir = Path.Combine(sessionsRoot, id);
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "prompt.txt"), req.UserPrompt ?? "");
        File.WriteAllText(Path.Combine(dir, "transcript.jsonl"), "");

        var meta = new SessionMeta
        {
            Id = id,
            Orchestrator = "csharp",
            SchemaVersion = "1",
            FlowName = req.FlowName,
            ProjectName = req.ProjectName,
            ProjectDir = req.ProjectDir,
            UserPrompt = req.UserPrompt,
            StartedAt = DateTimeOffset.UtcNow,
        };

        var s = new Session(id, dir, meta);
        s.WriteMeta();
        return s;
    }

    public void End(string result, string? failureReason = null)
    {
        Meta.EndedAt = DateTimeOffset.UtcNow;
        Meta.DurationMs = _sw.ElapsedMilliseconds;
        Meta.Result = result;
        Meta.FailureReason = failureReason;
        WriteMeta();
    }

    private void WriteMeta()
    {
        var json = JsonSerializer.Serialize(Meta, SessionJsonContext.Default.SessionMeta);
        File.WriteAllText(MetaFile, json);
    }

    private static string Slugify(string s)
    {
        var lower = s.ToLowerInvariant();
        var stripped = SlugStrip.Replace(lower, "-").Trim('-');
        if (stripped.Length > 40) stripped = stripped[..40];
        return stripped.Length == 0 ? "session" : stripped;
    }

    // 2026-05-28T13-11-42-491Z — safe on Windows (no colons)
    private static string TsForFilename() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ss-fffZ");

    private static string DefaultSessionsRoot()
    {
        // First look for the orchestrator dir as a sibling (the typical
        // case when invoked from the repo root). If none, fall back to
        // CWD/sessions/ — only happens in detached test harnesses.
        var root = RepoRoot.Find("remote-agents-dotnet");
        return root is null
            ? Path.Combine(Environment.CurrentDirectory, "sessions")
            : Path.Combine(root, "remote-agents-dotnet", "sessions");
    }
}

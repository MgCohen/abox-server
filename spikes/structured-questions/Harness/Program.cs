using System.Diagnostics;
using System.Text;
using System.Text.Json;
using StructuredQuestions;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

if (command is "help" or "-h" or "--help")
{
    Console.WriteLine(
        """
        Structured-questions spike harness.

          Harness selftest
              Run the parser fixture suite (no agents, no tokens).

          Harness partA  [--providers claude,codex] [--prompts open-1,...] [--n 1] [--tag run] [--timeout 240]
              Emission-reliability matrix (claims C1/C2). New turn per run, scored vs prompts.json.

          Harness partB  [--providers claude,codex] [--prompts choice-1,open-1] [--tag continuity] [--timeout 240]
              Session-continuity (claim C3). turn1 -> hand-written answer -> resume.

        Outputs land under out/<tag>/ : raw captures, final texts, results.json, summary.md.
        """);
    return 0;
}

if (command == "selftest")
    return SelfTest.Run();

var opts = ParseOptions(args);
var root = FindSpikeRoot();
var prompts = LoadPrompts(Path.Combine(root, "prompts.json"));
var selectedIds = opts.Prompts ?? prompts.Select(p => p.Id).ToArray();
var corpus = prompts.Where(p => selectedIds.Contains(p.Id)).ToList();
if (corpus.Count == 0) { Console.Error.WriteLine("No matching prompts."); return 1; }

var providers = opts.Providers ?? ["claude", "codex"];
var tag = opts.Tag ?? command;
var outDir = Path.Combine(root, "out", tag);
Directory.CreateDirectory(outDir);

var runner = new Runner(root, outDir, opts.TimeoutSeconds);

Console.WriteLine($"Spike root : {root}");
Console.WriteLine($"Providers  : {string.Join(", ", providers)}");
Console.WriteLine($"Prompts    : {string.Join(", ", corpus.Select(p => p.Id))}");
Console.WriteLine($"N          : {opts.N}");
Console.WriteLine($"Out        : {outDir}\n");

var records = new List<RunRecord>();

if (command == "parta")
{
    foreach (var provider in providers)
        foreach (var spec in corpus)
            for (var i = 1; i <= opts.N; i++)
            {
                Console.WriteLine($"[A] {provider}/{spec.Id} #{i} ...");
                var rec = runner.RunTurn1(provider, spec, i);
                records.Add(rec);
                Console.WriteLine($"    -> {Summarize(rec)}");
            }

    File.WriteAllText(Path.Combine(outDir, "results.json"),
        JsonSerializer.Serialize(records, JsonOpts()));
    File.WriteAllText(Path.Combine(outDir, "summary.md"), Report.PartA(records, opts.N));
    Console.WriteLine($"\nWrote results.json + summary.md to {outDir}");
    return 0;
}

if (command == "partb")
{
    var continuity = new List<ContinuityRecord>();
    foreach (var provider in providers)
        foreach (var spec in corpus)
        {
            Console.WriteLine($"[B] {provider}/{spec.Id} turn1 ...");
            var t1 = runner.RunTurn1(provider, spec, 1);
            records.Add(t1);
            Console.WriteLine($"    turn1 -> {Summarize(t1)}");

            if (!t1.Asked || t1.SessionId.Length == 0)
            {
                Console.WriteLine("    (no question or no session id; skipping resume)");
                continuity.Add(new ContinuityRecord(provider, spec.Id, t1.ParsedKind, "(n/a)", "(n/a)", false, t1.FinalText, ""));
                continue;
            }

            var answer = AnswerFor(spec, t1);
            Console.WriteLine($"    answering: {answer}");
            var t2 = runner.RunResume(provider, spec, t1.SessionId, answer);
            var askedAgain = QuestionParser.TryParse(t2.FinalText) is not null;
            continuity.Add(new ContinuityRecord(
                provider, spec.Id, t1.ParsedKind, t1.QuestionPrompt, answer,
                !askedAgain, t1.FinalText, t2.FinalText));
            Console.WriteLine($"    turn2 -> askedAgain={askedAgain}");
        }

    File.WriteAllText(Path.Combine(outDir, "results.json"),
        JsonSerializer.Serialize(records, JsonOpts()));
    File.WriteAllText(Path.Combine(outDir, "continuity.json"),
        JsonSerializer.Serialize(continuity, JsonOpts()));
    File.WriteAllText(Path.Combine(outDir, "summary.md"), Report.PartB(continuity));
    Console.WriteLine($"\nWrote continuity.json + summary.md to {outDir}");
    return 0;
}

Console.Error.WriteLine($"Unknown command: {command}");
return 1;

static string Summarize(RunRecord r)
{
    if (r.Error is not null) return $"ERROR exit={r.DriverExit} timedOut={r.TimedOut} :: {r.Error}";
    var kind = r.Asked ? (r.ParsedKind ?? "?") : "none";
    var verdict = r.StatusCorrect && r.KindCorrect ? "ok" : "MISS";
    return $"asked={r.Asked} kind={kind} parsed={r.Parsed} degraded={r.Degraded} [{verdict}] ({r.DurationMs}ms)";
}

static string AnswerFor(PromptSpec spec, RunRecord t1)
{
    if (t1.ParsedKind == "choice" && t1.ChoiceOptions is { Count: > 0 })
        return t1.ChoiceOptions[0];
    return spec.Id switch
    {
        "open-1" => "Use s3://acme-prod-artifacts in us-east-1.",
        "open-2" => "Use signing key 0xA1B2C3D4 from the team keyring.",
        _ => "Use the first sensible default and proceed.",
    };
}

static Options ParseOptions(string[] argv)
{
    string[]? providers = null, prompts = null;
    string? tag = null;
    var n = 1;
    var timeout = 240;
    for (var i = 1; i < argv.Length; i++)
    {
        switch (argv[i])
        {
            case "--providers": providers = argv[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
            case "--prompts": prompts = argv[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
            case "--tag": tag = argv[++i]; break;
            case "--n": n = int.Parse(argv[++i]); break;
            case "--timeout": timeout = int.Parse(argv[++i]); break;
        }
    }
    return new Options(providers, prompts, tag, n, timeout);
}

static string FindSpikeRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Directive.txt"))
            && File.Exists(Path.Combine(dir.FullName, "prompts.json")))
            return dir.FullName;
        dir = dir.Parent;
    }
    var cwd = Directory.GetCurrentDirectory();
    if (File.Exists(Path.Combine(cwd, "Directive.txt"))) return cwd;
    throw new InvalidOperationException("Could not locate spike root (Directive.txt + prompts.json).");
}

static List<PromptSpec> LoadPrompts(string path)
    => JsonSerializer.Deserialize<List<PromptSpec>>(File.ReadAllText(path),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
       ?? throw new InvalidOperationException("prompts.json failed to parse.");

static JsonSerializerOptions JsonOpts() => new() { WriteIndented = true };

record Options(string[]? Providers, string[]? Prompts, string? Tag, int N, int TimeoutSeconds);

record PromptSpec(string Id, string Prompt, string ExpectedStatus, string? ExpectedKind, string Note);

record RunRecord(
    string Provider, string PromptId, int Iteration,
    string ExpectedStatus, string? ExpectedKind,
    bool Asked, string? ParsedKind, IReadOnlyList<string>? ChoiceOptions,
    bool Parsed, bool Degraded,
    bool StatusCorrect, bool KindCorrect, bool FalsePositive, bool FreeformOrHang,
    string SessionId, int DriverExit, bool TimedOut, string? Error,
    double? CostUsd, long DurationMs, string QuestionPrompt, string RawTail, string FinalText);

record ContinuityRecord(
    string Provider, string PromptId, string? Turn1Kind,
    string Turn1Question, string AnswerGiven, bool KeptContextHeuristic,
    string Turn1Text, string Turn2Text);

sealed class Runner(string root, string outDir, int timeoutSeconds)
{
    private readonly string _directive = Path.Combine(root, "Directive.txt");
    private readonly string _template = Path.Combine(root, "sandbox-template");
    private readonly string _runClaude = Path.Combine(root, "RunClaude.ps1");
    private readonly string _runCodex = Path.Combine(root, "RunCodex.ps1");

    public RunRecord RunTurn1(string provider, PromptSpec spec, int iter)
    {
        var stem = $"{provider}-{spec.Id}-{iter}";
        var sandbox = ResetSandbox(stem);
        var sw = Stopwatch.StartNew();
        var (finalText, sessionId, exit, timedOut, error, cost) =
            provider == "claude"
                ? DriveClaude(spec.Prompt, sandbox, stem, sessionId: Guid.NewGuid().ToString(), resume: false)
                : DriveCodex(spec.Prompt, sandbox, stem, resumeId: null);
        sw.Stop();
        return Score(provider, spec, iter, finalText, sessionId, exit, timedOut, error, cost, sw.ElapsedMilliseconds);
    }

    public RunRecord RunResume(string provider, PromptSpec spec, string sessionId, string answer)
    {
        var stem = $"{provider}-{spec.Id}-resume";
        var sandbox = Path.Combine(outDir, "sandbox", $"{provider}-{spec.Id}-1");
        if (!Directory.Exists(sandbox)) sandbox = ResetSandbox(stem);
        var sw = Stopwatch.StartNew();
        var (finalText, sid, exit, timedOut, error, cost) =
            provider == "claude"
                ? DriveClaude(answer, sandbox, stem, sessionId: sessionId, resume: true)
                : DriveCodex(answer, sandbox, stem, resumeId: sessionId);
        sw.Stop();
        return Score(provider, spec, 0, finalText, sid, exit, timedOut, error, cost, sw.ElapsedMilliseconds);
    }

    private string ResetSandbox(string stem)
    {
        var dest = Path.Combine(outDir, "sandbox", stem);
        if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
        CopyDir(_template, dest);
        return dest;
    }

    private (string finalText, string sessionId, int exit, bool timedOut, string? error, double? cost)
        DriveClaude(string prompt, string sandbox, string stem, string sessionId, bool resume)
    {
        var outFile = Path.Combine(outDir, $"{stem}.{(resume ? "turn2" : "turn1")}.json");
        var pArgs = new List<string>
        {
            "-NoProfile", "-File", _runClaude,
            "-Prompt", prompt, "-Project", sandbox, "-OutFile", outFile,
        };
        if (resume) { pArgs.Add("-SessionId"); pArgs.Add(sessionId); pArgs.Add("-Resume"); }
        else { pArgs.Add("-SessionId"); pArgs.Add(sessionId); pArgs.Add("-DirectiveFile"); pArgs.Add(_directive); }

        var (exit, _, _, timedOut) = RunPwsh(pArgs);
        if (timedOut) return ("", resume ? sessionId : sessionId, exit, true, "timeout", null);

        if (!File.Exists(outFile) || new FileInfo(outFile).Length == 0)
            return ("", sessionId, exit, false, ReadErr(outFile) ?? "no output", null);

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(outFile));
            var r = doc.RootElement;
            var result = Prop(r, "result")?.GetString() ?? "";
            var sid = Prop(r, "session_id")?.GetString() ?? sessionId;
            var isError = Prop(r, "is_error")?.ValueKind == JsonValueKind.True;
            double? cost = Prop(r, "total_cost_usd")?.GetDouble();
            File.WriteAllText(Path.Combine(outDir, $"{stem}.{(resume ? "turn2" : "turn1")}.final.txt"), result);
            return (result, sid, exit, false, isError ? $"claude is_error: {result}" : null, cost);
        }
        catch (JsonException)
        {
            return ("", sessionId, exit, false, $"non-JSON claude output: {Head(File.ReadAllText(outFile))}", null);
        }
    }

    private (string finalText, string sessionId, int exit, bool timedOut, string? error, double? cost)
        DriveCodex(string prompt, string sandbox, string stem, string? resumeId)
    {
        var resume = resumeId is not null;
        var lastFile = Path.Combine(outDir, $"{stem}.{(resume ? "turn2" : "turn1")}.last.txt");
        var eventsFile = Path.Combine(outDir, $"{stem}.{(resume ? "turn2" : "turn1")}.events.jsonl");
        var pArgs = new List<string>
        {
            "-NoProfile", "-File", _runCodex,
            "-Prompt", prompt, "-Project", sandbox,
            "-LastFile", lastFile, "-EventsFile", eventsFile,
        };
        if (resume) { pArgs.Add("-ResumeId"); pArgs.Add(resumeId!); }
        else { pArgs.Add("-DirectiveFile"); pArgs.Add(_directive); }

        var (exit, _, _, timedOut) = RunPwsh(pArgs);
        if (timedOut) return ("", "", exit, true, "timeout", null);

        var finalText = File.Exists(lastFile) ? File.ReadAllText(lastFile) : "";
        var sessionId = File.Exists(eventsFile) ? ScanCodexSessionId(eventsFile) : "";
        string? error = null;
        if (finalText.Length == 0)
            error = ReadErr(eventsFile) ?? "no last-message output";
        if (finalText.Length > 0)
            File.WriteAllText(Path.Combine(outDir, $"{stem}.{(resume ? "turn2" : "turn1")}.final.txt"), finalText);
        return (finalText, sessionId, exit, false, error, null);
    }

    private RunRecord Score(
        string provider, PromptSpec spec, int iter,
        string finalText, string sessionId, int exit, bool timedOut, string? error, double? cost, long ms)
    {
        var diag = QuestionParser.Diagnose(finalText);
        var asked = diag.Question is not null;
        var kind = diag.Question switch
        {
            AgentQuestion.Choice => "choice",
            AgentQuestion.Open => "open",
            _ => (string?)null,
        };
        var options = (diag.Question as AgentQuestion.Choice)?.Options;
        var expectsNeedsInput = spec.ExpectedStatus.Equals("NeedsInput", StringComparison.OrdinalIgnoreCase);
        var statusCorrect = asked == expectsNeedsInput;
        var kindCorrect = expectsNeedsInput ? kind == spec.ExpectedKind : !asked;
        var falsePositive = !expectsNeedsInput && asked;
        var freeformOrHang = expectsNeedsInput && !asked;

        return new RunRecord(
            provider, spec.Id, iter, spec.ExpectedStatus, spec.ExpectedKind,
            asked, kind, options, diag.Parsed, diag.Degraded,
            statusCorrect, kindCorrect, falsePositive, freeformOrHang,
            sessionId, exit, timedOut, error, cost, ms,
            QuestionPrompt: diag.Question?.Prompt ?? "", RawTail: diag.Question?.RawTail ?? "",
            FinalText: finalText);
    }

    private (int exit, string stdout, string stderr, bool timedOut) RunPwsh(List<string> argList)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in argList) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        var so = new StringBuilder();
        var se = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) so.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) se.AppendLine(e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (!p.WaitForExit(timeoutSeconds * 1000))
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            return (-1, so.ToString(), se.ToString(), true);
        }
        p.WaitForExit();
        return (p.ExitCode, so.ToString(), se.ToString(), false);
    }

    private static string ScanCodexSessionId(string eventsFile)
    {
        foreach (var line in File.ReadLines(eventsFile))
        {
            var t = line.Trim();
            if (t.Length == 0 || t[0] != '{') continue;
            try
            {
                using var doc = JsonDocument.Parse(t);
                var found = FindString(doc.RootElement, "session_id") ?? FindString(doc.RootElement, "thread_id");
                if (!string.IsNullOrEmpty(found)) return found;
            }
            catch (JsonException) { }
        }
        return "";
    }

    private static string? FindString(JsonElement e, string key)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in e.EnumerateObject())
                {
                    if (prop.NameEquals(key) && prop.Value.ValueKind == JsonValueKind.String)
                        return prop.Value.GetString();
                    var nested = FindString(prop.Value, key);
                    if (nested is not null) return nested;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                {
                    var nested = FindString(item, key);
                    if (nested is not null) return nested;
                }
                break;
        }
        return null;
    }

    private static JsonElement? Prop(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : null;

    private static string? ReadErr(string outFile)
    {
        var errFile = outFile + ".err";
        if (File.Exists(errFile))
        {
            var txt = File.ReadAllText(errFile).Trim();
            if (txt.Length > 0) return Head(txt);
        }
        return null;
    }

    private static string Head(string s) => s.Length <= 400 ? s : s[..400] + " ...";

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(from, to));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(from, to), overwrite: true);
    }
}

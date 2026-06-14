namespace ABox.Domain.Agents.Judging;

public static class JudgePrompt
{
    public static string Compose(JudgeRequest request)
    {
        var files = request.Files.Count == 0
            ? "(none)"
            : string.Join("\n", request.Files.Select(f => $"- {f}"));
        var criteria = string.Join("\n", request.Criteria.Select((c, i) =>
            $"{i + 1}. [{c.Id}] {c.Description}{(c.HowToCheck is null ? string.Empty : $" — check: {c.HowToCheck}")}"));

        return
            $@"Subject: {request.Subject}

Context (use this first):
{request.Context}

Supporting files (read only if a criterion can't be assessed from the context above):
{files}

Criteria (return exactly one result per id):
{criteria}

When done, emit the sentinel {JudgeParser.Sentinel} on its own line as the LAST thing, then a JSON object:
{{ ""results"": [ {{ ""criterionId"": ""<id>"", ""status"": ""pass|fail|indeterminate"", ""evidence"": ""<file:line or quote>"" }} ] }}
Judge only the criteria above, use each id verbatim, and mark a criterion indeterminate only when the material cannot assess it.";
    }
}

namespace StructuredQuestions;

public static class SelfTest
{
    private record Case(string Name, string Input, Func<ParseDiagnostics, bool> Expect);

    public static int Run()
    {
        var cases = new List<Case>
        {
            new("no-sentinel-null",
                "All done. I added the endpoint and tests pass.",
                d => d.Question is null && !d.SentinelFound),

            new("empty-null",
                "",
                d => d.Question is null),

            new("open-clean",
                "Some preamble.\n<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"Which bucket?\" }",
                d => d is { Parsed: true, Degraded: false, Question: AgentQuestion.Open o } && o.Prompt == "Which bucket?"),

            new("choice-clean",
                "<<NEEDS_INPUT>>\n{ \"kind\": \"choice\", \"prompt\": \"Which TFM?\", \"options\": [\"net8.0\", \"net10.0\"], \"allow_free_text\": false }",
                d => d is { Parsed: true, Degraded: false, Question: AgentQuestion.Choice c }
                     && c.Options.Count == 2 && c.Options[0] == "net8.0" && !c.AllowFreeText),

            new("choice-allow-free-text",
                "<<NEEDS_INPUT>>\n{ \"kind\": \"choice\", \"prompt\": \"Pick\", \"options\": [\"a\"], \"allow_free_text\": true }",
                d => d.Question is AgentQuestion.Choice { AllowFreeText: true }),

            new("fenced-json",
                "<<NEEDS_INPUT>>\n```json\n{ \"kind\": \"open\", \"prompt\": \"What key?\" }\n```",
                d => d is { Parsed: true, Degraded: false, Question: AgentQuestion.Open o } && o.Prompt == "What key?"),

            new("leading-prose-before-json",
                "<<NEEDS_INPUT>>\nHere is my question:\n{ \"kind\": \"open\", \"prompt\": \"Where?\" }\nThanks.",
                d => d is { Parsed: true, Question: AgentQuestion.Open o } && o.Prompt == "Where?"),

            new("braces-inside-string",
                "<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"Use the {placeholder} or not?\" }",
                d => d.Question is AgentQuestion.Open o && o.Prompt == "Use the {placeholder} or not?"),

            new("malformed-json-degrades-to-open",
                "<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": ",
                d => d is { Degraded: true, Parsed: false, Question: AgentQuestion.Open }),

            new("sentinel-but-no-json-degrades",
                "<<NEEDS_INPUT>>\nWhat bucket should I use?",
                d => d is { Degraded: true, JsonExtracted: false, Question: AgentQuestion.Open o } && o.Prompt == "What bucket should I use?"),

            new("empty-prompt-degrades",
                "<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"\" }",
                d => d is { Degraded: true, Question: AgentQuestion.Open }),

            new("choice-missing-options-falls-to-open",
                "<<NEEDS_INPUT>>\n{ \"kind\": \"choice\", \"prompt\": \"Which?\" }",
                d => d is { Parsed: true, Degraded: false, Question: AgentQuestion.Open o } && o.Prompt == "Which?"),

            new("last-sentinel-wins",
                "<<NEEDS_INPUT>> (mentioned earlier)\n... reasoning ...\n<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"Final?\" }",
                d => d.Question is AgentQuestion.Open o && o.Prompt == "Final?"),

            new("trailing-prose-after-object",
                "<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"Q?\" }\nLet me know.",
                d => d is { Parsed: true, Question: AgentQuestion.Open o } && o.Prompt == "Q?"),
        };

        var pass = 0;
        foreach (var c in cases)
        {
            var diag = QuestionParser.Diagnose(c.Input);
            var ok = false;
            try { ok = c.Expect(diag); } catch { ok = false; }
            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {c.Name}");
            if (!ok)
                Console.WriteLine($"         got: sentinel={diag.SentinelFound} extracted={diag.JsonExtracted} parsed={diag.Parsed} degraded={diag.Degraded} q={Describe(diag.Question)}");
            if (ok) pass++;
        }

        Console.WriteLine($"\nParser self-test: {pass}/{cases.Count} passed.");
        return pass == cases.Count ? 0 : 1;
    }

    private static string Describe(AgentQuestion? q) => q switch
    {
        AgentQuestion.Open o => $"Open(\"{o.Prompt}\")",
        AgentQuestion.Choice c => $"Choice(\"{c.Prompt}\", [{string.Join(",", c.Options)}], free={c.AllowFreeText})",
        _ => "null",
    };
}

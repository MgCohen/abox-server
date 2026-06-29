using ABox.Domain.Agents;

namespace ABox.Agents.Tests.Unit;

public class QuestionParserTests
{
    [Rule("Text without the sentinel → null (not a question)")]
    [Fact]
    public void No_sentinel_is_not_a_question()
        => Assert.Null(QuestionParser.TryParse("All done. Tests pass."));

    [Rule("Text without the sentinel → null (not a question)")]
    [Fact]
    public void Empty_text_is_not_a_question()
        => Assert.Null(QuestionParser.TryParse(""));

    [Rule("A sentinel with a well-formed open envelope → an Open question carrying its prompt")]
    [Fact]
    public void A_clean_open_envelope_parses()
    {
        var q = QuestionParser.TryParse("Some preamble.\n<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"Which bucket?\" }");
        var open = Assert.IsType<AgentQuestion.Open>(q);
        Assert.Equal("Which bucket?", open.Prompt);
    }

    [Rule("A sentinel with a well-formed choice envelope → a Choice question carrying its options and free-text flag")]
    [Fact]
    public void A_clean_choice_envelope_parses_with_options()
    {
        var q = QuestionParser.TryParse("<<NEEDS_INPUT>>\n{ \"kind\": \"choice\", \"prompt\": \"Which TFM?\", \"options\": [\"net8.0\", \"net10.0\"], \"allow_free_text\": false }");
        var choice = Assert.IsType<AgentQuestion.Choice>(q);
        Assert.Equal(["net8.0", "net10.0"], choice.Options);
        Assert.False(choice.AllowFreeText);
    }

    [Rule("A sentinel with a well-formed choice envelope → a Choice question carrying its options and free-text flag")]
    [Fact]
    public void Allow_free_text_is_honored()
    {
        var q = QuestionParser.TryParse("<<NEEDS_INPUT>>\n{ \"kind\": \"choice\", \"prompt\": \"Pick\", \"options\": [\"a\"], \"allow_free_text\": true }");
        Assert.True(Assert.IsType<AgentQuestion.Choice>(q).AllowFreeText);
    }

    [Rule("A sentinel whose JSON is wrapped in noise (fences, surrounding prose) → the embedded object is still parsed")]
    [Fact]
    public void A_fenced_json_block_is_tolerated()
    {
        var q = QuestionParser.TryParse("<<NEEDS_INPUT>>\n```json\n{ \"kind\": \"open\", \"prompt\": \"What key?\" }\n```");
        Assert.Equal("What key?", Assert.IsType<AgentQuestion.Open>(q).Prompt);
    }

    [Rule("A sentinel whose JSON is wrapped in noise (fences, surrounding prose) → the embedded object is still parsed")]
    [Fact]
    public void Leading_prose_before_the_object_is_tolerated()
    {
        var q = QuestionParser.TryParse("<<NEEDS_INPUT>>\nHere is my question:\n{ \"kind\": \"open\", \"prompt\": \"Where?\" }\nThanks.");
        Assert.Equal("Where?", Assert.IsType<AgentQuestion.Open>(q).Prompt);
    }

    [Rule("Braces inside a JSON string value → the object scan stays balanced and parses correctly")]
    [Fact]
    public void Braces_inside_a_string_do_not_unbalance_the_scan()
    {
        var q = QuestionParser.TryParse("<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"Use the {placeholder} or not?\" }");
        Assert.Equal("Use the {placeholder} or not?", Assert.IsType<AgentQuestion.Open>(q).Prompt);
    }

    [Rule("A sentinel whose JSON cannot be parsed → an Open question whose prompt is the raw tail")]
    [Fact]
    public void Malformed_json_degrades_to_open_with_the_raw_tail()
    {
        var q = QuestionParser.TryParse("<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": ");
        var open = Assert.IsType<AgentQuestion.Open>(q);
        Assert.Equal(open.RawTail, open.Prompt);
    }

    [Rule("A sentinel with no JSON object at all → an Open question whose prompt is the trailing prose")]
    [Fact]
    public void A_sentinel_with_no_json_degrades_to_open()
    {
        var q = QuestionParser.TryParse("<<NEEDS_INPUT>>\nWhat bucket should I use?");
        Assert.Equal("What bucket should I use?", Assert.IsType<AgentQuestion.Open>(q).Prompt);
    }

    [Rule("An open envelope with an empty prompt → still an Open question")]
    [Fact]
    public void An_empty_prompt_degrades_to_open()
    {
        var q = QuestionParser.TryParse("<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"\" }");
        Assert.IsType<AgentQuestion.Open>(q);
    }

    [Rule("A choice envelope missing its options → falls back to an Open question")]
    [Fact]
    public void A_choice_missing_options_falls_back_to_open()
    {
        var q = QuestionParser.TryParse("<<NEEDS_INPUT>>\n{ \"kind\": \"choice\", \"prompt\": \"Which?\" }");
        Assert.Equal("Which?", Assert.IsType<AgentQuestion.Open>(q).Prompt);
    }

    [Rule("Multiple sentinels in the text → the last sentinel's envelope is the parsed question")]
    [Fact]
    public void The_last_sentinel_wins()
    {
        var q = QuestionParser.TryParse("<<NEEDS_INPUT>> earlier mention\n...\n<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"Final?\" }");
        Assert.Equal("Final?", Assert.IsType<AgentQuestion.Open>(q).Prompt);
    }

    [Rule("A sentinel whose JSON is wrapped in noise (fences, surrounding prose) → the embedded object is still parsed")]
    [Fact]
    public void Trailing_prose_after_the_object_is_tolerated()
    {
        var q = QuestionParser.TryParse("<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"Q?\" }\nLet me know.");
        Assert.Equal("Q?", Assert.IsType<AgentQuestion.Open>(q).Prompt);
    }
}

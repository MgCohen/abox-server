using RemoteAgents.Primitives;

namespace RemoteAgents.Tests.Primitives;

// Touches process env vars + PATH — keep serial.
[Collection("EnvSensitive")]
public class SubscriptionGuardTests
{
    [Theory]
    [InlineData("ANTHROPIC_API_KEY")]
    [InlineData("CLAUDE_API_KEY")]
    [InlineData("OPENAI_API_KEY")]
    public async Task Throws_when_api_key_env_var_is_set(string varName)
    {
        var original = Environment.GetEnvironmentVariable(varName);
        try
        {
            Environment.SetEnvironmentVariable(varName, "sk-test-not-real");
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await SubscriptionGuard.CheckAsync());
            Assert.Contains(varName, ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, original);
        }
    }

    [Fact]
    public async Task Throws_when_claude_cli_is_not_on_path()
    {
        // Restrict PATH to System32 only — neither claude nor codex live there.
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalPathExt = Environment.GetEnvironmentVariable("PATHEXT");
        try
        {
            Environment.SetEnvironmentVariable("PATH", Environment.SystemDirectory);
            // Make sure no API-key var leaks in
            foreach (var k in new[] { "ANTHROPIC_API_KEY", "CLAUDE_API_KEY", "OPENAI_API_KEY" })
                Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(k)), $"{k} must be unset for this test");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await SubscriptionGuard.CheckAsync());
            // Claude check comes first; message should name it.
            Assert.Contains("claude", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("PATHEXT", originalPathExt);
        }
    }
}

[CollectionDefinition("EnvSensitive", DisableParallelization = true)]
public class EnvSensitiveCollection { }

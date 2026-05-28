using RemoteAgents.Events;

namespace RemoteAgents.Tests.Sessions;

public class ProviderJsonlIngestSinkTests : IDisposable
{
    private readonly string _root;

    public ProviderJsonlIngestSinkTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ra-ingest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void EncodeCwd_matches_claude_code_disk_format()
    {
        Assert.Equal("C--Unity-CardFramework", ProviderJsonlIngestSink.EncodeCwd("C:\\Unity\\CardFramework"));
        Assert.Equal("C--Unity-dotnet-pty-smoke-stage2-work",
            ProviderJsonlIngestSink.EncodeCwd("C:\\Unity\\dotnet-pty-smoke\\stage2-work"));
        Assert.Equal("C--Unity-Mixed-Slashes",
            ProviderJsonlIngestSink.EncodeCwd("C:/Unity\\Mixed/Slashes"));
    }
}

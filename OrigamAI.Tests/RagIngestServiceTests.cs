using OrigamAI.Services;
using Xunit;

namespace OrigamAI.Tests;

public class RagIngestServiceTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;

    public RagIngestServiceTests(AppFactory app)
    {
        _app = app;
    }

    [Fact]
    public void Service_resolves_from_DI()
    {
        var svc = _app.Resolve<RagIngestService>();
        Assert.NotNull(svc);
    }

    [Fact]
    public async Task IngestTextAsyncTest()
    {
        var result = await _app.Ingest.IngestTextAsync(string.Empty, "test", null, CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task IngestOrigamTopicAsyncTest()
    {
        var result = await _app.Ingest.IngestOrigamTopicAsync(
            "https://community.origam.com/t/how-to-create-entities-and-fields-in-architect/3911",
            CancellationToken.None);
        Assert.NotNull(result);

    }
}

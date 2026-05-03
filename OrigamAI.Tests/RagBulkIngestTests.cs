using System.Diagnostics;
using OrigamAI.Services;
using Xunit;
using Xunit.Abstractions;

namespace OrigamAI.Tests;

public class RagBulkIngestTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _app;
    private readonly ITestOutputHelper _output;

    public RagBulkIngestTests(AppFactory app, ITestOutputHelper output)
    {
        _app = app;
        _output = output;
    }

    /// <summary>
    /// Reads OrigamAI.Tests/topics.txt and ingests every listed topic.
    /// Hits live community.origam.com + OpenAI Vision — runs slowly and costs API credits.
    /// Run on demand: dotnet test --filter "FullyQualifiedName~IngestTopicsBatch"
    /// </summary>
    [Fact]
    [Trait("Category", "BulkIngest")]
    public async Task IngestTopicsBatch()
    {
        var topicsFile = Path.Combine(AppContext.BaseDirectory, "topics.txt");
        Assert.True(File.Exists(topicsFile), $"topics.txt not found at {topicsFile}");

        var topics = (await File.ReadAllLinesAsync(topicsFile))
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#"))
            .ToList();

        Assert.NotEmpty(topics);
        _output.WriteLine($"Ingesting {topics.Count} topic(s)...\n");

        var results = new List<(string Topic, IngestOrigamTopicResult? Result, string? Error, double Seconds)>();
        var totalSw = Stopwatch.StartNew();

        for (var i = 0; i < topics.Count; i++)
        {
            var topic = topics[i];
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await _app.Ingest.IngestOrigamTopicAsync(topic, CancellationToken.None);
                sw.Stop();
                results.Add((topic, result, null, sw.Elapsed.TotalSeconds));
                _output.WriteLine(
                    $"[{i + 1}/{topics.Count}] OK  {sw.Elapsed.TotalSeconds,5:F1}s | " +
                    $"posts={result.Posts,2} images={result.ImagesDescribed}/{result.ImagesFound} " +
                    $"chars={result.Chars,6} | {result.Title}");

                var failedImages = result.Images.Where(im => !string.IsNullOrEmpty(im.Error)).ToList();
                if (failedImages.Count > 0)
                {
                    var grouped = failedImages
                        .GroupBy(f => f.Error)
                        .OrderByDescending(g => g.Count());
                    foreach (var g in grouped)
                        _output.WriteLine($"          ✗ {g.Count()}× {g.Key}");
                    foreach (var f in failedImages.Take(3))
                        _output.WriteLine($"            e.g. {f.Url}");
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                results.Add((topic, null, ex.Message, sw.Elapsed.TotalSeconds));
                _output.WriteLine(
                    $"[{i + 1}/{topics.Count}] ERR {sw.Elapsed.TotalSeconds,5:F1}s | {topic} | {ex.GetType().Name}: {ex.Message}");
            }
        }
        totalSw.Stop();

        var ok = results.Count(r => r.Result is not null);
        var failed = results.Count(r => r.Error is not null);
        var totalPosts = results.Where(r => r.Result is not null).Sum(r => r.Result!.Posts);
        var totalImagesFound = results.Where(r => r.Result is not null).Sum(r => r.Result!.ImagesFound);
        var totalImagesDescribed = results.Where(r => r.Result is not null).Sum(r => r.Result!.ImagesDescribed);

        _output.WriteLine("");
        _output.WriteLine("=== Summary ===");
        _output.WriteLine($"Topics: {results.Count}  OK: {ok}  Failed: {failed}");
        _output.WriteLine($"Posts: {totalPosts}");
        _output.WriteLine($"Images described: {totalImagesDescribed}/{totalImagesFound}");
        _output.WriteLine($"Total time: {totalSw.Elapsed.TotalSeconds:F1}s");

        if (failed > 0)
        {
            _output.WriteLine("");
            _output.WriteLine("Failed topics:");
            foreach (var f in results.Where(r => r.Error is not null))
                _output.WriteLine($"  - {f.Topic}: {f.Error}");
        }

        Assert.Equal(0, failed);
    }
}

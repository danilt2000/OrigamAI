using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrigamAI.Services;

public class OrigamCommunityService
{
    private readonly HttpClient _http;
    private readonly ILogger<OrigamCommunityService> _logger;

    public OrigamCommunityService(HttpClient http, ILogger<OrigamCommunityService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<Category>> GetCategoriesAsync(CancellationToken ct = default)
    {
        var json = await _http.GetStringAsync("/categories.json", ct);
        using var doc = JsonDocument.Parse(json);
        var list = doc.RootElement
            .GetProperty("category_list")
            .GetProperty("categories")
            .Deserialize<List<Category>>(JsonOpts) ?? new();
        return list;
    }

    public async Task<List<TopicSummary>> GetCategoryTopicsAsync(int categoryId, string slug, int maxPages = 1, CancellationToken ct = default)
    {
        var topics = new List<TopicSummary>();
        for (var page = 0; page < maxPages; page++)
        {
            var url = $"/c/{slug}/{categoryId}.json?page={page}";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("topic_list", out var tl)) break;
            if (!tl.TryGetProperty("topics", out var t)) break;
            var pageTopics = t.Deserialize<List<TopicSummary>>(JsonOpts) ?? new();
            if (pageTopics.Count == 0) break;
            topics.AddRange(pageTopics);
        }
        return topics;
    }

    public async Task<TopicDetail?> GetTopicAsync(int topicId, CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync($"/t/{topicId}.json", ct);
            return JsonSerializer.Deserialize<TopicDetail>(json, JsonOpts);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Failed to fetch topic {Id}: {Msg}", topicId, ex.Message);
            return null;
        }
    }

    public static int? ExtractTopicId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        if (int.TryParse(input, out var direct)) return direct;
        var m = System.Text.RegularExpressions.Regex.Match(input, @"/t/[^/]+/(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public record Category(int Id, string Name, string Slug, string? Description, int? TopicCount);

public record TopicSummary(int Id, string Title, string Slug, int PostsCount, int CategoryId);

public record TopicDetail(int Id, string Title, string Slug, int CategoryId, PostStream PostStream);

public record PostStream(List<Post> Posts);

public record Post(int Id, string Username, string Cooked, DateTime CreatedAt);

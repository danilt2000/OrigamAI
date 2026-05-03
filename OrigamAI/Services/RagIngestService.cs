using System.Text.RegularExpressions;
using Microsoft.KernelMemory;

namespace OrigamAI.Services;

public class IngestValidationException : Exception
{
    public IngestValidationException(string message) : base(message) { }
}

public class IngestNotFoundException : Exception
{
    public IngestNotFoundException(string message) : base(message) { }
}

public record IngestOrigamSummary(int Count, IReadOnlyList<object> Items);
public record IngestOrigamTopicResult(
    string DocId,
    string Title,
    string Url,
    int Posts,
    int Chars,
    int ImagesFound,
    int ImagesDescribed,
    IReadOnlyList<ImageDescription> Images);

public class RagIngestService
{
    private readonly IKernelMemory _memory;
    private readonly OrigamCommunityService _origam;
    private readonly ImageDescriptionService _images;
    private readonly ILogger<RagIngestService> _logger;

    private static readonly Regex LightboxAnchorBlockRegex = new(
        @"<a\b[^>]*\bclass=""[^""]*\blightbox\b[^""]*""[^>]*>.*?</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HrefRegex = new(
        @"\bhref=""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ImageMarkerRegex = new(
        @"[ \t]*\[\[__IMG_(\d+)__\]\][ \t]*",
        RegexOptions.Compiled);

    public RagIngestService(
        IKernelMemory memory,
        OrigamCommunityService origam,
        ImageDescriptionService images,
        ILogger<RagIngestService> logger)
    {
        _memory = memory;
        _origam = origam;
        _images = images;
        _logger = logger;
    }

    public async Task<string> IngestTextAsync(string id, string text, IReadOnlyDictionary<string, string>? tagPairs, CancellationToken ct)
    {
        var tags = new TagCollection();
        if (tagPairs is not null)
            foreach (var (k, v) in tagPairs) tags.Add(k, v);
        return await _memory.ImportTextAsync(text, documentId: id, tags: tags, cancellationToken: ct);
    }

    public async Task<IngestOrigamSummary> IngestOrigamAsync(int? categoryId, int maxTopicsPerCategory, int maxPages, CancellationToken ct)
    {
        var categories = await _origam.GetCategoriesAsync(ct);
        if (categoryId.HasValue)
            categories = categories.Where(c => c.Id == categoryId.Value).ToList();

        var ingested = new List<object>();
        foreach (var cat in categories)
        {
            var topics = await _origam.GetCategoryTopicsAsync(cat.Id, cat.Slug, maxPages, ct);
            foreach (var t in topics.Take(maxTopicsPerCategory))
            {
                var detail = await _origam.GetTopicAsync(t.Id, ct);
                if (detail is null) continue;

                var url = $"https://community.origam.com/t/{detail.Slug}/{detail.Id}";
                var body = string.Join("\n\n---\n\n",
                    detail.PostStream.Posts.Select(p => $"@{p.Username} ({p.CreatedAt:u}):\n{StripHtml(p.Cooked)}"));
                var text = $"# {detail.Title}\n\nCategory: {cat.Name}\nURL: {url}\n\n{body}";

                var tags = new TagCollection
                {
                    { "source", "origam-community" },
                    { "category", cat.Name },
                    { "categoryId", cat.Id.ToString() },
                    { "topicId", detail.Id.ToString() },
                    { "url", url },
                    { "title", detail.Title }
                };

                var docId = $"origam-topic-{detail.Id}";
                await _memory.ImportTextAsync(text, documentId: docId, tags: tags, cancellationToken: ct);
                ingested.Add(new { docId, detail.Title, category = cat.Name });
            }
        }

        return new IngestOrigamSummary(ingested.Count, ingested);
    }

    public async Task<IngestOrigamTopicResult> IngestOrigamTopicAsync(string topicIdOrUrl, CancellationToken ct)
    {
        var id = OrigamCommunityService.ExtractTopicId(topicIdOrUrl);
        if (id is null)
            throw new IngestValidationException("Provide a numeric topic id (e.g. 3932) or a full URL like https://community.origam.com/t/how-to-create-a-lookup/3932");

        var detail = await _origam.GetTopicAsync(id.Value, ct);
        if (detail is null)
            throw new IngestNotFoundException($"Topic {id} not found on community.origam.com");

        var url = $"https://community.origam.com/t/{detail.Slug}/{detail.Id}";

        var allImages = new List<ImageDescription>();
        var postBlocks = new List<string>();

        foreach (var p in detail.PostStream.Posts)
        {
            var (htmlWithMarkers, imageUrls) = InlineImageMarkers(p.Cooked);
            var bodyText = StripHtml(htmlWithMarkers);

            var descriptions = new Dictionary<int, ImageDescription>(imageUrls.Count);
            for (var i = 0; i < imageUrls.Count; i++)
            {
                var description = await _images.DescribeAsync(imageUrls[i], ct);
                allImages.Add(description);
                descriptions[i] = description;
            }

            var bodyWithImages = ImageMarkerRegex.Replace(bodyText, m =>
            {
                var idx = int.Parse(m.Groups[1].Value);
                if (!descriptions.TryGetValue(idx, out var desc)) return string.Empty;
                return string.IsNullOrWhiteSpace(desc.Description)
                    ? $"\n\n[Image: {desc.Url}]\n\n"
                    : $"\n\n[Image: {desc.Url}]\nDescription: {desc.Description}\n\n";
            });

            postBlocks.Add($"@{p.Username} ({p.CreatedAt:u}):\n{bodyWithImages}");
        }

        var body = string.Join("\n\n---\n\n", postBlocks);
        var text = $"# {detail.Title}\n\nURL: {url}\n\n{body}";

        var imagesDescribed = allImages.Count(i => !string.IsNullOrWhiteSpace(i.Description));

        var tags = new TagCollection
        {
            { "source", "origam-community" },
            { "topicId", detail.Id.ToString() },
            { "categoryId", detail.CategoryId.ToString() },
            { "url", url },
            { "title", detail.Title }
        };
        if (allImages.Count > 0)
        {
            tags.Add("imagesFound", allImages.Count.ToString());
            tags.Add("imagesDescribed", imagesDescribed.ToString());
            foreach (var img in allImages.Where(i => !string.IsNullOrWhiteSpace(i.Description)))
                tags.Add("imageUrl", img.Url);
        }

        _logger.LogInformation(
            "Ingest topic {Id}: {Posts} posts, {Found} images found, {Described} described",
            detail.Id, detail.PostStream.Posts.Count, allImages.Count, imagesDescribed);

        var docId = $"origam-topic-{detail.Id}";
        await _memory.ImportTextAsync(text, documentId: docId, tags: tags, cancellationToken: ct);

        return new IngestOrigamTopicResult(
            docId,
            detail.Title,
            url,
            detail.PostStream.Posts.Count,
            text.Length,
            allImages.Count,
            imagesDescribed,
            allImages);
    }

    public Task<SearchResult> SearchAsync(string query, int limit, CancellationToken ct)
        => _memory.SearchAsync(query, limit: limit, cancellationToken: ct);

    public Task DeleteDocumentAsync(string id, CancellationToken ct)
        => _memory.DeleteDocumentAsync(id, cancellationToken: ct);

    internal static (string HtmlWithMarkers, List<string> ImageUrls) InlineImageMarkers(string? cookedHtml)
    {
        var urls = new List<string>();
        if (string.IsNullOrWhiteSpace(cookedHtml)) return (string.Empty, urls);

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var html = LightboxAnchorBlockRegex.Replace(cookedHtml, match =>
        {
            var hrefMatch = HrefRegex.Match(match.Value);
            if (!hrefMatch.Success) return match.Value;

            var raw = System.Net.WebUtility.HtmlDecode(hrefMatch.Groups[1].Value);
            if (!IsImageUrl(raw)) return match.Value;

            var resolved = ResolveUrl(raw);
            if (!seen.TryGetValue(resolved, out var idx))
            {
                idx = urls.Count;
                urls.Add(resolved);
                seen[resolved] = idx;
            }
            return $"\n[[__IMG_{idx}__]]\n";
        });
        return (html, urls);
    }

    private static bool IsImageUrl(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.AbsolutePath : url;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
    }

    private static string ResolveUrl(string url)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.StartsWith("//")) return "https:" + url;
        if (url.StartsWith("/")) return "https://community.origam.com" + url;
        return url;
    }

    private static string StripHtml(string html) =>
        Regex.Replace(html ?? "", "<.*?>", " ", RegexOptions.Singleline)
             .Replace("&nbsp;", " ")
             .Replace("&amp;", "&")
             .Replace("&lt;", "<")
             .Replace("&gt;", ">")
             .Replace("&quot;", "\"");
}

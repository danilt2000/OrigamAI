using Microsoft.AspNetCore.Mvc;
using Microsoft.KernelMemory;
using OrigamAI.Services;
using System.Text.RegularExpressions;

namespace OrigamAI.Controllers;

[ApiController]
[Route("api/rag")]
public class RagController : ControllerBase
{
    private readonly IKernelMemory _memory;
    private readonly OrigamCommunityService _origam;
    private readonly PollinationsChatService _chat;
    private readonly ILogger<RagController> _logger;

    public RagController(IKernelMemory memory, OrigamCommunityService origam, PollinationsChatService chat, ILogger<RagController> logger)
    {
        _memory = memory;
        _origam = origam;
        _chat = chat;
        _logger = logger;
    }

    public record IngestTextRequest(string Id, string Text, Dictionary<string, string>? Tags);
    public record AskRequest(string Question, string? Filter);

    [HttpPost("ingest-text")]
    public async Task<IActionResult> IngestText([FromBody] IngestTextRequest req, CancellationToken ct)
    {
        var tags = new TagCollection();
        if (req.Tags is not null)
            foreach (var (k, v) in req.Tags) tags.Add(k, v);

        var id = await _memory.ImportTextAsync(req.Text, documentId: req.Id, tags: tags, cancellationToken: ct);
        return Ok(new { documentId = id });
    }

    [HttpPost("ingest-origam")]
    public async Task<IActionResult> IngestOrigam(
        [FromQuery] int? categoryId = null,
        [FromQuery] int maxTopicsPerCategory = 10,
        [FromQuery] int maxPages = 1,
        CancellationToken ct = default)
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

                var body = string.Join("\n\n---\n\n",
                    detail.PostStream.Posts.Select(p => $"@{p.Username} ({p.CreatedAt:u}):\n{StripHtml(p.Cooked)}"));

                var url = $"https://community.origam.com/t/{detail.Slug}/{detail.Id}";
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

        return Ok(new { count = ingested.Count, items = ingested });
    }

    public record IngestOrigamTopicRequest(string TopicIdOrUrl);

    [HttpPost("ingest-origam-topic")]
    public async Task<IActionResult> IngestOrigamTopic([FromBody] IngestOrigamTopicRequest req, CancellationToken ct)
    {
        var id = OrigamCommunityService.ExtractTopicId(req.TopicIdOrUrl);
        if (id is null)
            return BadRequest(new { error = "Provide a numeric topic id (e.g. 3932) or a full URL like https://community.origam.com/t/how-to-create-a-lookup/3932" });

        var detail = await _origam.GetTopicAsync(id.Value, ct);
        if (detail is null)
            return NotFound(new { error = $"Topic {id} not found on community.origam.com" });

        var body = string.Join("\n\n---\n\n",
            detail.PostStream.Posts.Select(p => $"@{p.Username} ({p.CreatedAt:u}):\n{StripHtml(p.Cooked)}"));

        var url = $"https://community.origam.com/t/{detail.Slug}/{detail.Id}";
        var text = $"# {detail.Title}\n\nURL: {url}\n\n{body}";

        var tags = new TagCollection
        {
            { "source", "origam-community" },
            { "topicId", detail.Id.ToString() },
            { "categoryId", detail.CategoryId.ToString() },
            { "url", url },
            { "title", detail.Title }
        };

        var docId = $"origam-topic-{detail.Id}";
        await _memory.ImportTextAsync(text, documentId: docId, tags: tags, cancellationToken: ct);

        return Ok(new
        {
            docId,
            detail.Title,
            url,
            posts = detail.PostStream.Posts.Count,
            chars = text.Length
        });
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest req, CancellationToken ct)
    {
        MemoryFilter? filter = null;
        if (!string.IsNullOrWhiteSpace(req.Filter))
        {
            var parts = req.Filter.Split('=', 2);
            if (parts.Length == 2)
                filter = MemoryFilters.ByTag(parts[0], parts[1]);
        }

        _logger.LogInformation("ASK question='{Q}' filter={F}", req.Question, req.Filter);

        var search = await _memory.SearchAsync(req.Question, filter: filter, minRelevance: 0, limit: 5, cancellationToken: ct);

        var citations = search.Results
            .SelectMany(c => c.Partitions.Select(p => new
            {
                c.DocumentId,
                c.SourceName,
                c.Link,
                p.Text,
                p.Relevance,
                Url = p.Tags.TryGetValue("url", out var u) ? u?.FirstOrDefault() : null,
                Title = p.Tags.TryGetValue("title", out var t) ? t?.FirstOrDefault() : null
            }))
            .OrderByDescending(x => x.Relevance)
            .Take(5)
            .ToList();

        if (citations.Count == 0)
            return Ok(new { answer = "No matching facts were found in the knowledge base.", sources = Array.Empty<object>() });

        var facts = string.Join("\n\n---\n\n",
            citations.Select((c, i) =>
            {
                var header = $"[Source {i + 1}";
                if (!string.IsNullOrEmpty(c.Title)) header += $" — {c.Title}";
                if (!string.IsNullOrEmpty(c.Url)) header += $" — {c.Url}";
                header += $" | relevance={c.Relevance:F2}]";
                return $"{header}\n{c.Text}";
            }));

        var system =
            "You are a knowledge-base assistant. Answer ONLY using the provided facts. " +
            "If the facts are insufficient, say so honestly. " +
            "Always cite the sources you used as Markdown links: [Source 1](URL). " +
            "If a source has no URL, fall back to plain [Source N]. " +
            "Reply in the same language as the user's question.";
        var user = $"Facts:\n\n{facts}\n\nQuestion: {req.Question}";

        _logger.LogInformation("ASK calling Pollinations with {N} citations, top relevance {R}",
            citations.Count, citations[0].Relevance);

        string answerText;
        try
        {
            answerText = await _chat.ChatAsync(system, user, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat call failed");
            return StatusCode(502, new { error = ex.Message });
        }

        return Ok(new
        {
            answer = answerText,
            sources = citations.Select(c => new { c.DocumentId, c.Title, c.Url, c.SourceName, c.Link, c.Relevance })
        });
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 5, CancellationToken ct = default)
    {
        var results = await _memory.SearchAsync(q, limit: limit, cancellationToken: ct);
        return Ok(results);
    }

    [HttpDelete("document/{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await _memory.DeleteDocumentAsync(id, cancellationToken: ct);
        return NoContent();
    }

    private static string StripHtml(string html) =>
        Regex.Replace(html ?? "", "<.*?>", " ", RegexOptions.Singleline)
             .Replace("&nbsp;", " ")
             .Replace("&amp;", "&")
             .Replace("&lt;", "<")
             .Replace("&gt;", ">")
             .Replace("&quot;", "\"");
}

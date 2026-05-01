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
    private readonly OpenAiVisionService _vision;
    private readonly ILogger<RagController> _logger;

    public RagController(IKernelMemory memory, OrigamCommunityService origam, PollinationsChatService chat, OpenAiVisionService vision, ILogger<RagController> logger)
    {
        _memory = memory;
        _origam = origam;
        _chat = chat;
        _vision = vision;
        _logger = logger;
    }

    public record IngestTextRequest(string Id, string Text, Dictionary<string, string>? Tags);
    public record AskHistoryItem(string Role, string Content);
    public record AskRequest(string Question, string? Filter, List<AskHistoryItem>? History);

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
        => await AskCore(req.Question, req.Filter, images: null, BuildHistory(req.History), ct);

    [HttpPost("ask-multipart")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> AskMultipart(
        [FromForm] string? question,
        [FromForm] string? filter,
        [FromForm] string? history,
        [FromForm] List<IFormFile>? images,
        CancellationToken ct)
    {
        var imgs = new List<ChatImage>();
        if (images is { Count: > 0 })
        {
            foreach (var f in images)
            {
                if (f.Length == 0) continue;
                using var ms = new MemoryStream();
                await f.CopyToAsync(ms, ct);
                imgs.Add(new ChatImage(ms.ToArray(), string.IsNullOrWhiteSpace(f.ContentType) ? "image/png" : f.ContentType));
            }
        }

        List<AskHistoryItem>? historyList = null;
        if (!string.IsNullOrWhiteSpace(history))
        {
            try
            {
                historyList = System.Text.Json.JsonSerializer.Deserialize<List<AskHistoryItem>>(history,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse history field");
            }
        }

        return await AskCore(question ?? "", filter, imgs.Count > 0 ? imgs : null, BuildHistory(historyList), ct);
    }

    private static List<ChatHistoryMessage>? BuildHistory(List<AskHistoryItem>? items)
    {
        if (items is null || items.Count == 0) return null;
        // Cap at the last 10 messages to keep token cost predictable.
        var slice = items.Count > 10 ? items.GetRange(items.Count - 10, 10) : items;
        return slice
            .Where(i => !string.IsNullOrWhiteSpace(i.Content))
            .Select(i => new ChatHistoryMessage(i.Role == "assistant" ? "assistant" : "user", i.Content))
            .ToList();
    }

    private async Task<IActionResult> AskCore(string question, string? filterStr, IReadOnlyList<ChatImage>? images, IReadOnlyList<ChatHistoryMessage>? history, CancellationToken ct)
    {
        var hasText = !string.IsNullOrWhiteSpace(question);
        var hasImages = images is { Count: > 0 };

        if (!hasText && !hasImages)
            return BadRequest(new { error = "Provide question text, image(s), or both." });

        MemoryFilter? filter = null;
        if (!string.IsNullOrWhiteSpace(filterStr))
        {
            var parts = filterStr.Split('=', 2);
            if (parts.Length == 2)
                filter = MemoryFilters.ByTag(parts[0], parts[1]);
        }

        _logger.LogInformation("ASK question='{Q}' filter={F} images={I}", question, filterStr, images?.Count ?? 0);

        var pipeline = new List<PipelineStage>();
        var totalSw = System.Diagnostics.Stopwatch.StartNew();

        // Two-step retrieval: when images are attached, ask the vision model to caption them
        // first, then use (caption + user text) as the embedding/search query. The original
        // user text + images are still passed to the final answer call below.
        string? imageCaption = null;
        if (hasImages)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var captionSystem =
                    "You extract searchable keywords from images for a vector search engine. " +
                    "Your output will be embedded and matched against a documentation knowledge base, " +
                    "so density and accuracy of domain terms matter more than fluency.\n\n" +
                    "Rules:\n" +
                    "1. Transcribe ALL visible text VERBATIM — every UI label, field name, dialog title, " +
                    "menu item, button, tab, column header, error message, file name, code snippet, " +
                    "URL, product/feature name, identifier, number. Preserve original casing.\n" +
                    "2. After the verbatim text, add a short line describing the apparent task or context " +
                    "(e.g. \"property editor for a Data Service Lookup component\").\n" +
                    "3. No commentary, no \"the image shows…\", no speculation about what the user wants. " +
                    "Just keywords + one context line.\n" +
                    "4. Output plain text, comma- or newline-separated. No markdown.\n" +
                    "5. Length: as long as needed to capture all visible text. For a screenshot with " +
                    "30 labels, output 30 labels — do not summarize.";
                var captionUser = hasText
                    ? $"User's question (for context, do not answer it): {question}\n\nExtract searchable keywords from the attached image(s)."
                    : "Extract searchable keywords from the attached image(s).";
                string captionProvider;
                if (_vision.IsConfigured)
                {
                    imageCaption = await _vision.CaptionAsync(captionSystem, captionUser, images!, ct);
                    captionProvider = "openai";
                }
                else
                {
                    imageCaption = await _chat.ChatAsync(captionSystem, captionUser, images, ct);
                    captionProvider = "pollinations";
                }
                sw.Stop();
                _logger.LogInformation("ASK image caption ({P}): {C}", captionProvider, imageCaption);
                pipeline.Add(new PipelineStage("caption", true, sw.ElapsedMilliseconds, null,
                    new { caption = imageCaption, imageCount = images!.Count, provider = captionProvider }));
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning(ex, "Image caption step failed; continuing without caption");
                pipeline.Add(new PipelineStage("caption", false, sw.ElapsedMilliseconds, ex.Message, null));
            }
        }
        else
        {
            pipeline.Add(new PipelineStage("caption", true, 0, null, new { skipped = true, reason = "no images attached" }));
        }

        var searchQuery = string.Join(" ", new[] { question, imageCaption }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        var citations = new List<CitationVm>();
        var searchSw = System.Diagnostics.Stopwatch.StartNew();
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            try
            {
                var searchLimit = 10;
                var search = await _memory.SearchAsync(searchQuery, filter: filter, minRelevance: 0, limit: searchLimit, cancellationToken: ct);
                citations = search.Results
                    .SelectMany(c => c.Partitions.Select(p => new CitationVm(
                        c.DocumentId,
                        c.SourceName,
                        c.Link,
                        p.Text,
                        p.Relevance,
                        p.Tags.TryGetValue("url", out var u) ? u?.FirstOrDefault() : null,
                        p.Tags.TryGetValue("title", out var t) ? t?.FirstOrDefault() : null)))
                    .OrderByDescending(x => x.Relevance)
                    .Take(searchLimit)
                    .ToList();
                searchSw.Stop();
                pipeline.Add(new PipelineStage("search", true, searchSw.ElapsedMilliseconds, null, new
                {
                    query = searchQuery,
                    queryLength = searchQuery.Length,
                    filter = filterStr,
                    limit = searchLimit,
                    resultCount = citations.Count,
                    results = citations.Select(c => new
                    {
                        c.DocumentId,
                        c.Title,
                        c.Url,
                        c.Relevance,
                        snippet = c.Text.Length > 200 ? c.Text[..200] + "…" : c.Text
                    })
                }));
            }
            catch (Exception ex)
            {
                searchSw.Stop();
                _logger.LogError(ex, "Search step failed");
                pipeline.Add(new PipelineStage("search", false, searchSw.ElapsedMilliseconds, ex.Message, null));
            }
        }
        else
        {
            pipeline.Add(new PipelineStage("search", true, 0, null, new { skipped = true, reason = "no search query" }));
        }

        string system;
        string user;

        // Build an "Image content" block from the caption — the vision model has already
        // extracted all visible text verbatim, so we feed it back into the answer prompt.
        // The model still receives the original images too (richer context), but having
        // the OCR'd text inline helps when the answering model misses small UI labels.
        var imageBlock = !string.IsNullOrWhiteSpace(imageCaption)
            ? $"Image content (extracted by vision OCR — treat as authoritative for text visible in the image):\n{imageCaption}\n\n"
            : "";

        if (citations.Count > 0)
        {
            var facts = string.Join("\n\n---\n\n",
                citations.Select((c, i) =>
                {
                    var header = $"[Source {i + 1}";
                    if (!string.IsNullOrEmpty(c.Title)) header += $" — {c.Title}";
                    if (!string.IsNullOrEmpty(c.Url)) header += $" — {c.Url}";
                    header += $" | relevance={c.Relevance:F2}]";
                    return $"{header}\n{c.Text}";
                }));

            system =
                "You are a knowledge-base assistant. Prefer the provided facts when answering. " +
                "If the facts are insufficient, say so honestly. " +
                "Cite sources you used as Markdown links: [Source 1](URL). " +
                "If a source has no URL, fall back to plain [Source N]. " +
                "Reply in the same language as the user's question.";
            user = $"{imageBlock}Facts:\n\n{facts}\n\nQuestion: {(string.IsNullOrWhiteSpace(question) ? "Based on the image(s) and facts above, help the user." : question)}";
        }
        else
        {
            system = hasImages
                ? "You are a helpful assistant. The user has attached image(s) and no matching facts were found in the knowledge base. " +
                  "Answer using only the image(s), the extracted image text, and your general knowledge. Reply in the same language as the user."
                : "You are a knowledge-base assistant. No matching facts were found. Tell the user honestly. Reply in the same language as the user's question.";
            user = imageBlock + (hasText ? $"Question: {question}" : "Please analyze the attached image(s).");
        }

        _logger.LogInformation("ASK calling Pollinations with {N} citations, {I} images", citations.Count, images?.Count ?? 0);

        string answerText;
        var answerSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            answerText = await _chat.ChatAsync(system, user, images, history, ct);
            answerSw.Stop();
            pipeline.Add(new PipelineStage("answer", true, answerSw.ElapsedMilliseconds, null, new
            {
                citationsUsed = citations.Count,
                imagesSent = images?.Count ?? 0,
                imageTextInPrompt = !string.IsNullOrWhiteSpace(imageBlock),
                historyTurns = history?.Count ?? 0,
                promptLength = user.Length,
                answerLength = answerText.Length,
                model = "pollinations"
            }));
        }
        catch (Exception ex)
        {
            answerSw.Stop();
            _logger.LogError(ex, "Chat call failed");
            pipeline.Add(new PipelineStage("answer", false, answerSw.ElapsedMilliseconds, ex.Message, null));
            return StatusCode(502, new { error = ex.Message, pipeline = new { stages = pipeline, totalMs = totalSw.ElapsedMilliseconds } });
        }
        totalSw.Stop();

        return Ok(new
        {
            answer = answerText,
            sources = citations.Select(c => new { c.DocumentId, c.Title, c.Url, c.SourceName, c.Link, c.Relevance }),
            imageCaption,
            searchQuery = string.IsNullOrWhiteSpace(searchQuery) ? null : searchQuery,
            pipeline = new { stages = pipeline, totalMs = totalSw.ElapsedMilliseconds }
        });
    }

    private record CitationVm(string DocumentId, string SourceName, string Link, string Text, double Relevance, string? Url, string? Title);
    private record PipelineStage(string Stage, bool Ok, long Ms, string? Error, object? Data);

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

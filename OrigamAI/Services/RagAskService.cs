using Microsoft.KernelMemory;

namespace OrigamAI.Services;

public record AskHistoryItem(string Role, string Content);

public record AskCitation(
    string DocumentId,
    string SourceName,
    string Link,
    string Text,
    double Relevance,
    string? Url,
    string? Title);

public record PipelineStage(string Stage, bool Ok, long Ms, string? Error, object? Data);
public record PipelineResult(IReadOnlyList<PipelineStage> Stages, long TotalMs);

public record AskSource(string DocumentId, string? Title, string? Url, string SourceName, string Link, double Relevance);

public record AskOutcome(
    bool ChatOk,
    string? ChatError,
    string Answer,
    IReadOnlyList<AskSource> Sources,
    string? ImageCaption,
    string? SearchQuery,
    PipelineResult Pipeline)
{
    public object ToResponseBody() => new
    {
        answer = Answer,
        sources = Sources,
        imageCaption = ImageCaption,
        searchQuery = SearchQuery,
        pipeline = new { stages = Pipeline.Stages, totalMs = Pipeline.TotalMs }
    };

    public object ToErrorBody() => new
    {
        error = ChatError,
        pipeline = new { stages = Pipeline.Stages, totalMs = Pipeline.TotalMs }
    };
}

public class RagAskService
{
    private readonly IKernelMemory _memory;
    private readonly PollinationsChatService _chat;
    private readonly OpenAiVisionService _vision;
    private readonly ILogger<RagAskService> _logger;

    public RagAskService(IKernelMemory memory, PollinationsChatService chat, OpenAiVisionService vision, ILogger<RagAskService> logger)
    {
        _memory = memory;
        _chat = chat;
        _vision = vision;
        _logger = logger;
    }

    public static List<ChatHistoryMessage>? BuildHistory(IReadOnlyList<AskHistoryItem>? items)
    {
        if (items is null || items.Count == 0) return null;
        var slice = items.Count > 10 ? items.Skip(items.Count - 10).ToList() : items.ToList();
        return slice
            .Where(i => !string.IsNullOrWhiteSpace(i.Content))
            .Select(i => new ChatHistoryMessage(i.Role == "assistant" ? "assistant" : "user", i.Content))
            .ToList();
    }

    public async Task<AskOutcome> AskAsync(
        string question,
        string? filterStr,
        IReadOnlyList<ChatImage>? images,
        IReadOnlyList<ChatHistoryMessage>? history,
        CancellationToken ct)
    {
        var hasText = !string.IsNullOrWhiteSpace(question);
        var hasImages = images is { Count: > 0 };

        var filter = ParseFilter(filterStr);

        _logger.LogInformation("ASK question='{Q}' filter={F} images={I}", question, filterStr, images?.Count ?? 0);

        var stages = new List<PipelineStage>();
        var totalSw = System.Diagnostics.Stopwatch.StartNew();

        var imageCaption = await CaptionImagesAsync(question, images, hasText, hasImages, stages, ct);
        var searchQuery = string.Join(" ", new[] { question, imageCaption }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        var citations = await SearchAsync(searchQuery, filter, filterStr, stages, ct);

        var (system, user) = BuildPrompt(question, hasText, hasImages, imageCaption, citations);

        _logger.LogInformation("ASK calling Pollinations with {N} citations, {I} images", citations.Count, images?.Count ?? 0);

        var answerSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var answerText = await _chat.ChatAsync(system, user, images, history, ct);
            answerSw.Stop();
            stages.Add(new PipelineStage("answer", true, answerSw.ElapsedMilliseconds, null, new
            {
                citationsUsed = citations.Count,
                imagesSent = images?.Count ?? 0,
                imageTextInPrompt = !string.IsNullOrWhiteSpace(imageCaption),
                historyTurns = history?.Count ?? 0,
                promptLength = user.Length,
                answerLength = answerText.Length,
                model = "pollinations"
            }));
            totalSw.Stop();

            var sources = citations
                .Select(c => new AskSource(c.DocumentId, c.Title, c.Url, c.SourceName, c.Link, c.Relevance))
                .ToList();

            return new AskOutcome(
                ChatOk: true,
                ChatError: null,
                Answer: answerText,
                Sources: sources,
                ImageCaption: imageCaption,
                SearchQuery: string.IsNullOrWhiteSpace(searchQuery) ? null : searchQuery,
                Pipeline: new PipelineResult(stages, totalSw.ElapsedMilliseconds));
        }
        catch (Exception ex)
        {
            answerSw.Stop();
            totalSw.Stop();
            _logger.LogError(ex, "Chat call failed");
            stages.Add(new PipelineStage("answer", false, answerSw.ElapsedMilliseconds, ex.Message, null));
            return new AskOutcome(
                ChatOk: false,
                ChatError: ex.Message,
                Answer: "",
                Sources: Array.Empty<AskSource>(),
                ImageCaption: imageCaption,
                SearchQuery: string.IsNullOrWhiteSpace(searchQuery) ? null : searchQuery,
                Pipeline: new PipelineResult(stages, totalSw.ElapsedMilliseconds));
        }
    }

    private static MemoryFilter? ParseFilter(string? filterStr)
    {
        if (string.IsNullOrWhiteSpace(filterStr)) return null;
        var parts = filterStr.Split('=', 2);
        return parts.Length == 2 ? MemoryFilters.ByTag(parts[0], parts[1]) : null;
    }

    private async Task<string?> CaptionImagesAsync(
        string question,
        IReadOnlyList<ChatImage>? images,
        bool hasText,
        bool hasImages,
        List<PipelineStage> stages,
        CancellationToken ct)
    {
        if (!hasImages)
        {
            stages.Add(new PipelineStage("caption", true, 0, null, new { skipped = true, reason = "no images attached" }));
            return null;
        }

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
            string caption;
            if (_vision.IsConfigured)
            {
                caption = await _vision.CaptionAsync(captionSystem, captionUser, images!, ct);
                captionProvider = "openai";
            }
            else
            {
                caption = await _chat.ChatAsync(captionSystem, captionUser, images, ct);
                captionProvider = "pollinations";
            }
            sw.Stop();
            _logger.LogInformation("ASK image caption ({P}): {C}", captionProvider, caption);
            stages.Add(new PipelineStage("caption", true, sw.ElapsedMilliseconds, null,
                new { caption, imageCount = images!.Count, provider = captionProvider }));
            return caption;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Image caption step failed; continuing without caption");
            stages.Add(new PipelineStage("caption", false, sw.ElapsedMilliseconds, ex.Message, null));
            return null;
        }
    }

    private async Task<List<AskCitation>> SearchAsync(
        string searchQuery,
        MemoryFilter? filter,
        string? filterStr,
        List<PipelineStage> stages,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            stages.Add(new PipelineStage("search", true, 0, null, new { skipped = true, reason = "no search query" }));
            return new List<AskCitation>();
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            const int searchLimit = 10;
            var search = await _memory.SearchAsync(searchQuery, filter: filter, minRelevance: 0, limit: searchLimit, cancellationToken: ct);
            var citations = search.Results
                .SelectMany(c => c.Partitions.Select(p => new AskCitation(
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
            sw.Stop();
            stages.Add(new PipelineStage("search", true, sw.ElapsedMilliseconds, null, new
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
            return citations;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Search step failed");
            stages.Add(new PipelineStage("search", false, sw.ElapsedMilliseconds, ex.Message, null));
            return new List<AskCitation>();
        }
    }

    private static (string System, string User) BuildPrompt(
        string question,
        bool hasText,
        bool hasImages,
        string? imageCaption,
        IReadOnlyList<AskCitation> citations)
    {
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

            var system =
                "You are a knowledge-base assistant. Prefer the provided facts when answering. " +
                "If the facts are insufficient, say so honestly. " +
                "Cite sources you used as Markdown links: [Source 1](URL). " +
                "If a source has no URL, fall back to plain [Source N]. " +
                "Reply in the same language as the user's question.";
            var user = $"{imageBlock}Facts:\n\n{facts}\n\nQuestion: {(string.IsNullOrWhiteSpace(question) ? "Based on the image(s) and facts above, help the user." : question)}";
            return (system, user);
        }

        var sysNoFacts = hasImages
            ? "You are a helpful assistant. The user has attached image(s) and no matching facts were found in the knowledge base. " +
              "Answer using only the image(s), the extracted image text, and your general knowledge. Reply in the same language as the user."
            : "You are a knowledge-base assistant. No matching facts were found. Tell the user honestly. Reply in the same language as the user's question.";
        var userNoFacts = imageBlock + (hasText ? $"Question: {question}" : "Please analyze the attached image(s).");
        return (sysNoFacts, userNoFacts);
    }
}

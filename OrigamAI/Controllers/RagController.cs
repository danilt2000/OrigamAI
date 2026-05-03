using Microsoft.AspNetCore.Mvc;
using OrigamAI.Services;

namespace OrigamAI.Controllers;

[ApiController]
[Route("api/rag")]
public class RagController : ControllerBase
{
    private readonly RagAskService _ask;
    private readonly RagIngestService _ingest;

    public RagController(RagAskService ask, RagIngestService ingest)
    {
        _ask = ask;
        _ingest = ingest;
    }

    public record IngestTextRequest(string Id, string Text, Dictionary<string, string>? Tags);
    public record AskRequest(string Question, string? Filter, List<AskHistoryItem>? History);
    public record IngestOrigamTopicRequest(string TopicIdOrUrl);

    [HttpPost("ingest-text")]
    public async Task<IActionResult> IngestText([FromBody] IngestTextRequest req, CancellationToken ct)
    {
        var documentId = await _ingest.IngestTextAsync(req.Id, req.Text, req.Tags, ct);
        return Ok(new { documentId });
    }

    [HttpPost("ingest-origam")]
    public async Task<IActionResult> IngestOrigam(
        [FromQuery] int? categoryId = null,
        [FromQuery] int maxTopicsPerCategory = 10,
        [FromQuery] int maxPages = 1,
        CancellationToken ct = default)
    {
        var summary = await _ingest.IngestOrigamAsync(categoryId, maxTopicsPerCategory, maxPages, ct);
        return Ok(new { count = summary.Count, items = summary.Items });
    }

    [HttpPost("ingest-origam-topic")]
    public async Task<IActionResult> IngestOrigamTopic([FromBody] IngestOrigamTopicRequest req, CancellationToken ct)
    {
        try
        {
            var r = await _ingest.IngestOrigamTopicAsync(req.TopicIdOrUrl, ct);
            return Ok(new
            {
                docId = r.DocId,
                title = r.Title,
                url = r.Url,
                posts = r.Posts,
                chars = r.Chars,
                imagesFound = r.ImagesFound,
                imagesDescribed = r.ImagesDescribed,
                images = r.Images.Select(i => new { url = i.Url, description = i.Description, error = i.Error })
            });
        }
        catch (IngestValidationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (IngestNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AskRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Question))
            return BadRequest(new { error = "Provide question text, image(s), or both." });
        var outcome = await _ask.AskAsync(req.Question, req.Filter, images: null, RagAskService.BuildHistory(req.History), ct);
        return outcome.ChatOk ? Ok(outcome.ToResponseBody()) : StatusCode(502, outcome.ToErrorBody());
    }

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
        var imgs = await ReadImagesAsync(images, ct);
        var hasText = !string.IsNullOrWhiteSpace(question);
        if (!hasText && imgs.Count == 0)
            return BadRequest(new { error = "Provide question text, image(s), or both." });

        var historyList = ParseHistory(history);
        var outcome = await _ask.AskAsync(question ?? "", filter, imgs.Count > 0 ? imgs : null, RagAskService.BuildHistory(historyList), ct);
        return outcome.ChatOk ? Ok(outcome.ToResponseBody()) : StatusCode(502, outcome.ToErrorBody());
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 5, CancellationToken ct = default)
    {
        var results = await _ingest.SearchAsync(q, limit, ct);
        return Ok(results);
    }

    [HttpDelete("document/{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await _ingest.DeleteDocumentAsync(id, ct);
        return NoContent();
    }

    private static async Task<List<ChatImage>> ReadImagesAsync(List<IFormFile>? images, CancellationToken ct)
    {
        var imgs = new List<ChatImage>();
        if (images is null) return imgs;
        foreach (var f in images)
        {
            if (f.Length == 0) continue;
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            imgs.Add(new ChatImage(ms.ToArray(), string.IsNullOrWhiteSpace(f.ContentType) ? "image/png" : f.ContentType));
        }
        return imgs;
    }

    private static List<AskHistoryItem>? ParseHistory(string? history)
    {
        if (string.IsNullOrWhiteSpace(history)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<AskHistoryItem>>(
                history,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}

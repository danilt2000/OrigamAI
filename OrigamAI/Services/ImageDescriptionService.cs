namespace OrigamAI.Services;

public record ImageDescription(string Url, string? Description, string? Error);

public class ImageDescriptionService
{
    private readonly HttpClient _http;
    private readonly OpenAiVisionService _vision;
    private readonly ILogger<ImageDescriptionService> _logger;

    private const string CaptionSystemPrompt =
        "You generate detailed, search-friendly descriptions of images for a documentation knowledge base. " +
        "Output a concise paragraph (2-5 sentences) capturing: " +
        "1) the apparent context (e.g. 'Origam Architect property editor for a Data Service Lookup'); " +
        "2) all visible UI text — labels, field names, menu items, button text, column headers — verbatim, preserving casing; " +
        "3) any diagrams, charts, tables, or numerical data with their values; " +
        "4) the apparent purpose of what is shown. " +
        "No markdown, no preamble like 'the image shows', just dense factual content suitable for vector embedding.";

    private const string CaptionUserPrompt = "Describe this image for a vector search index.";

    public ImageDescriptionService(HttpClient http, OpenAiVisionService vision, ILogger<ImageDescriptionService> logger)
    {
        _http = http;
        _vision = vision;
        _logger = logger;
    }

    public bool IsAvailable => _vision.IsConfigured;

    public async Task<ImageDescription> DescribeAsync(string imageUrl, CancellationToken ct)
    {
        if (!_vision.IsConfigured)
            return new ImageDescription(imageUrl, null, "OpenAI vision not configured");

        try
        {
            using var res = await _http.GetAsync(imageUrl, ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Image download failed {Url}: HTTP {Code}", imageUrl, res.StatusCode);
                return new ImageDescription(imageUrl, null, $"download failed: HTTP {(int)res.StatusCode}");
            }

            var bytes = await res.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0)
                return new ImageDescription(imageUrl, null, "empty image body");

            var mime = res.Content.Headers.ContentType?.MediaType ?? GuessMime(imageUrl);
            var image = new ChatImage(bytes, mime);

            _logger.LogInformation("Captioning image {Url} ({Bytes} bytes, {Mime})", imageUrl, bytes.Length, mime);
            var caption = await _vision.CaptionAsync(CaptionSystemPrompt, CaptionUserPrompt, new[] { image }, ct);
            return new ImageDescription(imageUrl, caption, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image description failed for {Url}", imageUrl);
            return new ImageDescription(imageUrl, null, ex.Message);
        }
    }

    private static string GuessMime(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.AbsolutePath : url;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png"
        };
    }
}

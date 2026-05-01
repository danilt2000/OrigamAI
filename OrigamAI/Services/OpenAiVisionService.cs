using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OrigamAI.Services;

public class OpenAiVisionService
{
    private readonly HttpClient _http;
    private readonly ILogger<OpenAiVisionService> _logger;
    private readonly string _model;
    public bool IsConfigured { get; }

    public OpenAiVisionService(HttpClient http, IConfiguration cfg, ILogger<OpenAiVisionService> logger)
    {
        _http = http;
        _logger = logger;
        var section = cfg.GetSection("OpenAI");
        _model = section["VisionModel"] ?? "gpt-4o-mini";

        var endpoint = section["Endpoint"] ?? "https://api.openai.com";
        _http.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");

        var apiKey = section["ApiKey"];
        IsConfigured = !string.IsNullOrWhiteSpace(apiKey);
        if (IsConfigured)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> CaptionAsync(string systemPrompt, string userPrompt, IReadOnlyList<ChatImage> images, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("OpenAI API key is not configured (OpenAI:ApiKey).");
        if (images is null || images.Count == 0)
            throw new ArgumentException("At least one image required.", nameof(images));

        var parts = new List<object>
        {
            new { type = "text", text = string.IsNullOrWhiteSpace(userPrompt) ? "Describe the attached image(s)." : userPrompt }
        };
        foreach (var img in images)
        {
            var dataUrl = $"data:{img.Mime};base64,{Convert.ToBase64String(img.Data)}";
            parts.Add(new { type = "image_url", image_url = new { url = dataUrl } });
        }

        var payload = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = parts }
            }
        };

        var json = JsonSerializer.Serialize(payload);
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var res = await _http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (!res.IsSuccessStatusCode)
        {
            _logger.LogError("OpenAI HTTP {Code}: {Body}", res.StatusCode, body);
            throw new HttpRequestException($"OpenAI returned {res.StatusCode}: {Trim(body)}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errEl))
        {
            var msg = errEl.TryGetProperty("message", out var m) ? m.GetString() : errEl.ToString();
            throw new InvalidOperationException($"OpenAI error: {msg}");
        }

        var content = root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        return content.Trim();
    }

    private static string Trim(string s, int max = 500) => s.Length <= max ? s : s[..max] + "…";
}

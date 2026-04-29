using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OrigamAI.Services;

public class PollinationsChatService
{
    private readonly HttpClient _http;
    private readonly ILogger<PollinationsChatService> _logger;
    private readonly string _model;

    public PollinationsChatService(HttpClient http, IConfiguration cfg, ILogger<PollinationsChatService> logger)
    {
        _http = http;
        _logger = logger;
        var section = cfg.GetSection("Pollinations");
        _model = section["TextModel"] ?? "openai";

        var endpoint = section["Endpoint"] ?? "https://text.pollinations.ai/openai";
        _http.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
        var apiKey = section["ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var payload = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
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
            _logger.LogError("Pollinations error {Code}: {Body}", res.StatusCode, body);
            throw new HttpRequestException($"Pollinations returned {res.StatusCode}: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        return content.Trim();
    }
}

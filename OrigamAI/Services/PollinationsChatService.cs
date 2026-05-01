using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OrigamAI.Services;

public record ChatImage(byte[] Data, string Mime);
public record ChatHistoryMessage(string Role, string Content);

public class PollinationsChatService
{
    private readonly HttpClient _http;
    private readonly ILogger<PollinationsChatService> _logger;
    private readonly string _model;
    private const int MaxAttempts = 4;
    private static readonly TimeSpan[] BackoffDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
    };

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

    public Task<string> ChatAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        => ChatAsync(systemPrompt, userPrompt, images: null, history: null, ct);

    public Task<string> ChatAsync(string systemPrompt, string userPrompt, IReadOnlyList<ChatImage>? images, CancellationToken ct = default)
        => ChatAsync(systemPrompt, userPrompt, images, history: null, ct);

    public async Task<string> ChatAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<ChatImage>? images,
        IReadOnlyList<ChatHistoryMessage>? history,
        CancellationToken ct = default)
    {
        object userContent;
        if (images is { Count: > 0 })
        {
            var parts = new List<object>
            {
                new { type = "text", text = string.IsNullOrWhiteSpace(userPrompt) ? "Describe the attached image(s)." : userPrompt }
            };
            foreach (var img in images)
            {
                var dataUrl = $"data:{img.Mime};base64,{Convert.ToBase64String(img.Data)}";
                parts.Add(new { type = "image_url", image_url = new { url = dataUrl } });
            }
            userContent = parts;
        }
        else
        {
            userContent = userPrompt;
        }

        var msgs = new List<object> { new { role = "system", content = systemPrompt } };
        if (history is { Count: > 0 })
        {
            foreach (var h in history)
            {
                if (string.IsNullOrWhiteSpace(h.Content)) continue;
                var role = h.Role == "assistant" ? "assistant" : "user";
                msgs.Add(new { role, content = h.Content });
            }
        }
        msgs.Add(new { role = "user", content = userContent });

        var payload = new
        {
            model = _model,
            messages = msgs.ToArray()
        };

        var json = JsonSerializer.Serialize(payload);
        var body = await SendWithRetryAsync(json, ct);

        // Defensive parsing — Pollinations occasionally returns 200 with an error payload,
        // a plain-text error string, or a slightly different schema (e.g. content as array of parts).
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            _logger.LogError("Pollinations returned non-JSON body: {Body}", body);
            throw new InvalidOperationException($"Pollinations returned non-JSON: {Trim(body)}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;

            // Surface upstream error fields if present (OpenAI-compat shape)
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var errEl))
            {
                var msg = errEl.ValueKind == JsonValueKind.String
                    ? errEl.GetString()
                    : errEl.TryGetProperty("message", out var m) ? m.GetString() : errEl.ToString();
                _logger.LogError("Pollinations error payload: {Err} | full body: {Body}", msg, body);
                throw new InvalidOperationException($"Pollinations error: {msg}");
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                _logger.LogError("Pollinations response missing 'choices': {Body}", body);
                throw new InvalidOperationException($"Pollinations response missing 'choices'. Body: {Trim(body)}");
            }

            var first = choices[0];
            if (!first.TryGetProperty("message", out var message))
            {
                _logger.LogError("Pollinations choice missing 'message': {Body}", body);
                throw new InvalidOperationException($"Pollinations choice missing 'message'. Body: {Trim(body)}");
            }

            if (!message.TryGetProperty("content", out var contentEl))
            {
                _logger.LogError("Pollinations message missing 'content': {Body}", body);
                throw new InvalidOperationException($"Pollinations message missing 'content'. Body: {Trim(body)}");
            }

            // content can be a plain string OR an array of {type,text} parts
            string content = contentEl.ValueKind switch
            {
                JsonValueKind.String => contentEl.GetString() ?? "",
                JsonValueKind.Array => string.Concat(contentEl.EnumerateArray()
                    .Where(p => p.TryGetProperty("type", out var t) && t.GetString() == "text"
                                && p.TryGetProperty("text", out _))
                    .Select(p => p.GetProperty("text").GetString() ?? "")),
                _ => contentEl.ToString()
            };

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Pollinations returned empty content. Body: {Body}", body);
            }

            return content.Trim();
        }
    }

    private async Task<string> SendWithRetryAsync(string json, CancellationToken ct)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                using var res = await _http.SendAsync(req, ct);
                var body = await res.Content.ReadAsStringAsync(ct);

                if (res.IsSuccessStatusCode)
                    return body;

                if (IsRetryableStatus(res.StatusCode) && attempt < MaxAttempts)
                {
                    var delay = BackoffDelays[Math.Min(attempt - 1, BackoffDelays.Length - 1)];
                    _logger.LogWarning(
                        "Pollinations HTTP {Code} on attempt {Attempt}/{Max}; retrying in {Delay}ms. Body: {Body}",
                        (int)res.StatusCode, attempt, MaxAttempts, delay.TotalMilliseconds, Trim(body));
                    await Task.Delay(delay, ct);
                    continue;
                }

                _logger.LogError("Pollinations HTTP {Code}: {Body}", res.StatusCode, body);
                throw new HttpRequestException($"Pollinations returned {res.StatusCode}: {Trim(body)}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                lastError = ex;
                var delay = BackoffDelays[Math.Min(attempt - 1, BackoffDelays.Length - 1)];
                _logger.LogWarning(ex,
                    "Pollinations network error on attempt {Attempt}/{Max}; retrying in {Delay}ms",
                    attempt, MaxAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested && attempt < MaxAttempts)
            {
                // HttpClient timeout (not user cancellation) surfaces as TaskCanceledException.
                lastError = ex;
                var delay = BackoffDelays[Math.Min(attempt - 1, BackoffDelays.Length - 1)];
                _logger.LogWarning(ex,
                    "Pollinations timeout on attempt {Attempt}/{Max}; retrying in {Delay}ms",
                    attempt, MaxAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
        }

        throw new HttpRequestException(
            $"Pollinations failed after {MaxAttempts} attempts: {lastError?.Message ?? "unknown error"}",
            lastError);
    }

    private static bool IsRetryableStatus(HttpStatusCode code)
    {
        var n = (int)code;
        return n == 408 || n == 425 || n == 429 || n >= 500;
    }

    private static string Trim(string s, int max = 500) =>
        s.Length <= max ? s : s[..max] + "…";
}

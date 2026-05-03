using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OrigamAI.Services;

public class OpenAiVisionService
{
    private readonly HttpClient _http;
    private readonly ILogger<OpenAiVisionService> _logger;
    private readonly string _model;
    public bool IsConfigured { get; }

    private const int MaxAttempts = 5;

    private static readonly TimeSpan[] BackoffDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
    };

    // Used when 429 keeps coming after we trust the API's "try again in Xms" hint.
    // Plateau at 30s = half a TPM window — virtually guaranteed to recover.
    private static readonly TimeSpan[] RateLimitFallbackDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30),
    };

    // After this many 429s in a row we stop trusting the API's hint and use fallback backoff.
    private const int RateLimitHintAttempts = 3;

    private static readonly Regex TryAgainInMsRegex = new(
        @"try again in\s+(\d+(?:\.\d+)?)\s*(ms|s)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        var body = await SendWithRetryAsync(json, ct);

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

    private async Task<string> SendWithRetryAsync(string json, CancellationToken ct)
    {
        var nonRateLimitAttempts = 0;
        var rateLimitAttempts = 0;

        while (true)
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

                // 429: rate limit — retry until success or cancellation. The condition is
                // by definition transient, so giving up just throws away in-flight work.
                if (res.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    rateLimitAttempts++;
                    var delay = ChooseRateLimitDelay(res, body, rateLimitAttempts);
                    _logger.LogWarning(
                        "OpenAI 429 (rate-limit attempt #{N}); waiting {Delay}ms. Body: {Body}",
                        rateLimitAttempts, delay.TotalMilliseconds, Trim(body));
                    await Task.Delay(delay, ct);
                    continue;
                }

                // 5xx / 408 / 425: finite retries — these may indicate persistent issues.
                if (IsRetryableStatus(res.StatusCode) && nonRateLimitAttempts < MaxAttempts - 1)
                {
                    nonRateLimitAttempts++;
                    var delay = BackoffDelays[Math.Min(nonRateLimitAttempts - 1, BackoffDelays.Length - 1)];
                    _logger.LogWarning(
                        "OpenAI HTTP {Code} on attempt {Attempt}/{Max}; retrying in {Delay}ms. Body: {Body}",
                        (int)res.StatusCode, nonRateLimitAttempts + 1, MaxAttempts, delay.TotalMilliseconds, Trim(body));
                    await Task.Delay(delay, ct);
                    continue;
                }

                _logger.LogError("OpenAI HTTP {Code}: {Body}", res.StatusCode, body);
                throw new HttpRequestException($"OpenAI returned {res.StatusCode}: {Trim(body)}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException ex) when (nonRateLimitAttempts < MaxAttempts - 1)
            {
                nonRateLimitAttempts++;
                var delay = BackoffDelays[Math.Min(nonRateLimitAttempts - 1, BackoffDelays.Length - 1)];
                _logger.LogWarning(ex,
                    "OpenAI network error on attempt {Attempt}/{Max}; retrying in {Delay}ms",
                    nonRateLimitAttempts + 1, MaxAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested && nonRateLimitAttempts < MaxAttempts - 1)
            {
                nonRateLimitAttempts++;
                var delay = BackoffDelays[Math.Min(nonRateLimitAttempts - 1, BackoffDelays.Length - 1)];
                _logger.LogWarning(ex,
                    "OpenAI timeout on attempt {Attempt}/{Max}; retrying in {Delay}ms",
                    nonRateLimitAttempts + 1, MaxAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, ct);
            }
        }
    }

    private static TimeSpan ChooseRateLimitDelay(HttpResponseMessage res, string body, int attempt)
    {
        // Trust the API's hint for the first few attempts. If 429s keep coming despite us
        // waiting exactly as told, the hint is unreliable — switch to fallback backoff.
        if (attempt <= RateLimitHintAttempts)
        {
            if (TryReadRetryAfter(res, out var retryAfter))
                return Clamp(retryAfter);
            if (TryParseTryAgainHint(body, out var hint))
                return Clamp(hint + TimeSpan.FromMilliseconds(500));
        }

        var idx = Math.Min(attempt - 1, RateLimitFallbackDelays.Length - 1);
        return RateLimitFallbackDelays[idx];
    }

    private static bool TryReadRetryAfter(HttpResponseMessage res, out TimeSpan delay)
    {
        delay = TimeSpan.Zero;
        if (res.Headers.RetryAfter is not { } ra) return false;
        if (ra.Delta is { } d) { delay = d; return true; }
        if (ra.Date is { } when_)
        {
            var diff = when_ - DateTimeOffset.UtcNow;
            if (diff > TimeSpan.Zero) { delay = diff; return true; }
        }
        return false;
    }

    private static bool TryParseTryAgainHint(string body, out TimeSpan hint)
    {
        hint = TimeSpan.Zero;
        var match = TryAgainInMsRegex.Match(body);
        if (!match.Success) return false;
        if (!double.TryParse(match.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
            return false;
        var unit = match.Groups[2].Value.ToLowerInvariant();
        hint = unit == "ms" ? TimeSpan.FromMilliseconds(n) : TimeSpan.FromSeconds(n);
        return true;
    }

    private static TimeSpan Clamp(TimeSpan d)
    {
        if (d < TimeSpan.FromMilliseconds(200)) return TimeSpan.FromMilliseconds(200);
        if (d > TimeSpan.FromSeconds(60)) return TimeSpan.FromSeconds(60);
        return d;
    }

    private static bool IsRetryableStatus(HttpStatusCode code)
    {
        var n = (int)code;
        return n == 408 || n == 425 || n == 429 || n >= 500;
    }

    private static string Trim(string s, int max = 500) => s.Length <= max ? s : s[..max] + "…";
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace UniversalITMonitor.Application.Notifications;

public sealed class TelegramNotificationSender : ITelegramNotificationSender, IDisposable
{
    private readonly HttpClient _client = new(new SocketsHttpHandler())
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    public async Task<NotificationTransportResult> SendAsync(
        string botToken,
        string chatId,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            return new(false, "Telegram is not fully configured.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.telegram.org/bot{botToken}/sendMessage")
        {
            Content = JsonContent.Create(new TelegramMessageRequest(chatId, text))
        };
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var apiResult = await response.Content.ReadFromJsonAsync<TelegramApiResponse>(cancellationToken);
        if (response.IsSuccessStatusCode && apiResult?.Ok == true) return new(true, "Sent.");

        var transient = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
            (int)response.StatusCode >= 500 || apiResult?.ErrorCode == (int)HttpStatusCode.TooManyRequests;
        var detail = string.IsNullOrWhiteSpace(apiResult?.Description)
            ? $"Telegram returned HTTP {(int)response.StatusCode} {Reason(response.StatusCode)}."
            : Limit(apiResult.Description, 256);
        if (response.StatusCode == HttpStatusCode.NotFound || apiResult?.ErrorCode == (int)HttpStatusCode.NotFound)
        {
            detail = "Telegram returned 404 Not Found. Check that the saved bot token is the current token from BotFather.";
        }
        TimeSpan? retryAfter = apiResult?.Parameters?.RetryAfter is > 0 and var seconds
            ? TimeSpan.FromSeconds(Math.Min(seconds, 86400))
            : null;
        return new(false, detail, transient, retryAfter);
    }

    public void Dispose() => _client.Dispose();

    private static string Reason(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "Unauthorized",
        HttpStatusCode.Forbidden => "Forbidden",
        HttpStatusCode.TooManyRequests => "Rate limited",
        >= HttpStatusCode.InternalServerError => "Server error",
        _ => "Request rejected"
    };

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];

    private sealed record TelegramMessageRequest(
        [property: JsonPropertyName("chat_id")] string ChatId,
        [property: JsonPropertyName("text")] string Text);

    private sealed class TelegramApiResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; init; }

        [JsonPropertyName("parameters")]
        public TelegramResponseParameters? Parameters { get; init; }
    }

    private sealed class TelegramResponseParameters
    {
        [JsonPropertyName("retry_after")]
        public int? RetryAfter { get; init; }
    }
}

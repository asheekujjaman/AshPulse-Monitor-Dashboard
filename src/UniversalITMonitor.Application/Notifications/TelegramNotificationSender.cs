using System.Net;
using System.Net.Http.Json;

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
        if (response.IsSuccessStatusCode) return new(true, "Sent.");
        var transient = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
            (int)response.StatusCode >= 500;
        return new(false, $"Telegram returned HTTP {(int)response.StatusCode} {Reason(response.StatusCode)}.", transient);
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

    private sealed record TelegramMessageRequest(string Chat_Id, string Text);
}

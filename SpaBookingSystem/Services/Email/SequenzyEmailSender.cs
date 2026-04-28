using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SpaBookingSystem.Api.Options;

namespace SpaBookingSystem.Api.Services.Email;

public class SequenzyEmailSender : IEmailSender
{
    private readonly HttpClient _client;
    private readonly EmailOptions _options;
    private readonly ILogger<SequenzyEmailSender> _logger;

    private record SequenzyEmailRequest(
        string to,
        string subject,
        string body,
        string? from = null);

    public SequenzyEmailSender(HttpClient client, IOptions<EmailOptions> options, ILogger<SequenzyEmailSender> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;

        _client.BaseAddress = new Uri("https://api.sequenzy.com/api/v1/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.SequenzyApiKey);
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var from = string.IsNullOrWhiteSpace(_options.SequenzyFrom)
            ? $"{_options.SenderName} <{_options.SenderEmail}>"
            : _options.SequenzyFrom!;

        var payload = new SequenzyEmailRequest(
            to: toEmail,
            subject: subject,
            body: htmlBody,
            from: from);

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _client.PostAsync("transactional/send", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Sequenzy send failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Sequenzy send failed: {response.StatusCode}");
        }

        _logger.LogInformation("Sequenzy email queued to {To}", toEmail);
    }
}

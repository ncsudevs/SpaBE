using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpaBookingSystem.Services.Options;

namespace SpaBookingSystem.Services.Email;

public class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _client;
    private readonly EmailOptions _options;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(HttpClient client, IOptions<EmailOptions> options, ILogger<ResendEmailSender> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;

        _client.BaseAddress = new Uri("https://api.resend.com/");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ResendApiKey);
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var from = string.IsNullOrWhiteSpace(_options.ResendFrom)
            ? $"{_options.SenderName} <{_options.SenderEmail}>"
            : _options.ResendFrom!;

        var payload = new
        {
            from,
            to = new[] { toEmail },
            subject,
            html = htmlBody
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _client.PostAsync("emails", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Resend send failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Resend send failed: {response.StatusCode}");
        }

        _logger.LogInformation("Resend email queued to {To}", toEmail);
    }
}

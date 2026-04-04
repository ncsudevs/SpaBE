using Microsoft.Extensions.Options;
using SpaBookingSystem.Api.Options;
using System.Text;

namespace SpaBookingSystem.Api.Services.Email;

public class FileEmailSender : IEmailSender
{
    private readonly IWebHostEnvironment _environment;
    private readonly EmailOptions _options;
    private readonly ILogger<FileEmailSender> _logger;

    public FileEmailSender(IWebHostEnvironment environment, IOptions<EmailOptions> options, ILogger<FileEmailSender> logger)
    {
        _environment = environment;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var pickupFolder = Path.Combine(_environment.ContentRootPath, _options.PickupFolder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(pickupFolder);

        var safeFileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.html";
        var filePath = Path.Combine(pickupFolder, safeFileName);

        var content = new StringBuilder()
            .AppendLine($"From: {_options.SenderName} <{_options.SenderEmail}>")
            .AppendLine($"To: {toEmail}")
            .AppendLine($"Subject: {subject}")
            .AppendLine($"CreatedAtUtc: {DateTime.UtcNow:O}")
            .AppendLine("Content-Type: text/html; charset=utf-8")
            .AppendLine()
            .AppendLine(htmlBody)
            .ToString();

        await File.WriteAllTextAsync(filePath, content, cancellationToken);
        _logger.LogInformation("Email written to pickup folder for {ToEmail}: {FilePath}", toEmail, filePath);
    }
}

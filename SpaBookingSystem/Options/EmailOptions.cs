namespace SpaBookingSystem.Api.Options;

public class EmailOptions
{
    public const string SectionName = "Email";
    public string SenderName { get; set; } = "SuSpa";
    public string SenderEmail { get; set; } = "hello@sequenzy-9ffb14.sequenzymail.com";
    public string PickupFolder { get; set; } = "wwwroot/email-pickup";
    public string Provider { get; set; } = "File"; // File | Resend | Sequenzy
    public string? ResendApiKey { get; set; }
    public string? ResendFrom { get; set; }
    public string? SequenzyApiKey { get; set; }
    public string? SequenzyFrom { get; set; }
}

namespace SpaBookingSystem.Api.Options;

public class EmailOptions
{
    public const string SectionName = "Email";
    public string SenderName { get; set; } = "SuSpa";
    public string SenderEmail { get; set; } = "nguyenchisu.10a4@gmail.com";
    public string PickupFolder { get; set; } = "wwwroot/email-pickup";
}

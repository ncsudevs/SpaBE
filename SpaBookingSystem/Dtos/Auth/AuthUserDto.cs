namespace SpaBookingSystem.Api.Dtos.Auth;

public class AuthUserDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Role { get; set; } = string.Empty;
    public string UserType { get; set; } = string.Empty;
}

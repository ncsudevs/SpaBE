namespace SpaBookingSystem.Services.Auth;

public interface ITokenService
{
    string GenerateToken(TokenUser user);
}

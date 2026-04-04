using SpaBookingSystem.Api.Models.Auth;

namespace SpaBookingSystem.Api.Services;

public interface ITokenService
{
    string GenerateToken(TokenUser user);
}

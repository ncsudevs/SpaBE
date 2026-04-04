namespace SpaBookingSystem.Services;

public class PasswordService : IPasswordService
{
    public string Hash(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash)) return false;
        return BCrypt.Net.BCrypt.Verify(password, passwordHash);
    }
}

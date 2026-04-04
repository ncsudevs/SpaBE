using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.DataLayer;

namespace SpaBookingSystem.Services;

public class AdminSeedService : IAdminSeedService
{
    private readonly SpaDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IPasswordService _passwordService;

    public AdminSeedService(SpaDbContext db, IConfiguration configuration, IPasswordService passwordService)
    {
        _db = db;
        _configuration = configuration;
        _passwordService = passwordService;
    }

    public async Task SeedAsync()
    {
        var seedSection = _configuration.GetSection("DefaultAdmin");
        var email = seedSection["Email"]?.Trim().ToLower();
        var password = seedSection["Password"]?.Trim();
        var fullName = seedSection["FullName"]?.Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(fullName))
            return;

        var existing = await _db.Admins.FirstOrDefaultAsync(x => x.Email.ToLower() == email);
        if (existing != null) return;

        _db.Admins.Add(new Admin
        {
            FullName = fullName,
            Email = email,
            PasswordHash = _passwordService.Hash(password),
            Role = "ADMIN",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
    }
}

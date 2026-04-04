using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.Auth;
using SpaBookingSystem.Api.Models.Auth;
using SpaBookingSystem.Api.Services;
using SpaBookingSystem.Services;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.DataLayer;
using System.Security.Claims;
using SpaBookingSystem.Api.Helpers;
using SpaBookingSystem.Api.Services.Email;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly SpaDbContext _db;
    private readonly IPasswordService _passwordService;
    private readonly ITokenService _tokenService;
    private readonly IEmailSender _emailSender;

    public AuthController(SpaDbContext db, IPasswordService passwordService, ITokenService tokenService, IEmailSender emailSender)
    {
        _db = db;
        _passwordService = passwordService;
        _tokenService = tokenService;
        _emailSender = emailSender;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterDto dto)
    {
        var normalizedEmail = dto.Email.Trim().ToLower();

        var existsInCustomers = await _db.Customers.AnyAsync(x => x.Email.ToLower() == normalizedEmail);
        var existsInAdmins = await _db.Admins.AnyAsync(x => x.Email.ToLower() == normalizedEmail);
        if (existsInCustomers || existsInAdmins)
            return Conflict(new { message = "Email is already registered" });
        string? normalizedPhone = null;

        if (!string.IsNullOrWhiteSpace(dto.Phone))
        {
            if (!PhoneHelper.TryNormalizePhone(dto.Phone, dto.Region, out var parsedPhone, out var phoneError))
                return BadRequest(new { message = phoneError });

            var existsPhoneInCustomers = await _db.Customers.AnyAsync(x => x.Phone != null && x.Phone == parsedPhone);
            if (existsPhoneInCustomers)
                return Conflict(new { message = "Phone number is already registered" });

            normalizedPhone = parsedPhone;
        }
        var customer = new Customer
        {
            FullName = dto.FullName.Trim(),
            Email = normalizedEmail,
            Phone = normalizedPhone,
            PasswordHash = _passwordService.Hash(dto.Password),
            Role = "CUSTOMER",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var registerCode = $"REG-{customer.Id:D6}";
        await _emailSender.SendAsync(customer.Email, "SuSpa registration successful", EmailTemplateService.BuildRegisterTemplate(customer.FullName, registerCode));

        var user = new AuthUserDto
        {
            Id = customer.Id,
            FullName = customer.FullName,
            Email = customer.Email,
            Phone = customer.Phone,
            Role = customer.Role,
            UserType = "CUSTOMER"
        };

        return Ok(new AuthResponseDto
        {
            Token = _tokenService.GenerateToken(new TokenUser
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                UserType = user.UserType
            }),
            User = user
        });
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        var normalizedEmail = dto.Email.Trim().ToLower();

        var admin = await _db.Admins.FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail);
        if (admin != null)
        {
            if (!admin.IsActive)
                return Unauthorized(new { message = "This account has been deactivated" });

            if (!_passwordService.Verify(dto.Password, admin.PasswordHash))
                return Unauthorized(new { message = "Invalid email or password" });

            var adminUser = new AuthUserDto
            {
                Id = admin.Id,
                FullName = admin.FullName,
                Email = admin.Email,
                Role = admin.Role,
                UserType = "ADMIN"
            };

            return Ok(new AuthResponseDto
            {
                Token = _tokenService.GenerateToken(new TokenUser
                {
                    Id = adminUser.Id,
                    FullName = adminUser.FullName,
                    Email = adminUser.Email,
                    Role = adminUser.Role,
                    UserType = adminUser.UserType
                }),
                User = adminUser
            });
        }

        var customer = await _db.Customers.FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail);
        if (customer == null || !_passwordService.Verify(dto.Password, customer.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password" });

        if (!customer.IsActive)
            return Unauthorized(new { message = "This account has been deactivated" });

        var customerUser = new AuthUserDto
        {
            Id = customer.Id,
            FullName = customer.FullName,
            Email = customer.Email,
            Phone = customer.Phone,
            Role = customer.Role,
            UserType = "CUSTOMER"
        };

        return Ok(new AuthResponseDto
        {
            Token = _tokenService.GenerateToken(new TokenUser
            {
                Id = customerUser.Id,
                FullName = customerUser.FullName,
                Email = customerUser.Email,
                Role = customerUser.Role,
                UserType = customerUser.UserType
            }),
            User = customerUser
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AuthUserDto>> Me()
    {
        var email = User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLower();
        var role = User.FindFirstValue(ClaimTypes.Role);

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(role))
            return Unauthorized(new { message = "Invalid token" });

        if (role == "ADMIN")
        {
            var admin = await _db.Admins.AsNoTracking().FirstOrDefaultAsync(x => x.Email.ToLower() == email);
            if (admin == null) return Unauthorized(new { message = "Account not found" });

            return Ok(new AuthUserDto
            {
                Id = admin.Id,
                FullName = admin.FullName,
                Email = admin.Email,
                Role = admin.Role,
                UserType = "ADMIN"
            });
        }

        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Email.ToLower() == email);
        if (customer == null) return Unauthorized(new { message = "Account not found" });

        return Ok(new AuthUserDto
        {
            Id = customer.Id,
            FullName = customer.FullName,
            Email = customer.Email,
            Phone = customer.Phone,
            Role = customer.Role,
            UserType = "CUSTOMER"
        });
    }
}

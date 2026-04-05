using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.Staff;
using SpaBookingSystem.Api.Helpers;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.DataLayer;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/staff")]
public class StaffController : ControllerBase
{
    private readonly SpaDbContext _db;

    public StaffController(SpaDbContext db)
    {
        _db = db;
    }

    [Authorize(Roles = "ADMIN")]
    [HttpGet]
    public async Task<ActionResult<List<StaffDto>>> GetAll()
    {
        var data = await _db.Staffs
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.FullName)
            .ToListAsync();

        return Ok(data.Select(Map));
    }

    [Authorize(Roles = "ADMIN")]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<StaffDto>> GetById(int id)
    {
        var entity = await _db.Staffs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return NotFound(new { message = "Staff not found" });
        return Ok(Map(entity));
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPost]
    public async Task<ActionResult<StaffDto>> Create(StaffCreateUpdateDto dto)
    {
        var validation = await ValidateAsync(dto, null);
        if (validation is not null) return validation;

        var entity = new Staff
        {
            FullName = dto.FullName.Trim(),
            Email = NormalizeEmail(dto.Email),
            Phone = await NormalizePhoneAsync(dto.Phone),
            Skills = string.IsNullOrWhiteSpace(dto.Skills) ? null : dto.Skills.Trim(),
            IsActive = dto.IsActive,
            MaxConcurrent = dto.MaxConcurrent < 1 ? 1 : dto.MaxConcurrent,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Staffs.Add(entity);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, Map(entity));
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<StaffDto>> Update(int id, StaffCreateUpdateDto dto)
    {
        var entity = await _db.Staffs.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return NotFound(new { message = "Staff not found" });

        var validation = await ValidateAsync(dto, id);
        if (validation is not null) return validation;

        entity.FullName = dto.FullName.Trim();
        entity.Email = NormalizeEmail(dto.Email);
        entity.Phone = await NormalizePhoneAsync(dto.Phone);
        entity.Skills = string.IsNullOrWhiteSpace(dto.Skills) ? null : dto.Skills.Trim();
        entity.IsActive = dto.IsActive;
        entity.MaxConcurrent = dto.MaxConcurrent < 1 ? 1 : dto.MaxConcurrent;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(Map(entity));
    }

    [Authorize(Roles = "ADMIN")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.Staffs.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return NotFound(new { message = "Staff not found" });

        var inUse = await _db.BookingDetails.AnyAsync(x => x.StaffId == id);
        if (inUse) return Conflict(new { message = "Cannot delete: staff is already assigned to bookings." });

        _db.Staffs.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<ActionResult?> ValidateAsync(StaffCreateUpdateDto dto, int? id)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName))
            return BadRequest(new { message = "Full name is required." });

        var email = NormalizeEmail(dto.Email);
        if (!string.IsNullOrWhiteSpace(email))
        {
            var existsEmail = await _db.Staffs.AnyAsync(x => x.Email == email && x.Id != id);
            if (existsEmail)
                return Conflict(new { message = "Email is already used by another staff." });
        }

        var phone = await NormalizePhoneAsync(dto.Phone);
        if (!string.IsNullOrWhiteSpace(phone))
        {
            var existsPhone = await _db.Staffs.AnyAsync(x => x.Phone == phone && x.Id != id);
            if (existsPhone)
                return Conflict(new { message = "Phone number is already used by another staff." });
        }

        return null;
    }

    private static string? NormalizeEmail(string? email)
    {
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    }

    private static async Task<string?> NormalizePhoneAsync(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        if (!PhoneHelper.TryNormalizePhone(phone, "VN", out var parsed, out _))
            return null;
        return await Task.FromResult(parsed);
    }

    private static StaffDto Map(Staff staff) => new()
    {
        Id = staff.Id,
        FullName = staff.FullName,
        Email = staff.Email,
        Phone = staff.Phone,
        Skills = staff.Skills,
        IsActive = staff.IsActive,
        MaxConcurrent = staff.MaxConcurrent,
        CreatedAt = staff.CreatedAt,
        UpdatedAt = staff.UpdatedAt
    };
}

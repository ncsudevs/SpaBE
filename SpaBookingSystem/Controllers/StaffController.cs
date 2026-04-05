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
            .Include(x => x.StaffCategories)
            .ThenInclude(sc => sc.Category)
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.FullName)
            .ToListAsync();

        return Ok(data.Select(Map));
    }

    [Authorize(Roles = "ADMIN")]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<StaffDto>> GetById(int id)
    {
        var entity = await _db.Staffs
            .AsNoTracking()
            .Include(x => x.StaffCategories)
            .ThenInclude(sc => sc.Category)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return NotFound(new { message = "Staff not found" });
        return Ok(Map(entity));
    }

    [Authorize(Roles = "ADMIN")]
    [HttpGet("{id:int}/schedule")]
    public async Task<ActionResult<List<StaffScheduleDto>>> GetSchedule(int id, [FromQuery] DateOnly? date)
    {
        var staffExists = await _db.Staffs.AsNoTracking().AnyAsync(x => x.Id == id);
        if (!staffExists) return NotFound(new { message = "Staff not found" });

        var query = _db.BookingDetails
            .AsNoTracking()
            .Include(d => d.Service)
            .Include(d => d.Booking)
            .Where(d => d.StaffId == id && d.Booking != null && d.Booking.Status != "CANCELLED");

        if (date.HasValue)
        {
            query = query.Where(d => d.AppointmentDate == date.Value);
        }

        var data = await query
            .OrderBy(d => d.AppointmentDate)
            .ThenBy(d => d.AppointmentTime)
            .ToListAsync();

        var schedule = data.Select(d => new StaffScheduleDto
        {
            BookingDetailId = d.Id,
            BookingId = d.BookingId,
            BookingCode = d.Booking?.BookingCode ?? string.Empty,
            AppointmentDate = d.AppointmentDate,
            AppointmentTime = d.AppointmentTime ?? string.Empty,
            ServiceName = d.Service?.Name ?? string.Empty,
            Duration = d.Service?.Duration ?? 0,
            Quantity = d.Quantity,
            CustomerName = d.Booking?.FullName ?? string.Empty,
            CustomerEmail = d.Booking?.Email ?? string.Empty,
            Status = d.Booking?.Status ?? string.Empty
        }).ToList();

        return Ok(schedule);
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
            Phone = dto.Phone,
            Skills = string.IsNullOrWhiteSpace(dto.Skills) ? null : dto.Skills.Trim(),
            IsActive = dto.IsActive,
            MaxConcurrent = dto.MaxConcurrent < 1 ? 1 : dto.MaxConcurrent,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Staffs.Add(entity);
        await _db.SaveChangesAsync();

        await UpsertCategoriesAsync(entity, dto.CategoryIds);
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
        entity.Phone = dto.Phone;
        entity.Skills = string.IsNullOrWhiteSpace(dto.Skills) ? null : dto.Skills.Trim();
        entity.IsActive = dto.IsActive;
        entity.MaxConcurrent = dto.MaxConcurrent < 1 ? 1 : dto.MaxConcurrent;
        entity.UpdatedAt = DateTime.UtcNow;

        await UpsertCategoriesAsync(entity, dto.CategoryIds);
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

        if (!string.IsNullOrWhiteSpace(dto.Phone))
        {
            if (!PhoneHelper.TryNormalizePhone(dto.Phone, "VN", out var parsedPhone, out var phoneError))
                return BadRequest(new { message = phoneError });

            dto.Phone = parsedPhone;

            var existsPhone = await _db.Staffs.AnyAsync(x => x.Phone == dto.Phone && x.Id != id);
            if (existsPhone)
                return Conflict(new { message = "Phone number is already used by another staff." });
        }

        return null;
    }

    private static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private async Task UpsertCategoriesAsync(Staff staff, List<int> categoryIds)
    {
        var normalized = categoryIds?.Distinct().ToList() ?? new List<int>();
        await _db.Entry(staff).Collection(x => x.StaffCategories).LoadAsync();

        staff.StaffCategories.Clear();
        if (normalized.Count == 0) return;

        var validIds = await _db.ServiceCategories
            .Where(c => normalized.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync();

        foreach (var catId in validIds)
        {
            staff.StaffCategories.Add(new StaffServiceCategory
            {
                StaffId = staff.Id,
                CategoryId = catId
            });
        }
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
        UpdatedAt = staff.UpdatedAt,
        CategoryIds = staff.StaffCategories?.Select(sc => sc.CategoryId).ToList() ?? new(),
        CategoryNames = staff.StaffCategories?
            .Where(sc => sc.Category != null)
            .Select(sc => sc.Category!.Name)
            .ToList() ?? new()
    };
}

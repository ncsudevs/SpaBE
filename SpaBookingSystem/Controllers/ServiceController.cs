using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.Services;
using SpaBookingSystem.Api.Helpers;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.DataLayer;
using System.Security.Claims;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/services")]
public class ServiceController : ControllerBase
{
    private readonly SpaDbContext _db;
    private readonly IWebHostEnvironment _environment;

    public ServiceController(SpaDbContext db, IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    [HttpGet]
    public async Task<ActionResult<List<ServiceDto>>> GetAll([FromQuery] int? categoryId)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

        var query = _db.Services
            .AsNoTracking()
            .Include(x => x.Category)
            .AsQueryable();

        if (role != "ADMIN")
            query = query.Where(x => x.Status == "ACTIVE");

        if (categoryId.HasValue)
            query = query.Where(x => x.CategoryId == categoryId.Value);

        var data = await query
            .OrderBy(x => x.Name)
            .Select(x => new ServiceDto
            {
                Id = x.Id,
                Name = x.Name,
                Description = x.Description,
                Price = x.Price,
                Duration = x.Duration,
                Status = x.Status,
                SlotCapacity = x.SlotCapacity,
                CategoryId = x.CategoryId,
                CategoryName = x.Category != null ? x.Category.Name : null,
                ImageUrl = x.ImageUrl
            })
            .ToListAsync();

        return Ok(data);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServiceDto>> GetById(int id)
    {
        var entity = await _db.Services
            .AsNoTracking()
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (entity == null) return NotFound(new { message = "Service not found" });

        if (entity.Status != "ACTIVE" && (User.FindFirstValue(ClaimTypes.Role) ?? string.Empty) != "ADMIN")
            return NotFound(new { message = "Service not found" });

        return Ok(new ServiceDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Price = entity.Price,
            Duration = entity.Duration,
            Status = entity.Status,
            SlotCapacity = entity.SlotCapacity,
            CategoryId = entity.CategoryId,
            CategoryName = entity.Category?.Name,
            ImageUrl = entity.ImageUrl
        });
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPost]
    public async Task<ActionResult<ServiceDto>> Create([FromForm] ServiceCreateDto dto)
    {
        var categoryExists = await _db.ServiceCategories.AnyAsync(x => x.Id == dto.CategoryId);
       

        var imageUrl = await FileStorageHelper.SaveServiceImageAsync(dto.ImageFile, _environment);

        var entity = new Service
        {
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            Price = dto.Price,
            Duration = dto.Duration,
            Status = string.IsNullOrWhiteSpace(dto.Status) ? "ACTIVE" : dto.Status.Trim().ToUpperInvariant(),
            SlotCapacity = dto.SlotCapacity <= 0 ? 5 : dto.SlotCapacity,
            CategoryId = dto.CategoryId,
            ImageUrl = imageUrl,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.Services.Add(entity);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, new ServiceDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Price = entity.Price,
            Duration = entity.Duration,
            Status = entity.Status,
            SlotCapacity = entity.SlotCapacity,
            CategoryId = entity.CategoryId,
            CategoryName = null,
            ImageUrl = entity.ImageUrl
        });
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromForm] ServiceUpdateDto dto)
    {
        var entity = await _db.Services.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return NotFound(new { message = "Service not found" });

        var categoryExists = await _db.ServiceCategories.AnyAsync(x => x.Id == dto.CategoryId);
        if (!categoryExists) return BadRequest(new { message = "Category does not exist" });

        var imageUrl = await FileStorageHelper.SaveServiceImageAsync(dto.ImageFile, _environment);
        if (!string.IsNullOrWhiteSpace(imageUrl))
            entity.ImageUrl = imageUrl;

        entity.Name = dto.Name.Trim();
        entity.Description = dto.Description?.Trim();
        entity.Price = dto.Price;
        entity.Duration = dto.Duration;
        entity.SlotCapacity = dto.SlotCapacity <= 0 ? entity.SlotCapacity : dto.SlotCapacity;
        entity.Status = string.IsNullOrWhiteSpace(dto.Status) ? entity.Status : dto.Status.Trim().ToUpperInvariant();
        entity.CategoryId = dto.CategoryId;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = "ADMIN")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.Services.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return NotFound(new { message = "Service not found" });

        _db.Services.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

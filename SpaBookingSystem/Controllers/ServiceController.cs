using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.Services;
using SpaBookingSystem.Api.Helpers;
using SpaBookingSystem.DataLayer;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/services")]
public class ServicesController : ControllerBase
{
    private readonly SpaDbContext _db;
    private readonly IWebHostEnvironment _env;

    public ServicesController(SpaDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    [HttpGet]
    public async Task<ActionResult<List<ServiceDto>>> GetAll([FromQuery] int? categoryId)
    {
        var query = _db.Services
            .AsNoTracking()
            .Include(s => s.Category)
            .AsQueryable();

        // Category filtering is optional so the same endpoint supports both all-services and category pages.
        if (categoryId.HasValue)
        {
            query = query.Where(s => s.CategoryId == categoryId.Value);
        }

        var data = await query
            .OrderByDescending(s => s.Id)
            .Select(s => new ServiceDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                Price = s.Price,
                Duration = s.Duration,
                Status = s.Status,
                CategoryId = s.CategoryId,
                CategoryName = s.Category != null ? s.Category.Name : null,
                ImageUrl = s.ImageUrl
            })
            .ToListAsync();

        return Ok(data);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServiceDto>> GetById(int id)
    {
        var s = await _db.Services
            .AsNoTracking()
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (s == null) return NotFound();

        return Ok(new ServiceDto
        {
            Id = s.Id,
            Name = s.Name,
            Description = s.Description,
            Price = s.Price,
            Duration = s.Duration,
            Status = s.Status,
            CategoryId = s.CategoryId,
            CategoryName = s.Category?.Name,
            ImageUrl = s.ImageUrl
        });
    }

    [HttpPost]
    public async Task<ActionResult<ServiceDto>> Create([FromForm] ServiceCreateDto dto)
    {
        var catExists = await _db.ServiceCategories.AnyAsync(c => c.Id == dto.CategoryId);
        if (!catExists) return BadRequest($"CategoryId {dto.CategoryId} not found");

        string? imageUrl = null;
        if (dto.ImageFile != null)
        {
            imageUrl = await FileStorageHelper.SaveServiceImageAsync(dto.ImageFile, _env);
        }

        var entity = new SpaBookingSystem.ApplicationCore.Entities.Service
        {
            Name = dto.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            Price = dto.Price,
            Duration = dto.Duration,
            Status = string.IsNullOrWhiteSpace(dto.Status) ? "ACTIVE" : dto.Status.Trim(),
            CategoryId = dto.CategoryId,
            ImageUrl = imageUrl
        };

        _db.Services.Add(entity);
        await _db.SaveChangesAsync();

        var catName = await _db.ServiceCategories
            .Where(c => c.Id == entity.CategoryId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync();

        var result = new ServiceDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Price = entity.Price,
            Duration = entity.Duration,
            Status = entity.Status,
            CategoryId = entity.CategoryId,
            CategoryName = catName,
            ImageUrl = entity.ImageUrl
        };

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromForm] ServiceUpdateDto dto)
    {
        var entity = await _db.Services.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return NotFound();

        var catExists = await _db.ServiceCategories.AnyAsync(c => c.Id == dto.CategoryId);
        if (!catExists) return BadRequest($"CategoryId {dto.CategoryId} not found");

        entity.Name = dto.Name.Trim();
        entity.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        entity.Price = dto.Price;
        entity.Duration = dto.Duration;
        entity.Status = string.IsNullOrWhiteSpace(dto.Status) ? "ACTIVE" : dto.Status.Trim();
        entity.CategoryId = dto.CategoryId;

        // Replacing the image removes the previous physical file first to avoid orphaned uploads.
        if (dto.ImageFile != null)
        {
            FileStorageHelper.DeleteFileIfExists(entity.ImageUrl, _env);
            entity.ImageUrl = await FileStorageHelper.SaveServiceImageAsync(dto.ImageFile, _env);
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.Services.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return NotFound();

        FileStorageHelper.DeleteFileIfExists(entity.ImageUrl, _env);
        _db.Services.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

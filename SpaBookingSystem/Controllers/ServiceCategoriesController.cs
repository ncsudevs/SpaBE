using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.ServiceCategories;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.DataLayer;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/service-categories")]
public class ServiceCategoriesController : ControllerBase
{
    private readonly SpaDbContext _context;

    public ServiceCategoriesController(SpaDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<ServiceCategoryDto>>> GetAll()
    {
        var items = await _context.ServiceCategories
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new ServiceCategoryDto
            {
                Id = x.Id,
                Name = x.Name,
                Description = x.Description,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServiceCategoryDto>> GetById(int id)
    {
        var item = await _context.ServiceCategories
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new ServiceCategoryDto
            {
                Id = x.Id,
                Name = x.Name,
                Description = x.Description,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .FirstOrDefaultAsync();

        if (item == null)
            return NotFound(new { message = "Category not found" });

        return Ok(item);
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPost]
    public async Task<ActionResult<ServiceCategoryDto>> Create(ServiceCategoryCreateDto input)
    {
        var normalizedName = input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            return BadRequest(new { message = "Category name is required" });

        var exists = await _context.ServiceCategories
            .AnyAsync(x => x.Name.ToLower() == normalizedName.ToLower());

        if (exists)
            return Conflict(new { message = "Category name already exists" });

        var entity = new ServiceCategory
        {
            Name = normalizedName,
            Description = input.Description?.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.ServiceCategories.Add(entity);
        await _context.SaveChangesAsync();

        var dto = new ServiceCategoryDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, dto);
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ServiceCategoryUpdateDto input)
    {
        var entity = await _context.ServiceCategories.FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return NotFound(new { message = "Category not found" });

        var normalizedName = input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            return BadRequest(new { message = "Category name is required" });

        var exists = await _context.ServiceCategories
            .AnyAsync(x => x.Id != id && x.Name.ToLower() == normalizedName.ToLower());

        if (exists)
            return Conflict(new { message = "Category name already exists" });

        entity.Name = normalizedName;
        entity.Description = input.Description?.Trim();
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = "ADMIN")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _context.ServiceCategories
            .Include(x => x.Services)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (entity == null) return NotFound(new { message = "Category not found" });
        if (entity.Services.Any())
            return BadRequest(new { message = "Cannot delete category because it has services" });

        _context.ServiceCategories.Remove(entity);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}

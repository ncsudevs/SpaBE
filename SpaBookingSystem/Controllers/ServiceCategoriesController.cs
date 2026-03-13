using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.ServiceCategories;
using SpaBookingSystem.DataLayer;
using SpaBookingSystem.ApplicationCore.Entities;

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

    // GET: /api/service-categories
    [HttpGet]
    public async Task<ActionResult<List<ServiceCategoryDto>>> GetAll()
    {
        var data = await _context.ServiceCategories
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

        return Ok(data);
    }

    // GET: /api/service-categories/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ServiceCategoryDto>> GetById(int id)
    {
        var entity = await _context.ServiceCategories.FindAsync(id);
        if (entity == null) return NotFound(new { message = "Category not found" });

        var dto = new ServiceCategoryDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };

        return Ok(dto);
    }

    // POST: /api/service-categories
    [HttpPost]
    public async Task<ActionResult<ServiceCategoryDto>> Create(ServiceCategoryCreateDto input)
    {
        //Validate
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { message = "Name is required" });

        var normalizedName = input.Name.Trim();

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

    // PUT: /api/service-categories/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, ServiceCategoryUpdateDto input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { message = "Name is required" });

        var entity = await _context.ServiceCategories.FindAsync(id);
        if (entity == null) return NotFound(new { message = "Category not found" });

        var normalizedName = input.Name.Trim();

        var exists = await _context.ServiceCategories
            .AnyAsync(x => x.Id != id && x.Name.ToLower() == normalizedName.ToLower());

        if (exists)
            return Conflict(new { message = "Category name already exists" });

        entity.Name = normalizedName;
        entity.Description = input.Description?.Trim();
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent(); // 204
    }

    // DELETE: /api/service-categories/{id}
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

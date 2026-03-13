using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;
using SpaBookingSystem.ApplicationCore.Constants;

namespace SpaBookingSystem.Api.Dtos.Services;

public class ServiceUpdateDto
{
    [Required]
    [MaxLength(DataLengths.NAME)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(DataLengths.DESCRIPTION)]
    public string? Description { get; set; }

    [Range(0, 999999999)]
    public decimal Price { get; set; }

    [Range(1, 10000)]
    public int Duration { get; set; }

    [MaxLength(DataLengths.STATUS)]
    public string? Status { get; set; }

    public int CategoryId { get; set; }

    // Sending a new file replaces the current image; omitting it keeps the existing file.
    public IFormFile? ImageFile { get; set; }
}

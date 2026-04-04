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

    [Range(1, 100)]
    public int SlotCapacity { get; set; } = 5;

    [MaxLength(DataLengths.STATUS)]
    public string? Status { get; set; }

    public int CategoryId { get; set; }
    public IFormFile? ImageFile { get; set; }
}

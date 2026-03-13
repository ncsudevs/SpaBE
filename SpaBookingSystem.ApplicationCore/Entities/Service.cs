namespace SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.ApplicationCore.Constants;
using System.ComponentModel.DataAnnotations;

public class Service
{
    public int Id { get; set; }

    [MaxLength(DataLengths.NAME)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(DataLengths.DESCRIPTION)]
    public string? Description { get; set; }

    // Price is stored as decimal to preserve currency precision.
    public decimal Price { get; set; }

    // Duration is stored in minutes for simpler display and summary calculation.
    public int Duration { get; set; }

    [MaxLength(DataLengths.STATUS)]
    public string Status { get; set; } = "ACTIVE";

    // ImageUrl stores the relative public path returned after saving the uploaded file into wwwroot.
    [MaxLength(DataLengths.IMAGE_URL)]
    public string? ImageUrl { get; set; }

    public int CategoryId { get; set; }
    public ServiceCategory? Category { get; set; }
}

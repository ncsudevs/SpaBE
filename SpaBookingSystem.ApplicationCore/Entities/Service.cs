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

    public decimal Price { get; set; }
    public int Duration { get; set; }

    [MaxLength(DataLengths.STATUS)]
    public string Status { get; set; } = "ACTIVE";

    [MaxLength(DataLengths.IMAGE_URL)]
    public string? ImageUrl { get; set; }

    // Every service time slot uses the same default capacity, e.g. 09:00=5, 10:30=5.
    public int SlotCapacity { get; set; } = 5;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public int CategoryId { get; set; }
    public ServiceCategory? Category { get; set; }
}

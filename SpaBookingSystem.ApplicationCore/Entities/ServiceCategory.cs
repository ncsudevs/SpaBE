namespace SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.ApplicationCore.Constants;
using System.ComponentModel.DataAnnotations;

public class ServiceCategory
{
    public int Id { get; set; }

    [MaxLength(DataLengths.NAME)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(DataLengths.DESCRIPTION)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation collection is used by EF Core for category-to-service relationship mapping.
    public List<Service> Services { get; set; } = new();
}

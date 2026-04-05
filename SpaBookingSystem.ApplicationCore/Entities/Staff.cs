namespace SpaBookingSystem.ApplicationCore.Entities;
using System.ComponentModel.DataAnnotations;
using SpaBookingSystem.ApplicationCore.Constants;

public class Staff
{
    public int Id { get; set; }

    [MaxLength(DataLengths.NAME)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(DataLengths.EMAIL)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(DataLengths.SHORT_DESCRIPTION)]
    public string? Skills { get; set; }

    public bool IsActive { get; set; } = true;

    public int MaxConcurrent { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<BookingDetail> BookingDetails { get; set; } = new();
}

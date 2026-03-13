namespace SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.ApplicationCore.Constants;
using System.ComponentModel.DataAnnotations;

public class Customer
{
    public int Id { get; set; }

    [MaxLength(DataLengths.NAME)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(DataLengths.EMAIL)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(DataLengths.PASSWORD_HASH)]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(DataLengths.ROLE_NAME)]
    public string Role { get; set; } = "CUSTOMER";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // A customer may create multiple bookings during the lifetime of the account.
    //public List<Booking> Bookings { get; set; } = new();
}

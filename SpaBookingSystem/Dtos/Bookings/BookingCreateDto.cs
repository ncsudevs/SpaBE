using System.ComponentModel.DataAnnotations;

namespace SpaBookingSystem.Api.Dtos.Bookings;

public class BookingCreateDto
{
    [Required]
    public string FullName { get; set; } = string.Empty;

    [Required]
    public string Phone { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public DateOnly AppointmentDate { get; set; }

    [Required]
    public string AppointmentTime { get; set; } = string.Empty;

    public string? Note { get; set; }

    [Required]
    [MinLength(1)]
    public List<BookingItemCreateDto> Items { get; set; } = new();
}

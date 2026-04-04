using SpaBookingSystem.Api.Dtos.Bookings;
using System.ComponentModel.DataAnnotations;

public class BookingCreateDto
{
    [Required]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Phone { get; set; } = string.Empty;

    [Required]
    [MaxLength(5)]
    public string Region { get; set; } = "VN";

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string? Note { get; set; }

    public bool IsGroupBooking { get; set; }

    [Range(1, 100)]
    public int GroupSize { get; set; } = 1;

    [Required]
    [MinLength(1)]
    public List<BookingItemCreateDto> Items { get; set; } = new();
}
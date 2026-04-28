using SpaBookingSystem.Api.Dtos.Bookings;
using SpaBookingSystem.Api.Dtos.Payments;

namespace SpaBookingSystem.Api.Dtos.Customers;

public class CustomerDetailDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool CanDelete { get; set; }
    public string? DeleteBlockedReason { get; set; }

    public List<BookingDto> Bookings { get; set; } = new();
    public List<PaymentDto> Payments { get; set; } = new();
}

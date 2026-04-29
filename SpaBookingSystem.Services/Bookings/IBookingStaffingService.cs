using SpaBookingSystem.ApplicationCore.Entities;

namespace SpaBookingSystem.Services.Bookings;

public interface IBookingStaffingService
{
    int GetAssignedQuantity(BookingDetail detail);
    int GetUnassignedQuantity(BookingDetail detail);
    bool IsFullyStaffed(BookingDetail detail);
    bool IsFullyStaffed(Booking booking);
    string? BuildDetailStaffingWarning(BookingDetail detail);
    string? BuildBookingStaffingWarning(Booking booking);
    Task<int> GetRemainingCapacityAsync(
        Staff staff,
        DateOnly date,
        string time,
        int durationMinutes,
        int? ignoreAssignmentId = null,
        CancellationToken cancellationToken = default);
    Task<BookingStaffingResult> AutoAssignAsync(Booking booking, CancellationToken cancellationToken = default);
}

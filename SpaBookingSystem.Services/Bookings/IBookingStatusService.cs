using SpaBookingSystem.ApplicationCore.Entities;

namespace SpaBookingSystem.Services.Bookings;

public interface IBookingStatusService
{
    string? ValidateAdminStatusChange(Booking booking, string nextStatus, bool isFullyStaffed);
    string? ValidateCheckInChange(Booking booking, bool isCheckedIn, bool isFullyStaffed);
    void ApplyAdminStatusChange(Booking booking, string nextStatus);
    void SetCheckIn(Booking booking, bool isCheckedIn, bool isFullyStaffed);
    void ResetCheckIn(Booking booking);
    bool IsEffectiveCheckedIn(Booking booking);
    string GetWorkflowStatus(Booking booking);
    string GetWorkflowStatusLabel(Booking booking);
}

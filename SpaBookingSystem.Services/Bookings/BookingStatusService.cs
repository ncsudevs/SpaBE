using SpaBookingSystem.ApplicationCore.Constants;
using SpaBookingSystem.ApplicationCore.Entities;

namespace SpaBookingSystem.Services.Bookings;

public class BookingStatusService : IBookingStatusService
{
    public string? ValidateAdminStatusChange(Booking booking, string nextStatus, bool isFullyStaffed)
    {
        if (booking.Status == BookingStatusNames.Cancelled && nextStatus != BookingStatusNames.Cancelled)
            return "Cancelled bookings are final and cannot be reopened.";

        return nextStatus switch
        {
            BookingStatusNames.Pending => booking.PaymentStatus == PaymentStatusNames.Paid
                ? "Paid bookings cannot be moved back to PENDING."
                : null,

            BookingStatusNames.Confirmed => booking.PaymentStatus != PaymentStatusNames.Paid
                ? "Only paid bookings can be confirmed."
                : booking.Status == BookingStatusNames.Completed
                    ? "Completed bookings cannot be moved back to CONFIRMED."
                    : null,

            BookingStatusNames.Completed => booking.PaymentStatus != PaymentStatusNames.Paid
                ? "Check-in can only be completed after payment is confirmed."
                : booking.Status != BookingStatusNames.Confirmed && booking.Status != BookingStatusNames.Completed
                    ? "Booking must be confirmed before it can be completed."
                    : !booking.IsCheckedIn
                        ? "Booking must be checked in before it can be completed."
                        : !isFullyStaffed
                            ? "Assign enough staff quantity to every booking item before completing check-in."
                            : null,

            BookingStatusNames.Cancelled => booking.Status == BookingStatusNames.Completed
                ? "Completed bookings cannot be cancelled."
                : booking.IsCheckedIn
                    ? "Undo check-in before cancelling this booking."
                    : booking.PaymentStatus == PaymentStatusNames.Paid
                        ? "Paid bookings must be refunded from the payments screen so a refund reason can be recorded."
                        : null,

            _ => null
        };
    }

    public string? ValidateCheckInChange(Booking booking, bool isCheckedIn)
    {
        if (booking.PaymentStatus != PaymentStatusNames.Paid)
            return "Only paid bookings can be checked in.";

        if (booking.Status == BookingStatusNames.Cancelled)
            return "Cancelled bookings cannot be checked in.";

        if (isCheckedIn)
        {
            return booking.Status != BookingStatusNames.Confirmed
                ? "Only confirmed bookings can be checked in."
                : null;
        }

        return booking.Status == BookingStatusNames.Completed
            ? "Completed bookings cannot be unchecked."
            : null;
    }

    public void ApplyAdminStatusChange(Booking booking, string nextStatus)
    {
        if (nextStatus == BookingStatusNames.Pending || nextStatus == BookingStatusNames.Cancelled)
        {
            ResetCheckIn(booking);
        }

        booking.Status = nextStatus;
        booking.UpdatedAt = DateTime.UtcNow;
    }

    public void SetCheckIn(Booking booking, bool isCheckedIn)
    {
        booking.IsCheckedIn = isCheckedIn;
        booking.CheckedInAt = isCheckedIn ? DateTime.UtcNow : null;
        booking.UpdatedAt = DateTime.UtcNow;
    }

    public void ResetCheckIn(Booking booking)
    {
        booking.IsCheckedIn = false;
        booking.CheckedInAt = null;
    }

    public bool IsEffectiveCheckedIn(Booking booking) =>
        booking.IsCheckedIn
        && booking.Status == BookingStatusNames.Confirmed
        && booking.PaymentStatus == PaymentStatusNames.Paid;

    public string GetWorkflowStatus(Booking booking)
    {
        if (booking.Status == BookingStatusNames.Cancelled)
            return BookingStatusNames.Cancelled;

        if (booking.Status == BookingStatusNames.Completed)
            return BookingStatusNames.Completed;

        if (IsEffectiveCheckedIn(booking))
            return "CHECKED_IN";

        return booking.Status;
    }

    public string GetWorkflowStatusLabel(Booking booking) =>
        GetWorkflowStatus(booking) switch
        {
            "CHECKED_IN" => "Checked in",
            BookingStatusNames.Pending => "Waiting processing",
            BookingStatusNames.Confirmed => "Paid and scheduled",
            BookingStatusNames.Completed => "Service completed",
            BookingStatusNames.Cancelled => "Cancelled",
            var other => other
        };
}

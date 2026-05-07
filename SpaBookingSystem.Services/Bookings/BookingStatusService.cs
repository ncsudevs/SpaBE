using SpaBookingSystem.ApplicationCore.Constants;
using SpaBookingSystem.ApplicationCore.Entities;

namespace SpaBookingSystem.Services.Bookings;

public class BookingStatusService : IBookingStatusService
{
    public string? ValidateAdminStatusChange(Booking booking, string nextStatus, bool isFullyStaffed)
    {
        // A cancelled booking is treated as final state because downstream
        // payment/refund history should stay auditable once the flow is closed.
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

    public string? ValidateCheckInChange(Booking booking, bool isCheckedIn, bool isFullyStaffed)
    {
       
        if (booking.PaymentStatus != PaymentStatusNames.Paid)
            return "Only paid bookings can be checked in.";

        if (booking.Status == BookingStatusNames.Cancelled)
            return "Cancelled bookings cannot be checked in.";

        if (isCheckedIn)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (booking.AppointmentDate > today)
            {
                return "Check-in is only allowed on the appointment date.";
            }

            return booking.Status != BookingStatusNames.Confirmed
                ? "Only confirmed bookings can be checked in."
                : !isFullyStaffed
                    ? "Finish staffing every booking item before checking in the customer."
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
        else if (nextStatus == BookingStatusNames.Completed)
        {
            // Completed is only meaningful after a real check-in moment, so we
            // keep the timestamp even when completion is triggered by admin.
            booking.IsCheckedIn = true;
            booking.CheckedInAt ??= DateTime.UtcNow;
        }

        booking.Status = nextStatus;
        booking.UpdatedAt = DateTime.UtcNow;
    }

    public void SetCheckIn(Booking booking, bool isCheckedIn, bool isFullyStaffed)
    {
        booking.IsCheckedIn = isCheckedIn;
        booking.CheckedInAt = isCheckedIn ? DateTime.UtcNow : null;

        // The UI treats check-in as the final action for a ready booking, so
        // the backend promotes CONFIRMED -> COMPLETED in the same request.
        if (isCheckedIn && booking.Status == BookingStatusNames.Confirmed && isFullyStaffed)
        {
            booking.Status = BookingStatusNames.Completed;
        }

        booking.UpdatedAt = DateTime.UtcNow;
    }

    public void ResetCheckIn(Booking booking)
    {
        booking.IsCheckedIn = false;
        booking.CheckedInAt = null;
    }

    public bool IsEffectiveCheckedIn(Booking booking) =>
        // Completed bookings should still read as checked-in from the
        // customer's perspective even though the stored status moved on.
        booking.PaymentStatus == PaymentStatusNames.Paid
        && (booking.Status == BookingStatusNames.Completed
            || (booking.IsCheckedIn && booking.Status == BookingStatusNames.Confirmed));

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

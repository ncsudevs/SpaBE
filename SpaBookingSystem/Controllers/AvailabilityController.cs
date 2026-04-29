using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.Bookings;
using SpaBookingSystem.ApplicationCore.Constants;
using SpaBookingSystem.DataLayer;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/availability")]
public class AvailabilityController : ControllerBase
{
    private readonly SpaDbContext _db;

    public AvailabilityController(SpaDbContext db)
    {
        _db = db;
    }

    [Authorize(Roles = RoleNames.Customer)]
    [HttpGet]
    public async Task<ActionResult<AvailabilityDto>> GetAvailability(
        [FromQuery] int serviceId,
        [FromQuery] DateOnly appointmentDate,
        [FromQuery] string appointmentTime)
    {
        var normalizedTime = NormalizeTime(appointmentTime);
        if (string.IsNullOrWhiteSpace(normalizedTime))
            return BadRequest(new { message = "Appointment time is required" });

        var service = await _db.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == serviceId && x.Status == "ACTIVE");

        if (service == null)
            return NotFound(new { message = "Service not found" });

        var bookedQuantity = await GetBookedQuantityAsync(
            serviceId,
            appointmentDate,
            normalizedTime,
            service.Duration);

        return Ok(new AvailabilityDto
        {
            ServiceId = serviceId,
            AppointmentDate = appointmentDate,
            AppointmentTime = normalizedTime,
            SlotCapacity = service.SlotCapacity,
            BookedQuantity = bookedQuantity,
            RemainingSlots = Math.Max(0, service.SlotCapacity - bookedQuantity),
        });
    }

    private async Task<int> GetBookedQuantityAsync(
        int serviceId,
        DateOnly appointmentDate,
        string appointmentTime,
        int targetDurationMinutes)
    {
        var targetStart = ParseTimeToMinutes(appointmentTime);
        if (targetStart == null)
            return 0;

        var details = await _db.BookingDetails
            .AsNoTracking()
            .Include(x => x.Booking)
            .Include(x => x.Service)
            .Where(x => x.ServiceId == serviceId
                && x.AppointmentDate == appointmentDate
                && x.Booking != null
                && x.Booking.Status != BookingStatusNames.Cancelled)
            .Select(x => new
            {
                x.Quantity,
                x.AppointmentTime,
                Duration = x.Service != null ? x.Service.Duration : (int?)null,
            })
            .ToListAsync();

        var targetEnd = targetStart.Value + Math.Max(1, targetDurationMinutes);
        var total = 0;

        foreach (var detail in details)
        {
            var existingStart = ParseTimeToMinutes(detail.AppointmentTime);
            if (existingStart == null)
                continue;

            var existingDuration = detail.Duration ?? targetDurationMinutes;
            var existingEnd = existingStart.Value + Math.Max(1, existingDuration);
            var overlap = existingStart.Value < targetEnd && targetStart.Value < existingEnd;

            if (overlap)
            {
                total += detail.Quantity;
            }
        }

        return total;
    }

    private static string NormalizeTime(string value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();

    private static int? ParseTimeToMinutes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTime.TryParse(value, out var dt))
            return dt.Hour * 60 + dt.Minute;

        var parts = value.Split(':');
        if (parts.Length >= 2
            && int.TryParse(parts[0], out var hour)
            && int.TryParse(parts[1].Substring(0, 2), out var minute))
        {
            return (hour % 24) * 60 + Math.Clamp(minute, 0, 59);
        }

        return null;
    }
}

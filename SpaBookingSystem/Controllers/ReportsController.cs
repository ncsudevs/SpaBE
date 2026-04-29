using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.Reports;
using SpaBookingSystem.ApplicationCore.Constants;
using SpaBookingSystem.DataLayer;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly SpaDbContext _db;

    public ReportsController(SpaDbContext db)
    {
        _db = db;
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Cashier}")]
    [HttpGet("summary")]
    public async Task<ActionResult<ReportSummaryDto>> GetSummary(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate)
    {
        var to = toDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var from = fromDate ?? to.AddDays(-29);

        if (from > to)
            return BadRequest(new { message = "fromDate must be earlier than or equal to toDate." });

        var bookingsQuery = _db.Bookings
            .AsNoTracking()
            .Where(x => x.AppointmentDate >= from && x.AppointmentDate <= to);

        var paymentsQuery = _db.Payments
            .AsNoTracking()
            .Where(x => x.Booking != null
                && x.Booking.AppointmentDate >= from
                && x.Booking.AppointmentDate <= to);

        var topServices = await _db.BookingDetails
            .AsNoTracking()
            .Include(x => x.Service)
            .Include(x => x.Booking)
            .Where(x => x.Booking != null
                && x.Booking.AppointmentDate >= from
                && x.Booking.AppointmentDate <= to
                && x.Booking.Status != BookingStatusNames.Cancelled)
            .GroupBy(x => new { x.ServiceId, ServiceName = x.Service != null ? x.Service.Name : "Unknown service" })
            .Select(g => new TopServiceReportDto
            {
                ServiceId = g.Key.ServiceId,
                ServiceName = g.Key.ServiceName,
                TotalQuantity = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.LineTotal),
            })
            .OrderByDescending(x => x.TotalQuantity)
            .ThenByDescending(x => x.Revenue)
            .Take(5)
            .ToListAsync();

        var dto = new ReportSummaryDto
        {
            FromDate = from,
            ToDate = to,
            TotalBookings = await bookingsQuery.CountAsync(),
            CompletedBookings = await bookingsQuery.CountAsync(x => x.Status == BookingStatusNames.Completed),
            CancelledBookings = await bookingsQuery.CountAsync(x => x.Status == BookingStatusNames.Cancelled),
            PaidPayments = await paymentsQuery.CountAsync(x => x.Status == PaymentStatusNames.Paid),
            RefundedPayments = await paymentsQuery.CountAsync(x => x.Status == PaymentStatusNames.Refunded),
            Revenue = await paymentsQuery
                .Where(x => x.Status == PaymentStatusNames.Paid)
                .SumAsync(x => (decimal?)x.Amount) ?? 0m,
            RefundedAmount = await paymentsQuery
                .Where(x => x.Status == PaymentStatusNames.Refunded)
                .SumAsync(x => (decimal?)x.Amount) ?? 0m,
            TopServices = topServices,
        };

        return Ok(dto);
    }
}

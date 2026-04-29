using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.Dashboard;
using SpaBookingSystem.ApplicationCore.Constants;
using SpaBookingSystem.DataLayer;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly SpaDbContext _db;

    public DashboardController(SpaDbContext db)
    {
        _db = db;
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Cashier}")]
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary()
    {
        var recentBookings = await _db.Bookings
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(5)
            .Select(x => new DashboardRecentBookingDto
            {
                BookingId = x.Id,
                BookingCode = x.BookingCode,
                CustomerName = x.FullName,
                AppointmentDate = x.AppointmentDate,
                AppointmentTime = x.AppointmentTime,
                Status = x.Status,
                PaymentStatus = x.PaymentStatus,
                TotalAmount = x.TotalAmount,
            })
            .ToListAsync();

        var recentPayments = await _db.Payments
            .AsNoTracking()
            .Include(x => x.Booking)
            .OrderByDescending(x => x.PaidAt)
            .Take(5)
            .Select(x => new DashboardRecentPaymentDto
            {
                PaymentId = x.Id,
                PaymentCode = x.PaymentCode,
                BookingCode = x.Booking != null ? x.Booking.BookingCode : string.Empty,
                Method = x.Method,
                Status = x.Status,
                Amount = x.Amount,
                PaidAt = x.PaidAt,
            })
            .ToListAsync();

        var dto = new DashboardSummaryDto
        {
            TotalServices = await _db.Services.CountAsync(),
            ActiveServices = await _db.Services.CountAsync(x => x.Status == "ACTIVE"),
            TotalCustomers = await _db.Customers.CountAsync(),
            ActiveCustomers = await _db.Customers.CountAsync(x => x.IsActive),
            TotalStaff = await _db.Staffs.CountAsync(),
            ActiveStaff = await _db.Staffs.CountAsync(x => x.IsActive),
            PendingBookings = await _db.Bookings.CountAsync(x => x.Status == BookingStatusNames.Pending),
            ConfirmedBookings = await _db.Bookings.CountAsync(x => x.Status == BookingStatusNames.Confirmed),
            CompletedBookings = await _db.Bookings.CountAsync(x => x.Status == BookingStatusNames.Completed),
            CancelledBookings = await _db.Bookings.CountAsync(x => x.Status == BookingStatusNames.Cancelled),
            PendingPayments = await _db.Payments.CountAsync(x => x.Status == PaymentStatusNames.Pending),
            AwaitingTransferPayments = await _db.Payments.CountAsync(x => x.Status == PaymentStatusNames.AwaitingTransfer),
            PaidPayments = await _db.Payments.CountAsync(x => x.Status == PaymentStatusNames.Paid),
            PaidRevenue = await _db.Payments
                .Where(x => x.Status == PaymentStatusNames.Paid)
                .SumAsync(x => (decimal?)x.Amount) ?? 0m,
            RecentBookings = recentBookings,
            RecentPayments = recentPayments,
        };

        return Ok(dto);
    }
}

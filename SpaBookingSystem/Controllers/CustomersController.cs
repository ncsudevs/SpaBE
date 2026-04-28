using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.Bookings;
using SpaBookingSystem.Api.Dtos.Customers;
using SpaBookingSystem.Api.Dtos.Payments;
using SpaBookingSystem.ApplicationCore.Constants;
using SpaBookingSystem.DataLayer;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.Api.Helpers;
using SpaBookingSystem.Api.Services;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly SpaDbContext _db;
    private readonly IBookingStaffingService _bookingStaffingService;

    public CustomersController(SpaDbContext db, IBookingStaffingService bookingStaffingService)
    {
        _db = db;
        _bookingStaffingService = bookingStaffingService;
    }

    [Authorize(Roles = "ADMIN")]
    [HttpGet]
    public async Task<ActionResult<List<CustomerDto>>> GetAll()
    {
        var customers = await _db.Customers
            .AsNoTracking()
            .OrderBy(c => c.FullName)
            .ToListAsync();

        var normalizedEmails = customers
            .Select(c => c.Email.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var bookings = await _db.Bookings
            .AsNoTracking()
            .Where(b => normalizedEmails.Contains(b.Email.ToLower()))
            .ToListAsync();

        var bookingsByEmail = bookings
            .GroupBy(b => b.Email.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList());

        var data = customers.Select(c =>
        {
            var emailKey = c.Email.Trim().ToLowerInvariant();
            bookingsByEmail.TryGetValue(emailKey, out var customerBookings);
            customerBookings ??= new List<Booking>();
            var deleteBlockedReason = GetCustomerDeleteBlockedReason(customerBookings);

            return new CustomerDto
            {
                Id = c.Id,
                FullName = c.FullName,
                Email = c.Email,
                Phone = c.Phone,
                IsActive = c.IsActive,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                BookingCount = customerBookings.Count,
                CanDelete = deleteBlockedReason == null,
                DeleteBlockedReason = deleteBlockedReason
            };
        }).ToList();

        return Ok(data);
    }

    [Authorize(Roles = "ADMIN")]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerDetailDto>> GetById(int id)
    {
        var customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (customer == null) return NotFound(new { message = "Customer not found" });

        var bookings = await _db.Bookings
            .AsNoTracking()
            .Include(b => b.BookingDetails)
                .ThenInclude(d => d.Service)
                    .ThenInclude(s => s.Category)
            .Include(b => b.BookingDetails)
                .ThenInclude(d => d.StaffAssignments)
                    .ThenInclude(a => a.Staff)
            .Include(b => b.Payments)
            .Where(b => b.Email.ToLower() == customer.Email.ToLower())
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        var payments = await _db.Payments
            .AsNoTracking()
            .Include(p => p.Booking)
            .Where(p => p.Booking != null && p.Booking.Email.ToLower() == customer.Email.ToLower())
            .OrderByDescending(p => p.PaidAt)
            .ToListAsync();

        var dto = new CustomerDetailDto
        {
            Id = customer.Id,
            FullName = customer.FullName,
            Email = customer.Email,
            Phone = customer.Phone,
            IsActive = customer.IsActive,
            CreatedAt = customer.CreatedAt,
            UpdatedAt = customer.UpdatedAt,
            CanDelete = GetCustomerDeleteBlockedReason(bookings) == null,
            DeleteBlockedReason = GetCustomerDeleteBlockedReason(bookings),
            Bookings = bookings.Select(MapBooking).ToList(),
            Payments = payments.Select(MapPayment).ToList()
        };

        return Ok(dto);
    }

    [Authorize(Roles = "ADMIN")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id);
        if (customer == null) return NotFound(new { message = "Customer not found" });

        var bookings = await _db.Bookings
            .AsNoTracking()
            .Where(b => b.Email.ToLower() == customer.Email.ToLower())
            .ToListAsync();

        var deleteBlockedReason = GetCustomerDeleteBlockedReason(bookings);
        if (deleteBlockedReason != null)
            return Conflict(new { message = deleteBlockedReason });

        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CustomerUpdateDto dto)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id);
        if (customer == null) return NotFound(new { message = "Customer not found" });

        if (string.IsNullOrWhiteSpace(dto.FullName))
            return BadRequest(new { message = "Full name is required." });

        if (!string.IsNullOrWhiteSpace(dto.Phone))
        {
            if (!PhoneHelper.TryNormalizePhone(dto.Phone, dto.Region, out var normalized, out var phoneError))
                return BadRequest(new { message = phoneError });

            var existsPhone = await _db.Customers.AnyAsync(x => x.Phone == normalized && x.Id != id);
            if (existsPhone)
                return Conflict(new { message = "Phone is already used by another customer." });

            customer.Phone = normalized;
        }
        else
        {
            customer.Phone = null;
        }

        customer.FullName = dto.FullName.Trim();
        customer.IsActive = dto.IsActive;
        customer.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return NoContent();
    }
    private BookingDto MapBooking(Booking booking)
    {
        var effectiveCheckedIn = IsEffectiveCheckedIn(booking);
        var firstSlot = booking.BookingDetails.OrderBy(x => x.AppointmentDate).ThenBy(x => x.AppointmentTime).FirstOrDefault();
        var latestPayment = booking.Payments?
            .OrderByDescending(p => p.PaidAt)
            .ThenByDescending(p => p.Id)
            .FirstOrDefault();

        return new BookingDto
        {
            Id = booking.Id,
            BookingCode = booking.BookingCode,
            FullName = booking.FullName,
            Phone = booking.Phone,
            Email = booking.Email,
            AppointmentDate = firstSlot?.AppointmentDate ?? booking.AppointmentDate,
            AppointmentTime = firstSlot?.AppointmentTime ?? booking.AppointmentTime,
            Note = booking.Note,
            TotalAmount = booking.TotalAmount,
            Status = booking.Status,
            WorkflowStatus = GetWorkflowStatus(booking),
            WorkflowStatusLabel = GetWorkflowStatusLabel(booking),
            PaymentStatus = booking.PaymentStatus,
            IsGroupBooking = booking.IsGroupBooking,
            GroupSize = booking.GroupSize,
            CreatedAt = booking.CreatedAt,
            UpdatedAt = booking.UpdatedAt,
            PaymentAttempts = booking.PaymentAttempts,
            LastPaymentCreatedAt = latestPayment?.PaidAt,
            LatestPaymentId = latestPayment?.Id,
            LatestPaymentMethod = latestPayment?.Method,
            IsCheckedIn = effectiveCheckedIn,
            CheckedInAt = effectiveCheckedIn ? booking.CheckedInAt : null,
            IsFullyStaffed = _bookingStaffingService.IsFullyStaffed(booking),
            StaffingWarning = _bookingStaffingService.BuildBookingStaffingWarning(booking),
            Items = booking.BookingDetails.OrderBy(x => x.AppointmentDate).ThenBy(x => x.AppointmentTime).Select(d => new BookingItemDto
            {
                DetailId = d.Id,
                ServiceId = d.ServiceId,
                ServiceName = d.Service != null ? d.Service.Name : string.Empty,
                CategoryId = d.Service?.CategoryId ?? 0,
                CategoryName = d.Service?.Category?.Name ?? string.Empty,
                Quantity = d.Quantity,
                AppointmentDate = d.AppointmentDate,
                AppointmentTime = d.AppointmentTime,
                AssignedQuantity = _bookingStaffingService.GetAssignedQuantity(d),
                UnassignedQuantity = _bookingStaffingService.GetUnassignedQuantity(d),
                IsFullyStaffed = _bookingStaffingService.IsFullyStaffed(d),
                StaffingWarning = _bookingStaffingService.BuildDetailStaffingWarning(d),
                UnitPrice = d.UnitPrice,
                LineTotal = d.LineTotal,
                StaffAssignments = d.StaffAssignments
                    .OrderBy(x => x.Staff?.FullName)
                    .ThenBy(x => x.StaffId)
                    .Select(x => new BookingItemStaffAssignmentDto
                    {
                        Id = x.Id,
                        StaffId = x.StaffId,
                        StaffName = x.Staff?.FullName ?? string.Empty,
                        AssignedQuantity = x.AssignedQuantity,
                        StaffMaxConcurrent = x.Staff?.MaxConcurrent ?? 0
                    })
                    .ToList()
            }).ToList()
        };
    }

    private static PaymentDto MapPayment(Payment payment)
    {
        var paymentContent = $"{payment.PaymentCode} {payment.Booking?.BookingCode ?? string.Empty}".Trim();
        return new PaymentDto
        {
            Id = payment.Id,
            PaymentCode = payment.PaymentCode,
            BookingId = payment.BookingId,
            BookingCode = payment.Booking?.BookingCode ?? string.Empty,
            Method = payment.Method,
            Amount = payment.Amount,
            Status = payment.Status,
            PaidAt = payment.PaidAt,
            TransactionCode = payment.TransactionCode,
            ProviderName = string.Empty,
            AccountNumber = string.Empty,
            AccountName = string.Empty,
            PaymentContent = paymentContent,
            QrNote = string.Empty,
            PayUrl = null,
            DeepLink = null,
            QrCodeUrl = null,
            IsSandbox = false
        };
    }

    private static string? GetCustomerDeleteBlockedReason(IEnumerable<Booking> bookings)
    {
        var bookingList = bookings.ToList();
        if (bookingList.Count == 0)
            return null;

        if (bookingList.Any(b => IsEffectiveCheckedIn(b) || b.Status == BookingStatusNames.Completed))
            return "Cannot delete this customer because at least one booking is already checked in or completed.";

        if (bookingList.Any(b => b.PaymentStatus is PaymentStatusNames.AwaitingTransfer or PaymentStatusNames.Pending or PaymentStatusNames.Paid))
            return "Cannot delete this customer while a booking still has an active or paid payment.";

        if (bookingList.Any(b => b.Status is BookingStatusNames.Pending or BookingStatusNames.Confirmed))
            return "Cannot delete this customer while a booking is still waiting or scheduled.";

        return null;
    }

    private static string GetWorkflowStatus(Booking booking)
    {
        if (booking.Status == BookingStatusNames.Cancelled)
            return BookingStatusNames.Cancelled;

        if (booking.Status == BookingStatusNames.Completed)
            return BookingStatusNames.Completed;

        if (IsEffectiveCheckedIn(booking))
            return "CHECKED_IN";

        return booking.Status;
    }

    private static bool IsEffectiveCheckedIn(Booking booking) =>
        booking.IsCheckedIn
        && (booking.Status == BookingStatusNames.Confirmed || booking.Status == BookingStatusNames.Completed)
        && booking.PaymentStatus == PaymentStatusNames.Paid;

    private static string GetWorkflowStatusLabel(Booking booking) =>
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

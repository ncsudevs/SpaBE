using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.Bookings;
using SpaBookingSystem.Api.Dtos.Customers;
using SpaBookingSystem.Api.Dtos.Payments;
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
        var data = await _db.Customers
            .AsNoTracking()
            .Select(c => new CustomerDto
            {
                Id = c.Id,
                FullName = c.FullName,
                Email = c.Email,
                Phone = c.Phone,
                IsActive = c.IsActive,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                BookingCount = _db.Bookings.Count(b => b.Email.ToLower() == c.Email.ToLower())
            })
            .OrderBy(c => c.FullName)
            .ToListAsync();

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

        var hasBookings = await _db.Bookings.AnyAsync(b => b.Email.ToLower() == customer.Email.ToLower());
        if (hasBookings)
            return Conflict(new { message = "Cannot delete: customer has bookings." });

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
            PaymentStatus = booking.PaymentStatus,
            IsGroupBooking = booking.IsGroupBooking,
            GroupSize = booking.GroupSize,
            CreatedAt = booking.CreatedAt,
            UpdatedAt = booking.UpdatedAt,
            PaymentAttempts = booking.PaymentAttempts,
            LastPaymentCreatedAt = latestPayment?.PaidAt,
            LatestPaymentId = latestPayment?.Id,
            LatestPaymentMethod = latestPayment?.Method,
            IsCheckedIn = booking.IsCheckedIn,
            CheckedInAt = booking.CheckedInAt,
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
}

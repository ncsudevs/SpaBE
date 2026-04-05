using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.Bookings;
using SpaBookingSystem.Api.Dtos.Customers;
using SpaBookingSystem.Api.Dtos.Payments;
using SpaBookingSystem.DataLayer;
using SpaBookingSystem.ApplicationCore.Entities;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly SpaDbContext _db;

    public CustomersController(SpaDbContext db)
    {
        _db = db;
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
            .Include(b => b.BookingDetails)
                .ThenInclude(d => d.Staff)
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

    private static BookingDto MapBooking(Booking booking)
    {
        var firstSlot = booking.BookingDetails.OrderBy(x => x.AppointmentDate).ThenBy(x => x.AppointmentTime).FirstOrDefault();

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
            LastPaymentCreatedAt = booking.Payments?
                .OrderByDescending(p => p.PaidAt)
                .Select(p => (DateTime?)p.PaidAt)
                .FirstOrDefault(),
            Items = booking.BookingDetails.OrderBy(x => x.AppointmentDate).ThenBy(x => x.AppointmentTime).Select(d => new BookingItemDto
            {
                ServiceId = d.ServiceId,
                ServiceName = d.Service != null ? d.Service.Name : string.Empty,
                Quantity = d.Quantity,
                AppointmentDate = d.AppointmentDate,
                AppointmentTime = d.AppointmentTime,
                StaffId = d.StaffId,
                StaffName = d.Staff?.FullName,
                UnitPrice = d.UnitPrice,
                LineTotal = d.LineTotal
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

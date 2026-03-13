using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.Payments;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.DataLayer;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly SpaDbContext _db;

    public PaymentsController(SpaDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<PaymentDto>>> GetAll()
    {
        var data = await _db.Payments
            .AsNoTracking()
            .Include(x => x.Booking)
            .OrderByDescending(x => x.PaidAt)
            .Select(x => new PaymentDto
            {
                Id = x.Id,
                PaymentCode = x.PaymentCode,
                BookingId = x.BookingId,
                BookingCode = x.Booking != null ? x.Booking.BookingCode : string.Empty,
                Method = x.Method,
                Amount = x.Amount,
                Status = x.Status,
                PaidAt = x.PaidAt,
                TransactionCode = x.TransactionCode
            })
            .ToListAsync();

        return Ok(data);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PaymentDto>> GetById(int id)
    {
        var entity = await _db.Payments
            .AsNoTracking()
            .Include(x => x.Booking)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (entity == null) return NotFound(new { message = "Payment not found" });

        return Ok(new PaymentDto
        {
            Id = entity.Id,
            PaymentCode = entity.PaymentCode,
            BookingId = entity.BookingId,
            BookingCode = entity.Booking?.BookingCode ?? string.Empty,
            Method = entity.Method,
            Amount = entity.Amount,
            Status = entity.Status,
            PaidAt = entity.PaidAt,
            TransactionCode = entity.TransactionCode
        });
    }

    [HttpPost]
    public async Task<ActionResult<PaymentDto>> Create(PaymentCreateDto dto)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(x => x.Id == dto.BookingId);
        if (booking == null) return NotFound(new { message = "Booking not found" });

        if (booking.PaymentStatus == "PAID")
            return BadRequest(new { message = "This booking has already been paid" });

        // For the current project scope, payment creation also acts as payment confirmation.
        var payment = new Payment
        {
            BookingId = booking.Id,
            PaymentCode = $"PAY-{DateTime.UtcNow:yyyyMMddHHmmss}",
            Method = dto.Method.Trim(),
            Amount = booking.TotalAmount,
            Status = "PAID",
            PaidAt = DateTime.UtcNow,
            TransactionCode = $"TXN-{Guid.NewGuid().ToString("N")[..10].ToUpper()}"
        };

        _db.Payments.Add(payment);

        // Booking state is updated in the same transaction boundary to keep payment and booking consistent.
        booking.PaymentStatus = "PAID";
        booking.Status = "CONFIRMED";
        booking.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = payment.Id }, new PaymentDto
        {
            Id = payment.Id,
            PaymentCode = payment.PaymentCode,
            BookingId = payment.BookingId,
            BookingCode = booking.BookingCode,
            Method = payment.Method,
            Amount = payment.Amount,
            Status = payment.Status,
            PaidAt = payment.PaidAt,
            TransactionCode = payment.TransactionCode
        });
    }
}

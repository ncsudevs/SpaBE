using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.Bookings;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.DataLayer;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private readonly SpaDbContext _db;

    public BookingsController(SpaDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<BookingDto>>> GetAll([FromQuery] string? email)
    {
        var query = _db.Bookings
            .AsNoTracking()
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.Service)
            .AsQueryable();

        // Customer pages use the email filter to read only the current user's booking history.
        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = email.Trim().ToLower();
            query = query.Where(x => x.Email.ToLower() == normalizedEmail);
        }

        var data = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new BookingDto
            {
                Id = x.Id,
                BookingCode = x.BookingCode,
                FullName = x.FullName,
                Phone = x.Phone,
                Email = x.Email,
                AppointmentDate = x.AppointmentDate,
                AppointmentTime = x.AppointmentTime,
                Note = x.Note,
                TotalAmount = x.TotalAmount,
                Status = x.Status,
                PaymentStatus = x.PaymentStatus,
                CreatedAt = x.CreatedAt,
                Items = x.BookingDetails.Select(d => new BookingItemDto
                {
                    ServiceId = d.ServiceId,
                    ServiceName = d.Service != null ? d.Service.Name : string.Empty,
                    Quantity = d.Quantity,
                    UnitPrice = d.UnitPrice,
                    LineTotal = d.LineTotal
                }).ToList()
            })
            .ToListAsync();

        return Ok(data);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BookingDto>> GetById(int id)
    {
        var entity = await _db.Bookings
            .AsNoTracking()
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.Service)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (entity == null) return NotFound(new { message = "Booking not found" });

        return Ok(new BookingDto
        {
            Id = entity.Id,
            BookingCode = entity.BookingCode,
            FullName = entity.FullName,
            Phone = entity.Phone,
            Email = entity.Email,
            AppointmentDate = entity.AppointmentDate,
            AppointmentTime = entity.AppointmentTime,
            Note = entity.Note,
            TotalAmount = entity.TotalAmount,
            Status = entity.Status,
            PaymentStatus = entity.PaymentStatus,
            CreatedAt = entity.CreatedAt,
            Items = entity.BookingDetails.Select(d => new BookingItemDto
            {
                ServiceId = d.ServiceId,
                ServiceName = d.Service != null ? d.Service.Name : string.Empty,
                Quantity = d.Quantity,
                UnitPrice = d.UnitPrice,
                LineTotal = d.LineTotal
            }).ToList()
        });
    }

    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create(BookingCreateDto dto)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            return BadRequest(new { message = "Booking must have at least one item" });

        // Services are loaded from the database to validate availability and guarantee trusted pricing.
        var serviceIds = dto.Items.Select(x => x.ServiceId).Distinct().ToList();
        var services = await _db.Services
            .Where(x => serviceIds.Contains(x.Id) && x.Status == "ACTIVE")
            .ToListAsync();

        if (services.Count != serviceIds.Count)
            return BadRequest(new { message = "One or more services are invalid or inactive" });

        var booking = new Booking
        {
            BookingCode = $"BK-{DateTime.UtcNow:yyyyMMddHHmmss}",
            FullName = dto.FullName.Trim(),
            Phone = dto.Phone.Trim(),
            Email = dto.Email.Trim().ToLower(),
            AppointmentDate = dto.AppointmentDate,
            AppointmentTime = dto.AppointmentTime.Trim(),
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            Status = "PENDING",
            PaymentStatus = "UNPAID",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var item in dto.Items)
        {
            var service = services.First(x => x.Id == item.ServiceId);
            var lineTotal = service.Price * item.Quantity;

            booking.BookingDetails.Add(new BookingDetail
            {
                ServiceId = service.Id,
                Quantity = item.Quantity,
                UnitPrice = service.Price,
                LineTotal = lineTotal
            });
        }

        booking.TotalAmount = booking.BookingDetails.Sum(x => x.LineTotal);

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = booking.Id }, new BookingDto
        {
            Id = booking.Id,
            BookingCode = booking.BookingCode,
            FullName = booking.FullName,
            Phone = booking.Phone,
            Email = booking.Email,
            AppointmentDate = booking.AppointmentDate,
            AppointmentTime = booking.AppointmentTime,
            Note = booking.Note,
            TotalAmount = booking.TotalAmount,
            Status = booking.Status,
            PaymentStatus = booking.PaymentStatus,
            CreatedAt = booking.CreatedAt,
            Items = booking.BookingDetails.Select(d => new BookingItemDto
            {
                ServiceId = d.ServiceId,
                ServiceName = services.First(x => x.Id == d.ServiceId).Name,
                Quantity = d.Quantity,
                UnitPrice = d.UnitPrice,
                LineTotal = d.LineTotal
            }).ToList()
        });
    }
}

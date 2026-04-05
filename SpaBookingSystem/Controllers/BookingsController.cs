using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.Bookings;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.DataLayer;
using System.Security.Claims;
using SpaBookingSystem.Api.Helpers;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private static readonly string[] AdminAllowedStatuses = ["PENDING", "CONFIRMED", "COMPLETED", "CANCELLED"];

    private readonly SpaDbContext _db;

    public BookingsController(SpaDbContext db)
    {
        _db = db;
    }

    [Authorize]
    [HttpGet]
    public async Task<ActionResult<List<BookingDto>>> GetAll([FromQuery] string? email)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var currentEmail = User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();

        var query = _db.Bookings
            .AsNoTracking()
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.Service)
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.Staff)
            .Include(x => x.Payments)
            .AsQueryable();

        if (role == "ADMIN")
        {
            if (!string.IsNullOrWhiteSpace(email))
            {
                var normalizedEmail = email.Trim().ToLowerInvariant();
                query = query.Where(x => x.Email.ToLower() == normalizedEmail);
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(currentEmail))
                return Unauthorized(new { message = "Invalid token" });

            query = query.Where(x => x.Email.ToLower() == currentEmail);
        }

        var bookings = await query.OrderByDescending(x => x.CreatedAt).ToListAsync();
        return Ok(bookings.Select(MapBooking));
    }

    [Authorize]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<BookingDto>> GetById(int id)
    {
        var booking = await _db.Bookings
            .AsNoTracking()
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.Service)
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.Staff)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (booking == null) return NotFound(new { message = "Booking not found" });

        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var currentEmail = User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        if (role != "ADMIN" && booking.Email.Trim().ToLowerInvariant() != currentEmail)
            return Forbid();

        return Ok(MapBooking(booking));
    }

    [Authorize(Roles = "CUSTOMER")]
    [HttpGet("availability")]
    public async Task<ActionResult<AvailabilityDto>> GetAvailability([FromQuery] int serviceId, [FromQuery] DateOnly appointmentDate, [FromQuery] string appointmentTime)
    {
        var normalizedTime = NormalizeTime(appointmentTime);
        if (string.IsNullOrWhiteSpace(normalizedTime))
            return BadRequest(new { message = "Appointment time is required" });

        var service = await _db.Services.AsNoTracking().FirstOrDefaultAsync(x => x.Id == serviceId && x.Status == "ACTIVE");
        if (service == null)
            return NotFound(new { message = "Service not found" });

        var bookedQuantity = await GetBookedQuantityAsync(serviceId, appointmentDate, normalizedTime, service.Duration);
        return Ok(new AvailabilityDto
        {
            ServiceId = serviceId,
            AppointmentDate = appointmentDate,
            AppointmentTime = normalizedTime,
            SlotCapacity = service.SlotCapacity,
            BookedQuantity = bookedQuantity,
            RemainingSlots = Math.Max(0, service.SlotCapacity - bookedQuantity)
        });
    }

    [Authorize(Roles = "CUSTOMER")]
    [HttpPost]
    public async Task<ActionResult<BookingDto>> Create(BookingCreateDto dto)
    {
        if (dto.Items == null || dto.Items.Count == 0)
            return BadRequest(new { message = "Booking must have at least one item" });

        var currentEmail = User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(currentEmail))
            return Unauthorized(new { message = "Invalid token" });

        var customer = await _db.Customers.FirstOrDefaultAsync(x => x.Email.ToLower() == currentEmail);
        if (customer == null || !customer.IsActive)
            return Unauthorized(new { message = "Customer account not found or inactive" });

        if (!PhoneHelper.TryNormalizePhone(dto.Phone, dto.Region, out var normalizedPhone, out var phoneError))
            return BadRequest(new { message = phoneError });
        var normalizedItems = dto.Items.Select(x => new NormalizedBookingItem
        {
            ServiceId = x.ServiceId,
            Quantity = x.Quantity,
            AppointmentDate = x.AppointmentDate,
            AppointmentTime = NormalizeTime(x.AppointmentTime)
        }).ToList();

        if (normalizedItems.Any(x => x.Quantity <= 0 || string.IsNullOrWhiteSpace(x.AppointmentTime)))
            return BadRequest(new { message = "Each booking item must have a valid quantity and preferred time" });

        if (normalizedItems.Any(x => x.AppointmentDate < DateOnly.FromDateTime(DateTime.Today)))
            return BadRequest(new { message = "Appointment date cannot be in the past" });

        if (dto.IsGroupBooking)
        {
            if (normalizedItems.Count != 1)
                return BadRequest(new { message = "Group booking currently supports one service at a time." });

            if (dto.GroupSize <= 1)
                return BadRequest(new { message = "Group booking requires at least 2 people." });

            if (normalizedItems[0].Quantity != dto.GroupSize)
                return BadRequest(new { message = "Group size must match the selected service quantity." });
        }
        else
        {
            var duplicateSlot = normalizedItems
                .GroupBy(x => new { x.AppointmentDate, x.AppointmentTime })
                .FirstOrDefault(g => g.Count() > 1);

            if (duplicateSlot != null)
                return BadRequest(new { message = "Two services cannot use the same appointment date and preferred time in one personal booking." });
        }

        var userSlotConflicts = await FindUserSlotConflictsAsync(currentEmail, normalizedItems.Select(x => (x.AppointmentDate, x.AppointmentTime)).Distinct().ToList());
        if (userSlotConflicts.Any())
        {
            var first = userSlotConflicts.First();
            return BadRequest(new { message = $"You already have another booking at {first.AppointmentDate:dd/MM/yyyy} {first.AppointmentTime}. Please choose a different slot." });
        }

        var serviceIds = normalizedItems.Select(x => x.ServiceId).Distinct().ToList();
        var services = await _db.Services
            .Where(x => serviceIds.Contains(x.Id) && x.Status == "ACTIVE")
            .ToListAsync();

        if (services.Count != serviceIds.Count)
            return BadRequest(new { message = "One or more services are invalid or inactive" });

        foreach (var item in normalizedItems)
        {
            var service = services.First(x => x.Id == item.ServiceId);
            var bookedQuantity = await GetBookedQuantityAsync(item.ServiceId, item.AppointmentDate, item.AppointmentTime, service.Duration);
            var remainingSlots = service.SlotCapacity - bookedQuantity;

            if (item.Quantity > remainingSlots)
            {
                return BadRequest(new
                {
                    message = $"Only {Math.Max(0, remainingSlots)} slot(s) left for {service.Name} at {item.AppointmentDate:dd/MM/yyyy} {item.AppointmentTime}."
                });
            }
        }

        customer.FullName = string.IsNullOrWhiteSpace(dto.FullName) ? customer.FullName : dto.FullName.Trim();
        customer.Phone = normalizedPhone;
        customer.UpdatedAt = DateTime.UtcNow;

        var firstSlot = normalizedItems.OrderBy(x => x.AppointmentDate).ThenBy(x => x.AppointmentTime).First();
        var totalPeople = dto.IsGroupBooking ? dto.GroupSize : 1;

        var booking = new Booking
        {
            BookingCode = $"BK-{DateTime.UtcNow:yyyyMMddHHmmss}",
            FullName = customer.FullName,
            Phone = customer.Phone ?? string.Empty,
            Email = customer.Email,
            AppointmentDate = firstSlot.AppointmentDate,
            AppointmentTime = firstSlot.AppointmentTime,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            Status = "PENDING",
            PaymentStatus = "UNPAID",
            IsGroupBooking = dto.IsGroupBooking,
            GroupSize = totalPeople,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        foreach (var item in normalizedItems)
        {
            var service = services.First(x => x.Id == item.ServiceId);
            booking.BookingDetails.Add(new BookingDetail
            {
                ServiceId = service.Id,
                Quantity = item.Quantity,
                AppointmentDate = item.AppointmentDate,
                AppointmentTime = item.AppointmentTime,
                UnitPrice = service.Price,
                LineTotal = service.Price * item.Quantity
            });
        }

        booking.TotalAmount = booking.BookingDetails.Sum(x => x.LineTotal);

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        await _db.Entry(booking).Collection(x => x.BookingDetails).Query().Include(x => x.Service).Include(x => x.Staff).LoadAsync();
        return CreatedAtAction(nameof(GetById), new { id = booking.Id }, MapBooking(booking));
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<BookingDto>> UpdateStatus(int id, BookingStatusUpdateDto dto)
    {
        var status = (dto.Status ?? string.Empty).Trim().ToUpperInvariant();
        if (!AdminAllowedStatuses.Contains(status))
            return BadRequest(new { message = "Invalid booking status" });

        var booking = await _db.Bookings
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.Service)
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.Staff)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (booking == null)
            return NotFound(new { message = "Booking not found" });

        booking.Status = status;
        booking.UpdatedAt = DateTime.UtcNow;

        if (status == "CANCELLED")
            booking.PaymentStatus = booking.PaymentStatus == "PAID" ? "REFUNDED" : booking.PaymentStatus;

        await _db.SaveChangesAsync();
        return Ok(MapBooking(booking));
    }

    [Authorize(Roles = "ADMIN")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var booking = await _db.Bookings
            .Include(x => x.Payments)
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.Staff)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (booking == null)
            return NotFound(new { message = "Booking not found" });

        _db.Bookings.Remove(booking);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<int> GetBookedQuantityAsync(int serviceId, DateOnly appointmentDate, string appointmentTime, int targetDurationMinutes)
    {
        var targetStart = ParseTimeToMinutes(appointmentTime);
        if (targetStart == null) return 0;

        var details = await _db.BookingDetails
            .AsNoTracking()
            .Include(x => x.Booking)
            .Include(x => x.Service)
            .Where(x => x.ServiceId == serviceId
                && x.AppointmentDate == appointmentDate
                && x.Booking != null
                && x.Booking.Status != "CANCELLED")
            .Select(x => new
            {
                x.Quantity,
                x.AppointmentTime,
                Duration = x.Service != null ? x.Service.Duration : (int?)null
            })
            .ToListAsync();

        var targetEnd = targetStart.Value + Math.Max(1, targetDurationMinutes);
        var total = 0;

        foreach (var d in details)
        {
            var existingStart = ParseTimeToMinutes(d.AppointmentTime);
            if (existingStart == null) continue;
            var existingDuration = d.Duration ?? targetDurationMinutes;
            var existingEnd = existingStart.Value + Math.Max(1, existingDuration);

            var overlap = existingStart.Value < targetEnd && targetStart.Value < existingEnd;
            if (overlap)
            {
                total += d.Quantity;
            }
        }

        return total;
    }

    private static int? ParseTimeToMinutes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, out var dt))
        {
            return dt.Hour * 60 + dt.Minute;
        }
        // fallback for formats like "09:00" without date
        var parts = value.Split(':');
        if (parts.Length >= 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1].Substring(0, 2), out var m))
        {
            return (h % 24) * 60 + Math.Clamp(m, 0, 59);
        }
        return null;
    }

    private async Task<List<(DateOnly AppointmentDate, string AppointmentTime)>> FindUserSlotConflictsAsync(string currentEmail, List<(DateOnly AppointmentDate, string AppointmentTime)> slots)
    {
        var dates = slots.Select(x => x.AppointmentDate).Distinct().ToList();
        var times = slots.Select(x => x.AppointmentTime).Distinct().ToList();

        var existing = await _db.BookingDetails
            .AsNoTracking()
            .Include(x => x.Booking)
            .Where(x => x.Booking != null
                && x.Booking.Email.ToLower() == currentEmail
                && x.Booking.Status != "CANCELLED"
                && dates.Contains(x.AppointmentDate)
                && times.Contains(x.AppointmentTime))
            .Select(x => new { x.AppointmentDate, x.AppointmentTime })
            .ToListAsync();

        return slots.Where(slot => existing.Any(x => x.AppointmentDate == slot.AppointmentDate && x.AppointmentTime == slot.AppointmentTime)).ToList();
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
            CreatedAt = ToUtc(booking.CreatedAt),
            UpdatedAt = ToUtc(booking.UpdatedAt),
            PaymentAttempts = booking.PaymentAttempts,
            LastPaymentCreatedAt = booking.Payments?
                .OrderByDescending(p => p.PaidAt)
                .Select(p => (DateTime?)ToUtc(p.PaidAt))
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

    private static string NormalizeTime(string time) => (time ?? string.Empty).Trim().ToUpperInvariant();

    private static DateTime ToUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc) return value;
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private sealed class NormalizedBookingItem
    {
        public int ServiceId { get; set; }
        public int Quantity { get; set; }
        public DateOnly AppointmentDate { get; set; }
        public string AppointmentTime { get; set; } = string.Empty;
    }
}

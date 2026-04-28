using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.Api.Dtos.Bookings;
using SpaBookingSystem.ApplicationCore.Constants;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.DataLayer;
using System.Security.Claims;
using SpaBookingSystem.Api.Helpers;
using SpaBookingSystem.Api.Services;

namespace SpaBookingSystem.Api.Controllers;

[ApiController]
[Route("api/bookings")]
public class BookingsController : ControllerBase
{
    private static readonly string[] AdminAllowedStatuses =
    [
        BookingStatusNames.Pending,
        BookingStatusNames.Confirmed,
        BookingStatusNames.Completed,
        BookingStatusNames.Cancelled
    ];

    private readonly SpaDbContext _db;
    private readonly IBookingStaffingService _bookingStaffingService;

    public BookingsController(SpaDbContext db, IBookingStaffingService bookingStaffingService)
    {
        _db = db;
        _bookingStaffingService = bookingStaffingService;
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
                    .ThenInclude(x => x.Category)
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.StaffAssignments)
                    .ThenInclude(x => x.Staff)
            .Include(x => x.Payments)
            .AsQueryable();

        if (IsManagementRole(role))
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
                    .ThenInclude(x => x.Category)
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.StaffAssignments)
                    .ThenInclude(x => x.Staff)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (booking == null) return NotFound(new { message = "Booking not found" });

        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var currentEmail = User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        if (!IsManagementRole(role) && booking.Email.Trim().ToLowerInvariant() != currentEmail)
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

        // Prevent selecting a past time on the same day (Bangkok time).
        var bangkokNow = GetBangkokNow();
        var todayBk = DateOnly.FromDateTime(bangkokNow.Date);
        var nowMinutes = bangkokNow.Hour * 60 + bangkokNow.Minute;

        foreach (var item in normalizedItems)
        {
            var parsedMinutes = ParseTimeToMinutes(item.AppointmentTime);
            if (item.AppointmentDate < todayBk)
                return BadRequest(new { message = "Appointment date cannot be in the past" });

            if (item.AppointmentDate == todayBk && parsedMinutes.HasValue && parsedMinutes.Value <= nowMinutes)
                return BadRequest(new { message = "Appointment time must be in the future for today." });
        }

        if (normalizedItems.Any(x => x.Quantity <= 0 || string.IsNullOrWhiteSpace(x.AppointmentTime)))
            return BadRequest(new { message = "Each booking item must have a valid quantity and preferred time" });

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
            BookingCode = $"BK-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
            FullName = customer.FullName,
            Phone = customer.Phone ?? string.Empty,
            Email = customer.Email,
            AppointmentDate = firstSlot.AppointmentDate,
            AppointmentTime = firstSlot.AppointmentTime,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            Status = BookingStatusNames.Pending,
            PaymentStatus = PaymentStatusNames.Unpaid,
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

        await _db.Entry(booking)
            .Collection(x => x.BookingDetails)
            .Query()
            .Include(x => x.Service)
                .ThenInclude(x => x.Category)
            .Include(x => x.StaffAssignments)
                .ThenInclude(x => x.Staff)
            .LoadAsync();
        await _db.Entry(booking).Collection(x => x.Payments).LoadAsync();
        return CreatedAtAction(nameof(GetById), new { id = booking.Id }, MapBooking(booking));
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Cashier}")]
    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<BookingDto>> UpdateStatus(int id, BookingStatusUpdateDto dto)
    {
        var status = (dto.Status ?? string.Empty).Trim().ToUpperInvariant();
        if (!AdminAllowedStatuses.Contains(status))
            return BadRequest(new { message = "Invalid booking status" });

        var booking = await _db.Bookings
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.Service)
                    .ThenInclude(x => x.Category)
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.StaffAssignments)
                    .ThenInclude(x => x.Staff)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (booking == null)
            return NotFound(new { message = "Booking not found" });

        if (booking.Status == BookingStatusNames.Cancelled && status != BookingStatusNames.Cancelled)
            return BadRequest(new { message = "Cancelled bookings are final and cannot be reopened." });

        switch (status)
        {
            case BookingStatusNames.Pending:
                if (booking.PaymentStatus == PaymentStatusNames.Paid)
                    return BadRequest(new { message = "Paid bookings cannot be moved back to PENDING." });

                ResetCheckIn(booking);
                break;

            case BookingStatusNames.Confirmed:
                if (booking.PaymentStatus != PaymentStatusNames.Paid)
                    return BadRequest(new { message = "Only paid bookings can be confirmed." });

                if (booking.Status == BookingStatusNames.Completed)
                    return BadRequest(new { message = "Completed bookings cannot be moved back to CONFIRMED." });
                break;

            case BookingStatusNames.Completed:
                if (booking.PaymentStatus != PaymentStatusNames.Paid)
                    return BadRequest(new { message = "Check-in can only be completed after payment is confirmed." });

                if (booking.Status != BookingStatusNames.Confirmed && booking.Status != BookingStatusNames.Completed)
                    return BadRequest(new { message = "Booking must be confirmed before it can be completed." });

                if (!booking.IsCheckedIn)
                    return BadRequest(new { message = "Booking must be checked in before it can be completed." });

                if (!_bookingStaffingService.IsFullyStaffed(booking))
                    return BadRequest(new { message = "Assign enough staff quantity to every booking item before completing check-in." });
                break;

            case BookingStatusNames.Cancelled:
                if (booking.Status == BookingStatusNames.Completed)
                    return BadRequest(new { message = "Completed bookings cannot be cancelled." });

                if (booking.IsCheckedIn)
                    return BadRequest(new { message = "Undo check-in before cancelling this booking." });

                if (booking.PaymentStatus == PaymentStatusNames.Paid)
                    return BadRequest(new { message = "Paid bookings must be refunded from the payments screen so a refund reason can be recorded." });

                ResetCheckIn(booking);
                break;
        }

        booking.Status = status;
        booking.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(MapBooking(booking));
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Cashier}")]
    [HttpPatch("{id:int}/check-in")]
    public async Task<ActionResult<BookingDto>> UpdateCheckIn(int id, BookingCheckInUpdateDto dto)
    {
        var booking = await _db.Bookings
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.Service)
                    .ThenInclude(x => x.Category)
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.StaffAssignments)
                    .ThenInclude(x => x.Staff)
            .Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (booking == null)
            return NotFound(new { message = "Booking not found" });

        if (booking.PaymentStatus != PaymentStatusNames.Paid)
            return Conflict(new { message = "Only paid bookings can be checked in." });

        if (booking.Status == BookingStatusNames.Cancelled)
            return Conflict(new { message = "Cancelled bookings cannot be checked in." });

        if (dto.IsCheckedIn)
        {
            if (booking.Status != BookingStatusNames.Confirmed)
                return Conflict(new { message = "Only confirmed bookings can be checked in." });
        }
        else if (booking.Status == BookingStatusNames.Completed)
        {
            return Conflict(new { message = "Completed bookings cannot be unchecked." });
        }

        booking.IsCheckedIn = dto.IsCheckedIn;
        booking.CheckedInAt = dto.IsCheckedIn ? DateTime.UtcNow : null;
        booking.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(MapBooking(booking));
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Cashier}")]
    [HttpPost("details/{detailId:int}/staff-assignments")]
    public async Task<ActionResult<BookingDto>> AddDetailStaffAssignment(int detailId, BookingStaffAssignmentUpsertDto dto)
    {
        if (dto.AssignedQuantity <= 0)
            return BadRequest(new { message = "Assigned quantity must be greater than zero." });

        var detail = await LoadDetailForAssignmentAsync(detailId);
        if (detail == null || detail.Booking == null)
            return NotFound(new { message = "Booking detail not found" });

        var validationError = await ValidateAssignmentRequestAsync(detail, dto.StaffId, dto.AssignedQuantity);
        if (validationError != null)
            return validationError;

        if (detail.StaffAssignments.Any(x => x.StaffId == dto.StaffId))
            return Conflict(new { message = "This staff member is already assigned to the booking detail. Update the existing assignment instead." });

        detail.StaffAssignments.Add(new BookingDetailStaffAssignment
        {
            StaffId = dto.StaffId,
            AssignedQuantity = dto.AssignedQuantity,
            CreatedAt = DateTime.UtcNow,
        });
        detail.Booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(MapBooking(detail.Booking));
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Cashier}")]
    [HttpPatch("details/{detailId:int}/staff-assignments/{assignmentId:int}")]
    public async Task<ActionResult<BookingDto>> UpdateDetailStaffAssignment(int detailId, int assignmentId, BookingStaffAssignmentUpsertDto dto)
    {
        if (dto.AssignedQuantity <= 0)
            return BadRequest(new { message = "Assigned quantity must be greater than zero." });

        var detail = await LoadDetailForAssignmentAsync(detailId);
        if (detail == null || detail.Booking == null)
            return NotFound(new { message = "Booking detail not found" });

        var assignment = detail.StaffAssignments.FirstOrDefault(x => x.Id == assignmentId);
        if (assignment == null)
            return NotFound(new { message = "Staff assignment not found" });

        if (assignment.StaffId != dto.StaffId && detail.StaffAssignments.Any(x => x.StaffId == dto.StaffId))
            return Conflict(new { message = "This staff member is already assigned to the booking detail." });

        var validationError = await ValidateAssignmentRequestAsync(detail, dto.StaffId, dto.AssignedQuantity, assignmentId);
        if (validationError != null)
            return validationError;

        assignment.StaffId = dto.StaffId;
        assignment.AssignedQuantity = dto.AssignedQuantity;
        detail.Booking.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(MapBooking(detail.Booking));
    }

    [Authorize(Roles = $"{RoleNames.Admin},{RoleNames.Cashier}")]
    [HttpDelete("details/{detailId:int}/staff-assignments/{assignmentId:int}")]
    public async Task<ActionResult<BookingDto>> DeleteDetailStaffAssignment(int detailId, int assignmentId)
    {
        var detail = await LoadDetailForAssignmentAsync(detailId);
        if (detail == null || detail.Booking == null)
            return NotFound(new { message = "Booking detail not found" });

        var assignment = detail.StaffAssignments.FirstOrDefault(x => x.Id == assignmentId);
        if (assignment == null)
            return NotFound(new { message = "Staff assignment not found" });

        if (detail.Booking.Status == BookingStatusNames.Completed)
            return Conflict(new { message = "Completed bookings cannot be changed." });

        _db.BookingDetailStaffAssignments.Remove(assignment);
        detail.Booking.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(MapBooking(detail.Booking));
    }

    [Authorize(Roles = "ADMIN")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var booking = await _db.Bookings
            .Include(x => x.Payments)
            .Include(x => x.BookingDetails)
                .ThenInclude(x => x.StaffAssignments)
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
                && x.Booking.Status != BookingStatusNames.Cancelled)
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
                && x.Booking.Status != BookingStatusNames.Cancelled
                && dates.Contains(x.AppointmentDate)
                && times.Contains(x.AppointmentTime))
            .Select(x => new { x.AppointmentDate, x.AppointmentTime })
            .ToListAsync();

        return slots.Where(slot => existing.Any(x => x.AppointmentDate == slot.AppointmentDate && x.AppointmentTime == slot.AppointmentTime)).ToList();
    }

    private async Task<BookingDetail?> LoadDetailForAssignmentAsync(int detailId)
    {
        return await _db.BookingDetails
            .Include(d => d.Service)
                .ThenInclude(x => x!.Category)
            .Include(d => d.Booking)
                .ThenInclude(x => x!.Payments)
            .Include(d => d.Booking)
                .ThenInclude(x => x!.BookingDetails)
                    .ThenInclude(x => x.Service)
                        .ThenInclude(x => x!.Category)
            .Include(d => d.Booking)
                .ThenInclude(x => x!.BookingDetails)
                    .ThenInclude(x => x.StaffAssignments)
                        .ThenInclude(x => x.Staff)
            .FirstOrDefaultAsync(d => d.Id == detailId);
    }

    private async Task<ActionResult?> ValidateAssignmentRequestAsync(
        BookingDetail detail,
        int staffId,
        int assignedQuantity,
        int? ignoreAssignmentId = null)
    {
        if (detail.Booking == null)
            return NotFound(new { message = "Booking not found" });

        if (detail.Booking.PaymentStatus != PaymentStatusNames.Paid)
            return Conflict(new { message = "Staff can only be assigned after payment is confirmed." });

        if (detail.Booking.Status == BookingStatusNames.Completed)
            return Conflict(new { message = "Completed bookings cannot be changed." });

        var totalAssignedElsewhere = detail.StaffAssignments
            .Where(x => x.Id != ignoreAssignmentId)
            .Sum(x => x.AssignedQuantity);

        if (totalAssignedElsewhere + assignedQuantity > detail.Quantity)
            return Conflict(new { message = $"Assigned quantity exceeds the required service quantity ({detail.Quantity})." });

        var staff = await _db.Staffs
            .Include(s => s.StaffCategories)
            .FirstOrDefaultAsync(s => s.Id == staffId && s.IsActive);

        if (staff == null)
            return NotFound(new { message = "Staff not found or inactive" });

        if (detail.Service == null || !staff.StaffCategories.Any(sc => sc.CategoryId == detail.Service.CategoryId))
            return Conflict(new { message = "Staff does not match the service category." });

        var remainingCapacity = await _bookingStaffingService.GetRemainingCapacityAsync(
            staff,
            detail.AppointmentDate,
            detail.AppointmentTime ?? string.Empty,
            detail.Service.Duration,
            ignoreAssignmentId);

        if (assignedQuantity > remainingCapacity)
        {
            return Conflict(new
            {
                message = $"{staff.FullName} only has {remainingCapacity} slot(s) of remaining capacity at this time."
            });
        }

        return null;
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
            CreatedAt = ToUtc(booking.CreatedAt),
            UpdatedAt = ToUtc(booking.UpdatedAt),
            PaymentAttempts = booking.PaymentAttempts,
            LastPaymentCreatedAt = latestPayment != null ? ToUtc(latestPayment.PaidAt) : null,
            LatestPaymentId = latestPayment?.Id,
            LatestPaymentMethod = latestPayment?.Method,
            IsCheckedIn = effectiveCheckedIn,
            CheckedInAt = effectiveCheckedIn && booking.CheckedInAt != null ? ToUtc(booking.CheckedInAt.Value) : null,
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
                        StaffMaxConcurrent = x.Staff?.MaxConcurrent ?? 0,
                    })
                    .ToList()
            }).ToList()
        };
    }

    private static string NormalizeTime(string time) => (time ?? string.Empty).Trim().ToUpperInvariant();

    private static DateTime ToUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc) return value;
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static void ResetCheckIn(Booking booking)
    {
        booking.IsCheckedIn = false;
        booking.CheckedInAt = null;
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
        && booking.Status == BookingStatusNames.Confirmed
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

    private static DateTime GetBangkokNow()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            return DateTime.UtcNow.AddHours(7); // fallback UTC+7
        }
    }

    private static bool IsManagementRole(string role) =>
        role == RoleNames.Admin || role == RoleNames.Cashier;

    private sealed class NormalizedBookingItem
    {
        public int ServiceId { get; set; }
        public int Quantity { get; set; }
        public DateOnly AppointmentDate { get; set; }
        public string AppointmentTime { get; set; } = string.Empty;
    }
}

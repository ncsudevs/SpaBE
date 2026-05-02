using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpaBookingSystem.ApplicationCore.Constants;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.DataLayer;

namespace SpaBookingSystem.Services.Bookings;

public sealed class BookingStaffingService : IBookingStaffingService
{
    private readonly SpaDbContext _db;
    private readonly ILogger<BookingStaffingService> _logger;

    public BookingStaffingService(SpaDbContext db, ILogger<BookingStaffingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public int GetAssignedQuantity(BookingDetail detail) =>
        detail.StaffAssignments.Sum(x => Math.Max(0, x.AssignedQuantity));

    public int GetUnassignedQuantity(BookingDetail detail) =>
        Math.Max(0, detail.Quantity - GetAssignedQuantity(detail));

    public bool IsFullyStaffed(BookingDetail detail) =>
        GetUnassignedQuantity(detail) == 0;

    public bool IsFullyStaffed(Booking booking) =>
        booking.BookingDetails.All(IsFullyStaffed);

    public string? BuildDetailStaffingWarning(BookingDetail detail)
    {
        var unassigned = GetUnassignedQuantity(detail);
        if (unassigned == 0)
            return null;

        var serviceName = detail.Service?.Name ?? "This service";
        return $"{serviceName} still needs {unassigned} more staffed slot(s).";
    }

    public string? BuildBookingStaffingWarning(Booking booking)
    {
        var warnings = booking.BookingDetails
            .Select(BuildDetailStaffingWarning)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (warnings.Count == 0)
            return null;

        return "Some services are still waiting for enough staff capacity. Cashier can continue, then finish staffing later.";
    }

    public async Task<int> GetRemainingCapacityAsync(
        Staff staff,
        DateOnly date,
        string time,
        int durationMinutes,
        int? ignoreAssignmentId = null,
        CancellationToken cancellationToken = default)
    {
        var start = ParseTimeToMinutes(time);
        if (start == null)
            return 0;

        var targetEnd = start.Value + Math.Max(1, durationMinutes);

        var assignments = await _db.BookingDetailStaffAssignments
            .AsNoTracking()
            .Include(x => x.BookingDetail)
                .ThenInclude(x => x!.Booking)
            .Include(x => x.BookingDetail)
                .ThenInclude(x => x!.Service)
            .Where(x => x.StaffId == staff.Id
                && x.BookingDetail != null
                && x.BookingDetail.AppointmentDate == date
                && x.BookingDetail.Booking != null
                && x.BookingDetail.Booking.Status != BookingStatusNames.Cancelled
                && x.Id != ignoreAssignmentId)
            .ToListAsync(cancellationToken);

        var usedCapacity = 0;

        foreach (var assignment in assignments)
        {
            var detail = assignment.BookingDetail;
            if (detail?.Service == null)
                continue;

            var existingStart = ParseTimeToMinutes(detail.AppointmentTime);
            if (existingStart == null)
                continue;

            var existingEnd = existingStart.Value + Math.Max(1, detail.Service.Duration);
            var overlap = existingStart.Value < targetEnd && start.Value < existingEnd;

            if (overlap)
            {
                usedCapacity += Math.Max(0, assignment.AssignedQuantity);
            }
        }

        return Math.Max(0, staff.MaxConcurrent - usedCapacity);
    }

    public async Task<BookingStaffingResult> AutoAssignAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        await _db.Entry(booking)
            .Collection(x => x.BookingDetails)
            .Query()
            .Include(x => x.Service)
            .Include(x => x.StaffAssignments)
                .ThenInclude(x => x.Staff)
            .LoadAsync(cancellationToken);

        var details = booking.BookingDetails
            .Where(x => x.Service != null)
            .ToList();

        if (details.Count == 0)
            return BookingStaffingResult.Empty;

        var categoryIds = details.Select(x => x.Service!.CategoryId).Distinct().ToList();

        var eligibleStaff = await _db.Staffs
            .AsNoTracking()
            .Include(x => x.StaffCategories)
            .Where(x => x.IsActive && x.StaffCategories.Any(sc => categoryIds.Contains(sc.CategoryId)))
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        if (eligibleStaff.Count == 0)
        {
            return new BookingStaffingResult
            {
                Warnings = details
                    .Select(detail => BuildDetailWarning(detail, detail.Quantity))
                    .ToList()
            };
        }

        var warnings = new List<string>();
        var plannedLoads = new List<PlannedAssignmentLoad>();

        foreach (var detail in details)
        {
            var remaining = GetUnassignedQuantity(detail);
            if (remaining == 0 || detail.Service == null)
                continue;

            var serviceCategoryId = detail.Service.CategoryId;
            var candidates = eligibleStaff
                .Where(x => x.StaffCategories.Any(sc => sc.CategoryId == serviceCategoryId))
                .ToList();

            while (remaining > 0)
            {
                var staffCapacities = new List<(Staff Staff, int RemainingCapacity)>();

                foreach (var staff in candidates)
                {
                    // Combine persisted assignments with in-memory planned loads
                    // so one auto-assign pass cannot overbook the same staff.
                    var persistedRemaining = await GetRemainingCapacityAsync(
                        staff,
                        detail.AppointmentDate,
                        detail.AppointmentTime ?? string.Empty,
                        detail.Service.Duration,
                        cancellationToken: cancellationToken);

                    var plannedUsage = GetPlannedUsage(plannedLoads, staff, detail);
                    var effectiveRemaining = Math.Max(0, persistedRemaining - plannedUsage);

                    if (effectiveRemaining > 0)
                    {
                        staffCapacities.Add((staff, effectiveRemaining));
                    }
                }

                var nextCandidate = staffCapacities
                    .OrderByDescending(x => x.RemainingCapacity)
                    .ThenBy(x => x.Staff.Id)
                    .FirstOrDefault();

                if (nextCandidate.Staff == null || nextCandidate.RemainingCapacity <= 0)
                    break;

                var assignedQuantity = Math.Min(remaining, nextCandidate.RemainingCapacity);
                if (assignedQuantity <= 0)
                    break;

                var existingAssignment = detail.StaffAssignments
                    .FirstOrDefault(x => x.StaffId == nextCandidate.Staff.Id);

                if (existingAssignment == null)
                {
                    // Reuse the booking detail collection so the caller sees
                    // the new assignments immediately on the same entity graph.
                    existingAssignment = new BookingDetailStaffAssignment
                    {
                        StaffId = nextCandidate.Staff.Id,
                        AssignedQuantity = assignedQuantity,
                        CreatedAt = DateTime.UtcNow,
                    };

                    detail.StaffAssignments.Add(existingAssignment);
                    _db.BookingDetailStaffAssignments.Add(existingAssignment);
                }
                else
                {
                    existingAssignment.AssignedQuantity += assignedQuantity;
                }

                plannedLoads.Add(new PlannedAssignmentLoad
                {
                    StaffId = nextCandidate.Staff.Id,
                    AppointmentDate = detail.AppointmentDate,
                    AppointmentTime = detail.AppointmentTime ?? string.Empty,
                    Duration = detail.Service.Duration,
                    AssignedQuantity = assignedQuantity,
                });
                remaining -= assignedQuantity;
            }

            if (remaining > 0)
            {
                warnings.Add(BuildDetailWarning(detail, remaining));
                _logger.LogInformation(
                    "Booking {BookingCode} detail {DetailId} is only partially staffed. Remaining quantity: {Remaining}",
                    booking.BookingCode,
                    detail.Id,
                    remaining);
            }
        }

        booking.UpdatedAt = DateTime.UtcNow;

        return new BookingStaffingResult
        {
            Warnings = warnings
        };
    }

    private static int GetPlannedUsage(
        List<PlannedAssignmentLoad> plannedLoads,
        Staff staff,
        BookingDetail detail)
    {
        var start = ParseTimeToMinutes(detail.AppointmentTime);
        if (start == null)
            return 0;

        var targetEnd = start.Value + Math.Max(1, detail.Service?.Duration ?? 1);
        var usedCapacity = 0;

        foreach (var load in plannedLoads)
        {
            if (load.StaffId != staff.Id || load.AppointmentDate != detail.AppointmentDate)
                continue;

            var existingStart = ParseTimeToMinutes(load.AppointmentTime);
            if (existingStart == null)
                continue;

            var existingEnd = existingStart.Value + Math.Max(1, load.Duration);
            var overlap = existingStart.Value < targetEnd && start.Value < existingEnd;

            if (overlap)
            {
                usedCapacity += load.AssignedQuantity;
            }
        }

        return usedCapacity;
    }

    private static string BuildDetailWarning(BookingDetail detail, int remainingQuantity)
    {
        var serviceName = detail.Service?.Name ?? "This service";
        return $"{serviceName} at {detail.AppointmentDate:dd/MM/yyyy} {detail.AppointmentTime} still needs {remainingQuantity} more staffed slot(s).";
    }

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

    private sealed class PlannedAssignmentLoad
    {
        public int StaffId { get; set; }
        public DateOnly AppointmentDate { get; set; }
        public string AppointmentTime { get; set; } = string.Empty;
        public int Duration { get; set; }
        public int AssignedQuantity { get; set; }
    }
}

public sealed class BookingStaffingResult
{
    public static BookingStaffingResult Empty { get; } = new();
    public List<string> Warnings { get; init; } = new();
    public bool HasIncompleteStaffing => Warnings.Count > 0;
}

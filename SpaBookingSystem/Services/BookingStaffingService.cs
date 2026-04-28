using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.ApplicationCore.Constants;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.DataLayer;

namespace SpaBookingSystem.Api.Services;

public interface IBookingStaffingService
{
    int GetAssignedQuantity(BookingDetail detail);
    int GetUnassignedQuantity(BookingDetail detail);
    bool IsFullyStaffed(BookingDetail detail);
    bool IsFullyStaffed(Booking booking);
    string? BuildDetailStaffingWarning(BookingDetail detail);
    string? BuildBookingStaffingWarning(Booking booking);
    Task<int> GetRemainingCapacityAsync(
        Staff staff,
        DateOnly date,
        string time,
        int durationMinutes,
        int? ignoreAssignmentId = null,
        CancellationToken cancellationToken = default);
    Task<BookingStaffingResult> AutoAssignAsync(Booking booking, CancellationToken cancellationToken = default);
}

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

        var appointmentDates = details.Select(x => x.AppointmentDate).Distinct().ToList();
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

        var scheduledAssignments = await _db.BookingDetailStaffAssignments
            .AsNoTracking()
            .Include(x => x.BookingDetail)
                .ThenInclude(x => x!.Booking)
            .Include(x => x.BookingDetail)
                .ThenInclude(x => x!.Service)
            .Where(x => x.BookingDetail != null
                && appointmentDates.Contains(x.BookingDetail.AppointmentDate)
                && x.BookingDetail.Booking != null
                && x.BookingDetail.Booking.Status != BookingStatusNames.Cancelled)
            .ToListAsync(cancellationToken);

        var loads = scheduledAssignments
            .Where(x => x.BookingDetail?.Service != null)
            .Select(x => new PlannedAssignmentLoad
            {
                AssignmentId = x.Id,
                BookingDetailId = x.BookingDetailId,
                StaffId = x.StaffId,
                AppointmentDate = x.BookingDetail!.AppointmentDate,
                AppointmentTime = x.BookingDetail.AppointmentTime ?? string.Empty,
                Duration = x.BookingDetail.Service!.Duration,
                AssignedQuantity = x.AssignedQuantity,
            })
            .ToList();

        var warnings = new List<string>();

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
                var nextCandidate = candidates
                    .Select(staff => new
                    {
                        Staff = staff,
                        RemainingCapacity = GetRemainingCapacityFromLoads(loads, staff, detail)
                    })
                    .Where(x => x.RemainingCapacity > 0)
                    .OrderByDescending(x => x.RemainingCapacity)
                    .ThenBy(x => x.Staff.Id)
                    .FirstOrDefault();

                if (nextCandidate == null)
                    break;

                var assignedQuantity = Math.Min(remaining, nextCandidate.RemainingCapacity);
                if (assignedQuantity <= 0)
                    break;

                var existingAssignment = detail.StaffAssignments
                    .FirstOrDefault(x => x.StaffId == nextCandidate.Staff.Id);

                if (existingAssignment == null)
                {
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

                UpsertLoad(loads, detail, nextCandidate.Staff.Id, assignedQuantity, existingAssignment.Id);
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

    private static int GetRemainingCapacityFromLoads(
        List<PlannedAssignmentLoad> loads,
        Staff staff,
        BookingDetail detail)
    {
        var start = ParseTimeToMinutes(detail.AppointmentTime);
        if (start == null)
            return 0;

        var targetEnd = start.Value + Math.Max(1, detail.Service?.Duration ?? 1);
        var usedCapacity = 0;

        foreach (var load in loads)
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

        return Math.Max(0, staff.MaxConcurrent - usedCapacity);
    }

    private static void UpsertLoad(
        List<PlannedAssignmentLoad> loads,
        BookingDetail detail,
        int staffId,
        int assignedQuantity,
        int assignmentId)
    {
        var existing = loads.FirstOrDefault(x =>
            x.BookingDetailId == detail.Id &&
            x.StaffId == staffId &&
            x.AssignmentId == assignmentId);

        if (existing == null)
        {
            loads.Add(new PlannedAssignmentLoad
            {
                AssignmentId = assignmentId,
                BookingDetailId = detail.Id,
                StaffId = staffId,
                AppointmentDate = detail.AppointmentDate,
                AppointmentTime = detail.AppointmentTime ?? string.Empty,
                Duration = detail.Service?.Duration ?? 1,
                AssignedQuantity = assignedQuantity,
            });
            return;
        }

        existing.AssignedQuantity += assignedQuantity;
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
        public int AssignmentId { get; set; }
        public int BookingDetailId { get; set; }
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

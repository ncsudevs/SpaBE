namespace SpaBookingSystem.Api.Dtos.Dashboard;

public class DashboardSummaryDto
{
    public int TotalServices { get; set; }
    public int ActiveServices { get; set; }
    public int TotalCustomers { get; set; }
    public int ActiveCustomers { get; set; }
    public int TotalStaff { get; set; }
    public int ActiveStaff { get; set; }
    public int PendingBookings { get; set; }
    public int ConfirmedBookings { get; set; }
    public int CompletedBookings { get; set; }
    public int CancelledBookings { get; set; }
    public int PendingPayments { get; set; }
    public int AwaitingTransferPayments { get; set; }
    public int PaidPayments { get; set; }
    public decimal PaidRevenue { get; set; }
    public List<DashboardRecentBookingDto> RecentBookings { get; set; } = [];
    public List<DashboardRecentPaymentDto> RecentPayments { get; set; } = [];
}

public class DashboardRecentBookingDto
{
    public int BookingId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateOnly AppointmentDate { get; set; }
    public string AppointmentTime { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
}

public class DashboardRecentPaymentDto
{
    public int PaymentId { get; set; }
    public string PaymentCode { get; set; } = string.Empty;
    public string BookingCode { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
}

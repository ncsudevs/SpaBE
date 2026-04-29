namespace SpaBookingSystem.Api.Dtos.Reports;

public class ReportSummaryDto
{
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public int TotalBookings { get; set; }
    public int CompletedBookings { get; set; }
    public int CancelledBookings { get; set; }
    public int PaidPayments { get; set; }
    public int RefundedPayments { get; set; }
    public decimal Revenue { get; set; }
    public decimal RefundedAmount { get; set; }
    public List<TopServiceReportDto> TopServices { get; set; } = [];
}

public class TopServiceReportDto
{
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public int TotalQuantity { get; set; }
    public decimal Revenue { get; set; }
}

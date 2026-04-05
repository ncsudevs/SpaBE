namespace SpaBookingSystem.Api.Dtos.Staff;

public class StaffCreateUpdateDto
{
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Skills { get; set; }
    public bool IsActive { get; set; } = true;
    public int MaxConcurrent { get; set; } = 1;
}

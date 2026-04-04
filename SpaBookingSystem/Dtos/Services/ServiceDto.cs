namespace SpaBookingSystem.Api.Dtos.Services;

public class ServiceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Duration { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public int SlotCapacity { get; set; }
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? ImageUrl { get; set; }
}

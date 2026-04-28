using System.ComponentModel.DataAnnotations;

namespace SpaBookingSystem.Api.Dtos;

public class BulkStatusUpdateDto
{
    [Required]
    public List<int> Ids { get; set; } = new();

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "ACTIVE";
}

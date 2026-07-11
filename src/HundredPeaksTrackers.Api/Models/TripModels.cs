using System.ComponentModel.DataAnnotations;

namespace HundredPeaksTrackers.Api.Models;

public class TripDto
{
    public string Id { get; set; } = default!;
    public string Date { get; set; } = default!;
    public List<int> PeakIds { get; set; } = new();
    public string? Note { get; set; }
    public long UpdatedAt { get; set; }
}

public class TripRequest
{
    [Required]
    public string Date { get; set; } = default!;

    [Required, MinLength(1)]
    public List<int> PeakIds { get; set; } = new();

    public string? Note { get; set; }
}

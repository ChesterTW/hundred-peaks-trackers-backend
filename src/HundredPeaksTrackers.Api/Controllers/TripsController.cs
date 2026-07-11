using Google.Cloud.Firestore;
using HundredPeaksTrackers.Api.Models;
using HundredPeaksTrackers.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HundredPeaksTrackers.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TripsController : ControllerBase
{
    private readonly FirestoreDb _db;
    private readonly ICurrentUserService _currentUser;

    public TripsController(FirestoreDb db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    private async Task<CollectionReference> GetTripsCollectionAsync()
    {
        var firestoreUid = await _currentUser.GetFirestoreUidAsync();
        return _db.Collection($"users/{firestoreUid}/trips");
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TripDto>>> GetTrips()
    {
        var trips = await GetTripsCollectionAsync();
        var snapshot = await trips.GetSnapshotAsync();
        return Ok(snapshot.Documents.Select(MapToDto));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TripDto>> GetTrip(string id)
    {
        var trips = await GetTripsCollectionAsync();
        var doc = await trips.Document(id).GetSnapshotAsync();
        if (!doc.Exists)
        {
            return NotFound();
        }

        return Ok(MapToDto(doc));
    }

    [HttpPost]
    public async Task<ActionResult<TripDto>> CreateTrip(TripRequest request)
    {
        var trips = await GetTripsCollectionAsync();
        var id = Guid.NewGuid().ToString();
        var updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await trips.Document(id).SetAsync(new Dictionary<string, object?>
        {
            ["date"] = request.Date,
            ["peakIds"] = request.PeakIds,
            ["note"] = request.Note,
            ["updatedAt"] = updatedAt,
        });

        var dto = new TripDto { Id = id, Date = request.Date, PeakIds = request.PeakIds, Note = request.Note, UpdatedAt = updatedAt };
        return CreatedAtAction(nameof(GetTrip), new { id }, dto);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TripDto>> UpdateTrip(string id, TripRequest request)
    {
        var trips = await GetTripsCollectionAsync();
        var docRef = trips.Document(id);
        var doc = await docRef.GetSnapshotAsync();
        if (!doc.Exists)
        {
            return NotFound();
        }

        var updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await docRef.SetAsync(new Dictionary<string, object?>
        {
            ["date"] = request.Date,
            ["peakIds"] = request.PeakIds,
            ["note"] = request.Note,
            ["updatedAt"] = updatedAt,
        }, SetOptions.Overwrite);

        return Ok(new TripDto { Id = id, Date = request.Date, PeakIds = request.PeakIds, Note = request.Note, UpdatedAt = updatedAt });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTrip(string id)
    {
        var trips = await GetTripsCollectionAsync();
        var docRef = trips.Document(id);
        var doc = await docRef.GetSnapshotAsync();
        if (!doc.Exists)
        {
            return NotFound();
        }

        await docRef.DeleteAsync();
        return NoContent();
    }

    private static TripDto MapToDto(DocumentSnapshot doc) => new()
    {
        Id = doc.Id,
        Date = doc.GetValue<string>("date"),
        PeakIds = doc.GetValue<List<int>>("peakIds"),
        Note = doc.ContainsField("note") ? doc.GetValue<string?>("note") : null,
        UpdatedAt = doc.GetValue<long>("updatedAt"),
    };
}

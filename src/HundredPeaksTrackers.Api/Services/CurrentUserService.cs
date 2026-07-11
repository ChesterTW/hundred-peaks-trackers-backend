using Google.Cloud.Firestore;
using HundredPeaksTrackers.Api.Models;
using Microsoft.IdentityModel.JsonWebTokens;

namespace HundredPeaksTrackers.Api.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly FirestoreDb _db;
    private string? _cachedFirestoreUid;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor, FirestoreDb db)
    {
        _httpContextAccessor = httpContextAccessor;
        _db = db;
    }

    public async Task<string> GetFirestoreUidAsync()
    {
        if (_cachedFirestoreUid is not null)
        {
            return _cachedFirestoreUid;
        }

        var googleSub = _httpContextAccessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? throw new InvalidOperationException("JWT 缺少 sub claim");

        var snapshot = await _db.Collection("users").Document(googleSub).GetSnapshotAsync();
        if (!snapshot.Exists)
        {
            throw new InvalidOperationException($"找不到使用者資料：users/{googleSub}");
        }

        _cachedFirestoreUid = snapshot.ConvertTo<UserRecord>().FirestoreUid;
        return _cachedFirestoreUid;
    }
}

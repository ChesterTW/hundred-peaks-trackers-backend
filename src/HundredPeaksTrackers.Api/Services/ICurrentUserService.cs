namespace HundredPeaksTrackers.Api.Services;

public interface ICurrentUserService
{
    Task<string> GetFirestoreUidAsync();
}

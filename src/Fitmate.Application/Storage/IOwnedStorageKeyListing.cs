namespace Fitmate.Application.Storage;

/// <summary>
/// Enumerates even unattached uploads issued to one account. Account erasure
/// must not depend on a media key having been saved in an entity row.
/// </summary>
public interface IOwnedStorageKeyListing
{
    Task<IReadOnlyList<string>> ListOwnedKeysAsync(Guid userId, CancellationToken ct = default);
}

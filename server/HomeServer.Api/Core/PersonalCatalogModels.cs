namespace HomeServer.Core;

public sealed record ContentPresentation(string? Title = null, string? Overview = null, int? MinimumAge = null, bool Blocked = false, string[]? Tags = null);
public sealed record MediaAccessInput(int? MinimumAge = null, bool Blocked = false);
public sealed record PersonalContent(bool Favorite = false, int? Rating = null);
public sealed record ContentCollection(string Id, string Name, bool Shared, bool Owned, int Count, string UpdatedAt);
public sealed record CollectionInput(string Name, bool Shared = false);
public sealed record CollectionItemInput(string ContentId);
public sealed record PinInput(string CurrentPassword, string? Pin);
public sealed record DeviceProfileInput(string[] UserIds);
public sealed record ProfileSwitchInput(string UserId, string Pin);
public sealed record DeviceProfile(string Id, string DisplayName, string Role);
public sealed record HouseholdDevice(string Id, string OwnerId, int OwnerVersion, Dictionary<string, int> Users, string ExpiresAt, int Failures, string? RetryAt);

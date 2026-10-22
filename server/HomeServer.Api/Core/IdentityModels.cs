namespace HomeServer.Core;

public sealed record Account(string Id, string DisplayName, string Role, bool Enabled, bool AllLibraries,
    string[] LibraryIds, bool CanDiscover, bool CanAcquire, bool AutoApprove, int MonthlyRequestLimit, string CreatedAt,
    int? MaximumAge = null, bool AllowUnrated = true, bool PinEnabled = false)
{
    public bool IsAdmin => Role == "admin";
    public bool CanRequest => Role is "admin" or "member";
}
public sealed record AccountSecret(Account Account, string PasswordHash, int Version, string? PinHash = null);
public sealed record AccountInput(string DisplayName, string Role = "member", bool Enabled = true, bool AllLibraries = true,
    string[]? LibraryIds = null, bool CanDiscover = true, bool CanAcquire = false, bool AutoApprove = false, int MonthlyRequestLimit = 20,
    int? MaximumAge = null, bool AllowUnrated = true);
public sealed record CreateAccount(AccountInput Account, string Password);
public sealed record PasswordChange(string CurrentPassword, string NewPassword);
public sealed record PasswordReset(string NewPassword);
public sealed record LoginSession(string Id, string UserId, string Device, string CreatedAt, string ExpiresAt, bool Current = false);
public sealed record Invitation(string Id, string Token, string ExpiresAt, string Role);
public sealed record AcceptInvitation(string Token, string DisplayName, string Password);
public sealed record NotificationItem(string Id, string Kind, string Title, string Message, string? ContentId, string CreatedAt, string? ReadAt);
public sealed record RequestDecision(string Status, string Note = "");
public sealed record RequestEntry(string Id, string ContentId, string RequestedBy, string RequesterName, string Status,
    int? Season, int? Episode, string MonitorMode, string QualityProfileId, string Note, string CreatedAt, string UpdatedAt, ProviderContent Metadata, int? ScopeEpisodeCount = null);
public sealed record RequestEvent(string Id, string RequestId, string ActorName, string Status, string Note, string CreatedAt);

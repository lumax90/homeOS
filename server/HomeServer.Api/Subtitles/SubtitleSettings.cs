using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace HomeServer.Subtitles;

public sealed class SubtitleSettings(MediaStore store, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Home.Subtitles.Connection.v1");
    private readonly IDataProtector _tickets = protection.CreateProtector("Home.Subtitles.Tickets.v1");
    public SubtitleSecrets Secrets
    {
        get
        {
            var stored = store.GetSubtitleConnectionSnapshot().Value;
            if (stored is null) return new("", null, "api.opensubtitles.com", null, null);
            try { return JsonSerializer.Deserialize<SubtitleSecrets>(_protector.Unprotect(stored))!; }
            catch (System.Security.Cryptography.CryptographicException) { throw new DomainException("Altyazı sağlayıcı anahtarı yeniden kaydedilmeli.", 409); }
        }
    }
    public void Save(SubtitleConnectionInput input)
    {
        var revision = ConnectionRevision;
        if (input.Clear) { Store(new("", null, "api.opensubtitles.com", null, null), revision); store.SetSubtitleQuota(null, null, revision + 1, true); return; }
        if (input.ApiKey is null) return;
        if (input.ApiKey.Length is < 8 or > 4096 || input.ApiKey.Any(char.IsControl)) throw new DomainException("Sağlayıcı API anahtarı geçersiz.");
        Store(new(input.ApiKey.Trim(), null, "api.opensubtitles.com", null, null), revision);
        store.SetSubtitleQuota(null, null, revision + 1, true);
    }
    public void Store(SubtitleSecrets secrets, int? expectedRevision = null)
    {
        if (!store.SetSubtitleConnection(_protector.Protect(JsonSerializer.Serialize(secrets)), expectedRevision)) throw new DomainException("Altyazı bağlantısı işlem sırasında değişti.", 409);
    }
    public (SubtitleSecrets Secrets, int Revision) Snapshot()
    {
        var snapshot = store.GetSubtitleConnectionSnapshot();
        try { return (snapshot.Value is null ? new("", null, "api.opensubtitles.com", null, null) : JsonSerializer.Deserialize<SubtitleSecrets>(_protector.Unprotect(snapshot.Value))!, snapshot.Revision); }
        catch (System.Security.Cryptography.CryptographicException) { throw new DomainException("Altyazı bağlantısı yeniden kaydedilmeli.", 409); }
    }
    public int ConnectionRevision => store.GetSubtitleConnectionSnapshot().Revision;
    public string ProtectTicket(string url) => _tickets.Protect(url);
    public string UnprotectTicket(string value)
    {
        try { return _tickets.Unprotect(value); }
        catch (System.Security.Cryptography.CryptographicException) { throw new DomainException("Altyazı indirme bileti okunamadı; aramayı yenile.", 409); }
    }
    public SubtitleConnection Status
    {
        get
        {
            var secrets = Secrets;
            return new(secrets.ApiKey.Length > 0, secrets.Token is not null && DateTimeOffset.TryParse(secrets.ExpiresAt, out var expiry) && expiry > DateTimeOffset.UtcNow,
                secrets.Username, secrets.ExpiresAt, int.TryParse(store.GetSetting("subtitles.remaining"), out var remaining) ? remaining : null, store.GetSetting("subtitles.reset"), store.GetSetting("subtitles.error"));
        }
    }
    public void Quota(int? remaining, string? reset, int? expectedRevision = null) => store.SetSubtitleQuota(remaining, reset, expectedRevision ?? ConnectionRevision);
    public SubtitleLanguageProfile Profile => NormalizeProfile(store.GetSetting("subtitles.profile") is { } json ? JsonSerializer.Deserialize<SubtitleLanguageProfile>(json)! : new(["tr"]));
    public int Revision => int.TryParse(store.GetSetting("subtitles.profile.revision"), out var value) ? value : 0;
    public void SaveProfile(SubtitleLanguageProfile value)
    {
        ValidateProfile(value);
        store.SetSubtitleProfile(NormalizeProfile(value));
    }
    public static void ValidateProfile(SubtitleLanguageProfile value)
    {
        if (value is null) throw new DomainException("Altyazı profili gerekli.");
        if (value.Languages is null || value.Languages.Length is < 1 or > 8 || value.Languages.Any(language => string.IsNullOrEmpty(language) || language.Length is < 2 or > 3 || language.Any(c => c is < 'a' or > 'z'))
            || value.HearingImpaired is not ("include" or "exclude" or "only") || value.Forced is not ("include" or "exclude" or "only")
            || value.MinimumScore is < 0 or > 150 || value.MaximumDailyDownloads is < 1 or > 100 || value.CutoffScore is < 0 or > 150 || value.CutoffScore < value.MinimumScore
            || value.MinimumUpgradeDelta is < 1 or > 50 || value.SearchIntervalHours is < 1 or > 168 || value.ExcludedAudioLanguages?.Length > 16
            || value.ExcludedAudioLanguages?.Any(language => string.IsNullOrEmpty(language) || language.Length is < 2 or > 3 || language.Any(c => c is < 'a' or > 'z')) == true) throw new DomainException("Altyazı dil profili geçersiz.");
    }
    public static SubtitleLanguageProfile NormalizeProfile(SubtitleLanguageProfile value) => value with { Languages = value.Languages.Select(Sources.ReleaseEvaluator.Language).Distinct().ToArray(), CutoffScore = Math.Max(value.MinimumScore, value.CutoffScore), ExcludedAudioLanguages = (value.ExcludedAudioLanguages ?? []).Select(Sources.ReleaseEvaluator.Language).Distinct().ToArray() };
}

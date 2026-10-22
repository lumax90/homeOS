using System.Globalization;
using HomeServer.Core;

namespace HomeServer.Music;

public static class MusicMonitorPolicy
{
    public static readonly string[] PrimaryTypes = ["Album", "Single", "EP", "Broadcast", "Other"];
    public static readonly string[] SecondaryTypes = ["Audio drama", "Audiobook", "Compilation", "Demo", "DJ-mix", "Field recording", "Interview", "Live", "Mixtape/Street", "Remix", "Soundtrack", "Spokenword"];
    public static readonly string[] Statuses = ["Official", "Promotion", "Bootleg", "Pseudo-Release"];
    public static MusicMonitorSettings Validate(MusicMonitorSettings input)
    {
        if (input is null || input.Mode is not ("all" or "future") || input.Cutoff is not ("lossy" or "lossless" or "hires")
            || input.RefreshHours is < 6 or > 168 || input.SearchIntervalMinutes is < 30 or > 10080 || input.DelayMinutes is < 0 or > 1440
            || input.MaximumDailyGrabs is < 1 or > 20 || input.CutoffBitrateKbps is < 64 or > 1000 || input.AutoGrab && !input.Search)
            throw new DomainException("Müzik takip tercihleri geçersiz.");
        var primary = input.PrimaryTypes ?? ["Album", "EP", "Single"];
        var secondary = input.ExcludedSecondaryTypes ?? ["Live", "Compilation", "Remix", "DJ-mix"];
        var statuses = input.ReleaseStatuses ?? ["Official"];
        if (primary.Length is 0 or > 5 || primary.Any(type => !PrimaryTypes.Contains(type)) || secondary.Length > SecondaryTypes.Length || secondary.Any(type => !SecondaryTypes.Contains(type))
            || statuses.Length is 0 or > 4 || statuses.Any(status => !Statuses.Contains(status)) || input.Countries?.Length > 30 || input.Countries?.Any(value => value is null || value.Length != 2 || !value.All(char.IsAsciiLetter)) == true
            || input.Formats?.Length > 10 || input.Formats?.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 50) == true)
            throw new DomainException("Müzik metadata profili geçersiz.");
        MusicReleaseEvaluator.Validate(input.Quality ?? new());
        if (input.Search && input.Quality?.CompleteAlbum == false) throw new DomainException("Otomasyon tam albüm doğrulaması gerektirir.");
        return input with { PrimaryTypes = primary.Distinct().ToArray(), ExcludedSecondaryTypes = secondary.Distinct().ToArray(), ReleaseStatuses = statuses.Distinct().ToArray(),
            Countries = input.Countries?.Select(value => value.ToUpperInvariant()).Distinct().ToArray() ?? [], Formats = input.Formats?.Distinct().ToArray() ?? [], Quality = input.Quality ?? new() };
    }
    public static bool Wanted(MusicMonitor monitor, MusicBrainzAlbum album) => monitor.Kind == "album" ||
        (monitor.Settings.PrimaryTypes!.Contains(album.Type) && !(album.SecondaryTypes ?? []).Intersect(monitor.Settings.ExcludedSecondaryTypes!, StringComparer.OrdinalIgnoreCase).Any()
        && (monitor.Settings.Mode == "all" || EarliestDate(album.Date) is { } date && date >= DateOnly.FromDateTime(DateTimeOffset.Parse(monitor.CreatedAt).UtcDateTime)));
    public static bool Released(string? date, DateTimeOffset now) => LatestDate(date) is { } latest && latest <= DateOnly.FromDateTime(now.UtcDateTime);
    public static bool AcceptRelease(MusicReleaseSummary release, MusicMonitorSettings settings, DateTimeOffset now) =>
        settings.ReleaseStatuses!.Contains(release.Status, StringComparer.OrdinalIgnoreCase) && Released(release.Date, now) && release.TrackCount is > 0 and <= 2000
        && (settings.Countries!.Length == 0 || settings.Countries.Contains(release.Country, StringComparer.OrdinalIgnoreCase))
        && (settings.Formats!.Length == 0 || release.Formats.Length > 0 && release.Formats.All(format => settings.Formats.Contains(format, StringComparer.OrdinalIgnoreCase)));
    public static DateOnly? EarliestDate(string? date)
    {
        var value = date?.Length switch { 4 => date + "-01-01", 7 => date + "-01", 10 => date, _ => null };
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ? result : null;
    }
    private static DateOnly? LatestDate(string? date)
    {
        if (EarliestDate(date) is not { } first) return null;
        return date!.Length switch { 4 => new DateOnly(first.Year, 12, 31), 7 => new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month)), _ => first };
    }
}

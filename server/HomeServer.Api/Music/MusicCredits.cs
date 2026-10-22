using HomeServer.Core;

namespace HomeServer.Music;

public static class MusicCredits
{
    public static MusicArtistCredit[] Normalize(MusicArtistCredit[]? credits, string fallback, string? mbid = null)
    {
        if (credits is null or { Length: 0 }) return string.IsNullOrWhiteSpace(fallback) ? [] : [new(ValidId(mbid), fallback.Trim(), fallback.Trim())];
        if (credits.Length > 64 || credits.Any(credit => string.IsNullOrWhiteSpace(credit.Name) || string.IsNullOrWhiteSpace(credit.CreditedName)
            || credit.Name.Length > 500 || credit.CreditedName.Length > 500 || credit.JoinPhrase is null || credit.JoinPhrase.Length > 100 || credit.SortName?.Length > 500))
            throw new DomainException("Sanatçı katkı listesi geçersiz veya 64 sanatçı sınırını aşıyor.", 422);
        return credits.Select(credit => credit with { Mbid = ValidId(credit.Mbid), Name = credit.Name.Trim(), CreditedName = credit.CreditedName.Trim(), ArtistId = null }).ToArray();
    }
    public static string Display(IEnumerable<MusicArtistCredit> credits) => string.Concat(credits.Select(credit => credit.CreditedName + credit.JoinPhrase));
    public static string? ValidId(string? id) => Guid.TryParseExact(id, "D", out var value) ? value.ToString() : null;
    public static bool Equivalent(MusicArtistCredit[]? actual, MusicArtistCredit[]? expected)
    {
        if (actual is not { Length: > 0 } || expected is not { Length: > 0 } || actual.Length != expected.Length) return false;
        return actual.Zip(expected).All(pair => pair.First.Mbid is { } left && pair.Second.Mbid is { } right ? left == right
            : MusicReleaseEvaluator.Normalize(pair.First.CreditedName) == MusicReleaseEvaluator.Normalize(pair.Second.CreditedName)
                || MusicReleaseEvaluator.Normalize(pair.First.Name) == MusicReleaseEvaluator.Normalize(pair.Second.Name));
    }
    public static MusicArtistCredit[] FromValues(string[] names, string? ids)
    {
        names = names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToArray();
        if (names.Length > 64 || names.Any(name => name.Length > 500)) throw new DomainException("Dosyanın sanatçı etiketi sınırı aşıyor.", 422);
        var keys = (ids ?? "").Split([';', '/', ' ', '\0'], StringSplitOptions.RemoveEmptyEntries).Select(ValidId).ToArray();
        return names.Select((name, index) => new MusicArtistCredit(keys.Length == names.Length ? keys[index] : null, name, name, index + 1 < names.Length ? "; " : "")).ToArray();
    }
}

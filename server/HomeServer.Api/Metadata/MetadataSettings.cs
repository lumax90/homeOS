using System.Security.Cryptography;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace HomeServer.Metadata;

public sealed class MetadataSettings(MediaStore store, IConfiguration configuration, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Home.Metadata.Token.v1");

    public string? Token
    {
        get
        {
            var value = store.GetSetting("metadata.tmdb.token");
            if (value is null) return configuration["Home:Tmdb:Token"];
            try { return _protector.Unprotect(value); }
            catch (CryptographicException) { throw new DomainException("TMDB bağlantı bilgisi okunamadı. Token'ı yeniden kaydet.", 409); }
        }
    }

    public string Language => store.GetSetting("metadata.language") ?? "tr-TR";
    public MetadataStatus Status => new(!string.IsNullOrWhiteSpace(Token), Language, ["tvmaze", "tmdb"]);

    public MetadataStatus Save(MetadataPreferences preferences)
    {
        if (preferences.Language is not ("tr-TR" or "en-US")) throw new DomainException("Desteklenen bir metadata dili seç.");
        if (preferences.ClearToken) store.SetSetting("metadata.tmdb.token", _protector.Protect(""));
        else if (!string.IsNullOrWhiteSpace(preferences.TmdbToken))
        {
            var token = preferences.TmdbToken.Trim();
            if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..].Trim();
            if (token.Length is < 20 or > 4096 || token.Any(char.IsWhiteSpace)) throw new DomainException("Geçerli bir TMDB API Read Access Token gir.");
            store.SetSetting("metadata.tmdb.token", _protector.Protect(token));
        }
        store.SetSetting("metadata.language", preferences.Language);
        return Status;
    }
}

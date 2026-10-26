using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Metadata;

namespace HomeServer.Infrastructure;

public sealed record LocalSeriesIdentity(string Key,string Title,int Season,int Episode);
public static partial class CatalogSeriesIdentity
{
    // A local view identity, never a fabricated provider match or media record.
    public static LocalSeriesIdentity? Local(string path,string? json)
    {
        var hint=FilenameHints.Parse(path.Replace('\\','/'));
        string? title=null;int? season=null,episode=null,year=null;
        if(json is not null)
        {
            try
            {
                using var document=JsonDocument.Parse(json);var root=document.RootElement;if(root.ValueKind!=JsonValueKind.Object)return null;
                title=root.TryGetProperty("showTitle",out var show)&&show.ValueKind==JsonValueKind.String?show.GetString():null;
                int? Number(string key)=>root.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var number)?number:null;
                season=Number("season");episode=Number("episode");year=Number("year");
            }
            catch(JsonException){return null;}
        }
        if(string.IsNullOrWhiteSpace(title))
        {
            if(hint.Ambiguous||hint.SeasonNumber is null||hint.EpisodeNumber is null||EpisodeOnly().IsMatch(hint.Query))return null;
            title=hint.Query;
        }
        season??=hint.SeasonNumber;episode??=hint.EpisodeNumber;year??=hint.Year;
        if(title.Length<2||season is not >=0 or >1000||episode is not >=1 or >10000)return null;
        var key=MediaPaths.SearchKey(title.Trim())+":"+year;
        return new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key))),title.Trim(),season.Value,episode.Value);
    }
    [GeneratedRegex(@"^(?:s\d{1,3}(?:e\d{1,4})?|\d{1,3}x\d{1,4})(?:\b|$)",RegexOptions.IgnoreCase,100)]
    private static partial Regex EpisodeOnly();
}

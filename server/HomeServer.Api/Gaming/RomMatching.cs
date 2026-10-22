using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HomeServer.Gaming;

/// <summary>Conservative title/platform matching and regional artwork selection, independent of ROM/save identity.</summary>
public static class RomMatching
{
    private static readonly Regex Tags=new(@"\(([^)]*)\)|\[([^\]]*)\]",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    private static bool DistributionTag(string value)=>Regex.IsMatch(value,@"^(?:(?:USA|Europe|World|Japan|UK|Australia|Canada|Germany|France|Spain|Italy|Korea|China|Taiwan|Netherlands|Brazil|Sweden|Russia)(?:\s*,\s*(?:USA|Europe|World|Japan|UK|Australia|Canada|Germany|France|Spain|Italy|Korea|China|Taiwan|Netherlands|Brazil|Sweden|Russia))*|(?:En|Ja|Fr|De|Es|It|Nl|Pt|Ko|Zh|Sv|No|Da|Fi|Tr)(?:\s*,\s*(?:En|Ja|Fr|De|Es|It|Nl|Pt|Ko|Zh|Sv|No|Da|Fi|Tr))*|(?:Disc|Disk|Track)\s+\d+(?:\s+of\s+\d+)?|Rev\s+[\w.]+|v?\d+(?:\.\d+)+)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    public static string SearchTitle(string title)=>Tags.Replace(title,m=>DistributionTag(m.Groups[1].Success?m.Groups[1].Value:m.Groups[2].Value)?"":m.Value).Trim();
    public static string TitleKey(string title)=>new(SearchTitle(title).Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark&&char.IsLetterOrDigit(c)).Select(char.ToLowerInvariant).ToArray());
    public static string? Region(string text)
    {
        foreach(var (id,pattern) in new[]{("us",@"\b(USA|US|United States|North America)\b"),("eu",@"\b(Europe|EU|PAL|UK|Germany|France|DE|FR)\b"),("jp",@"\b(Japan|JP|JPN)\b"),("world",@"\b(World|WOR)\b")})if(Regex.IsMatch(text,pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))return id;return null;
    }
    private static bool Special(RomMetadataCandidate value)=>new[]{"demo","beta","proto","hack","preproduction","sample"}.Any(tag=>(value.Edition??"").Contains(tag,StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(value.Name,@"[\[(][^\])]*\b"+Regex.Escape(tag),RegexOptions.IgnoreCase|RegexOptions.CultureInvariant));
    public static RomMetadataCandidate? Automatic(IEnumerable<RomMetadataCandidate> choices,string title,bool allowName)
    {
        var list=choices.ToArray();var hash=list.Where(c=>c.HashMatch).ToArray();var candidates=hash.Length>0?hash:allowName?list.Where(c=>TitleKey(c.Name)==TitleKey(title)&&(!Special(c)||Special(new(0,title,"","",null,null,[],[])))).ToArray():[];
        if(candidates.Length==0||candidates.Select(c=>TitleKey(c.Name)).Distinct().Count()!=1)return null;
        if(candidates.Any(Special)&&candidates.Any(c=>!Special(c))&&hash.Length==0)return null;
        // Duplicates in separate DATs are not separate user decisions when the same payload/title matches.
        var region=Region(title);return candidates.OrderByDescending(c=>region is not null&&c.Regions.Any(r=>Region(r)==region)).ThenByDescending(c=>(c.Publisher.Length>0?4:0)+(c.Year is not null?2:0)+(c.Description is not null?1:0)).ThenBy(c=>c.Id).First();
    }
    public static RomCover? Cover(IEnumerable<RomCover> covers,string title,string fileName,string preferredRegion,IEnumerable<string>? matchedRegions=null)
    {
        var exact=covers.Where(c=>TitleKey(c.Title)==TitleKey(title)).ToArray();if(exact.Length==0)return null;
        var region=preferredRegion=="auto"?(Region(fileName)??matchedRegions?.Select(Region).FirstOrDefault(r=>r is not null)):preferredRegion;
        return exact.OrderByDescending(c=>region is not null&&Region(c.Title)==region).ThenBy(c=>Region(c.Title) switch{"world"=>0,"us"=>1,"eu"=>2,"jp"=>3,_=>4}).ThenBy(c=>c.Name,StringComparer.Ordinal).First();
    }
}

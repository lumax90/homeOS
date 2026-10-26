using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;

namespace HomeServer.Downloads;

public static partial class DownloadImportNaming
{
    public static string DirectoryName(string title,string downloadId,int? year=null,int? season=null)
    {
        DownloadFiles.ValidId(downloadId);var builder=new StringBuilder();var bytes=0;
        foreach(var rune in title.EnumerateRunes())
        {
            var category=Rune.GetUnicodeCategory(rune);
            var value=category is UnicodeCategory.Control or UnicodeCategory.Format||rune.IsAscii&&"<>:\"/\\|?*".Contains((char)rune.Value)?" ":rune.ToString();
            var size=Encoding.UTF8.GetByteCount(value);if(bytes+size>150)break;bytes+=size;builder.Append(value);
        }
        var name=Spaces().Replace(builder.ToString()," ").Trim(' ','.');if(name.Length==0)name="Media";
        if(year is >=1000 and <=9999&&!name.EndsWith($"({year})",StringComparison.Ordinal))name+=$" ({year})";
        if(season is >=0 and <=1000)name+=$" S{season:D2}";
        // This short stable suffix distinguishes editions/imports, never media identity.
        return DownloadFiles.ValidateRelative(name+" ["+downloadId[..12]+"]");
    }
    public static string Title(DownloadRecord download,MediaStore store,out int? year,out int? season)
    {
        var acquisition=store.GetAcquisition(download.Id);var content=acquisition is null?null:store.GetContent(acquisition.ContentId)?.Metadata;
        var hint=FilenameHints.AnalyzeName(download.Name);season=content?.Kind=="show"?acquisition?.Season:hint.SeasonNumber;
        year=content?.ReleaseDate is{Length:>=4}date&&int.TryParse(date[..4],out var value)?value:hint.Year;
        return content?.Title??(hint.Query.Length>=2?hint.Query:download.Name);
    }
    [GeneratedRegex(@"\s+",RegexOptions.NonBacktracking)]private static partial Regex Spaces();
}

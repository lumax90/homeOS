using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;

namespace HomeServer.Music;

public sealed record MusicExperienceOptions(int Revision=0,bool OnlineMetadata=false,bool OnlineCovers=false,bool RadioDirectory=true,bool OnlineScenes=false,string Scene="fireplace",bool ShowNowPlaying=true,string RadioCountry="",int MetadataGeneration=0,string SceneVideo="");
public sealed class MusicExperiencePreferences(HomePaths paths,MediaStore store,EventHub events)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);private readonly object _gate=new();
    public MusicExperienceOptions Read(string user)
    {
        if(!Gaming.NativeSystems.SafeSegment(user))throw new DomainException("Müzik ayarı kapsamı geçersiz.");var relative=$"music/preferences/{user}.json";if(!File.Exists(Path.Combine(paths.State,relative)))return new();var file=MediaPaths.ResolveFile(paths.State,relative);if(new FileInfo(file).Length>8192)throw new DomainException("Müzik tercihleri okunamadı.",409);
        try{var value=JsonSerializer.Deserialize<MusicExperienceOptions>(File.ReadAllText(file),Json)??new();Validate(value);return value;}catch(JsonException){throw new DomainException("Müzik tercihleri okunamadı.",409);}
    }
    public static void Validate(MusicExperienceOptions value)
    {if(value.Revision is <0 or int.MaxValue||value.MetadataGeneration<0||value.Scene is not("vinyl" or "fireplace" or "rain" or "aquarium" or "ocean" or "gallery")||value.RadioCountry is null||value.RadioCountry.Length is not(0 or 2)||value.RadioCountry.Any(c=>!char.IsAsciiLetter(c))||value.SceneVideo is null||value.SceneVideo.Length>80||value.SceneVideo.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='-'))throw new DomainException("Müzik tercihi geçersiz.",400);}
    public MusicExperienceOptions Save(Account actor,string login,MusicExperienceOptions input,bool rescan=false)
    {lock(_gate){var currentActor=store.ValidateLoginSession(login,actor.Id)??throw new DomainException("Oturum gerekli.",401);Validate(input);var current=Read(actor.Id);if(current.Revision!=input.Revision||rescan&&current.MetadataGeneration==int.MaxValue)throw new DomainException("Müzik tercihleri değişti. Yeniden yükle.",409);if(!MusicAccess.Online(currentActor)&&(input.OnlineMetadata||input.OnlineCovers))throw new DomainException("Bu profil çevrimiçi müzik bilgilerine erişemiyor.",403);var value=input with{Revision=input.Revision+1,RadioCountry=input.RadioCountry.ToUpperInvariant(),MetadataGeneration=current.MetadataGeneration+(rescan?1:0)};var root=AppFiles.DirectoryIn(paths.State,"music","preferences");PackageTrust.AtomicJson(Path.Combine(root,actor.Id+".json"),value);events.Publish("music.preferences.changed");return value;}}
    public MusicExperienceOptions Refresh(Account actor,string login,int revision)
    {lock(_gate)return Save(actor,login,Read(actor.Id) with{Revision=revision},rescan:true);}
}

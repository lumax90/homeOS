using HomeServer.Core;
using HomeServer.LiveTv;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private static LiveProgramme? VisibleProgramme(LiveProgramme? value,Account actor)=>value is not null&&(actor.IsAdmin||actor.MaximumAge is null||value.MinimumAge is{} age&&age<=actor.MaximumAge||value.MinimumAge is null&&actor.AllowUnrated)?value:null;
    public static LiveChannel PublicLiveChannel(LiveChannel channel)=>channel with{Logo=string.IsNullOrWhiteSpace(channel.Logo)?null:$"/api/live/channels/{channel.Id}/logo"};
    public LiveLibraryPage BrowseLiveChannels(Account actor,int offset=0,string? query=null,string? group=null,string? source=null,string view="all")
    {
        if(offset is <0 or >1000000||query?.Length>120||group?.Length>160||source?.Length>80||view is not("all" or "favorites" or "recent"))throw new DomainException("TV arama alanları geçersiz.");
        if(!actor.Enabled)return new([],0,offset,60,[],0,0,0);
        using var db=Open();var now=Now();var key=MediaPaths.SearchKey(query?.Trim()??"");
        // The same current-program age rule is applied before counts, facets and pagination.
        var from="""
            FROM live_channels c JOIN live_sources s ON s.id=c.source_id
            LEFT JOIN live_channel_preferences pref ON pref.channel_id=c.id AND pref.user_id=$viewer
            LEFT JOIN live_programmes cur ON cur.id=(SELECT p.id FROM live_programmes p WHERE p.channel_id=c.id AND p.starts_at<=$now AND p.ends_at>$now ORDER BY p.starts_at LIMIT 1)
            """;
        var age="CASE WHEN c.minimum_age IS NOT NULL AND cur.minimum_age IS NOT NULL THEN MAX(c.minimum_age,cur.minimum_age) ELSE COALESCE(c.minimum_age,cur.minimum_age) END";
        var visible=$" WHERE ($admin=1 OR $maxage=-1 OR ({age})<=$maxage OR ({age}) IS NULL AND $unrated=1) AND (s.enabled=1 AND c.enabled=1 AND c.available=1 OR $view='favorites' AND pref.favorite=1)";
        var filter=" AND ($source='' OR c.source_id=$source) AND ($group='' OR c.group_name=$group) AND ($query='' OR c.search_text LIKE $pattern ESCAPE '~') AND ($view<>'favorites' OR pref.favorite=1) AND ($view<>'recent' OR pref.last_watched_at IS NOT NULL)";
        (string,object?)[] args=[("$now",now),("$offset",offset),("$source",source??""),("$group",group??""),("$query",key),("$pattern","%"+key.Replace("~","~~").Replace("%","~%").Replace("_","~_")+"%"),("$view",view),..ViewerArgs(actor)];
        var total=Convert.ToInt32(Scalar(db,"SELECT COUNT(*) "+from+visible+filter,args));
        var groups=Query(db,"SELECT c.group_name,COUNT(*) "+from+visible+" GROUP BY c.group_name ORDER BY COUNT(*) DESC,c.group_name LIMIT 300",r=>new LiveChannelGroup(r.GetString(0),r.GetInt32(1)),args).ToArray();
        var summary=Query(db,"SELECT COUNT(*),COALESCE(SUM(pref.favorite),0),COUNT(DISTINCT c.source_id) "+from+visible,r=>(Channels:r.GetInt32(0),Favorites:r.GetInt32(1),Sources:r.GetInt32(2)),args).Single();
        var select="SELECT c.id,c.source_id,c.name,c.group_name,COALESCE(c.epg_override,c.epg_id),CASE WHEN c.protected_logo IS NOT NULL THEN 'managed' ELSE c.logo END,c.radio,c.enabled,c.available,c.minimum_age,c.ordinal,s.name,s.public_preset,COALESCE(pref.favorite,0),pref.last_watched_at,cur.id,s.enabled ";
        var order=view=="recent"?"pref.last_watched_at DESC,c.ordinal,c.name,c.id":"COALESCE(pref.favorite,0) DESC,c.ordinal,c.name,c.id";
        var rows=Query(db,select+from+visible+filter+" ORDER BY "+order+" LIMIT 60 OFFSET $offset",r=>(Channel:ReadLiveChannel(r),Source:r.GetString(11),Preset:Text(r,12),Favorite:r.GetBoolean(13),Watched:Text(r,14),Current:Text(r,15),SourceEnabled:r.GetBoolean(16)),args);
        LiveProgramme? SafeProgramme(LiveProgramme? value)=>value is not null&&(actor.IsAdmin||actor.MaximumAge is null||value.MinimumAge is{} age&&age<=actor.MaximumAge||value.MinimumAge is null&&actor.AllowUnrated)?value:null;
        var items=rows.Select(row=>new LiveChannelCard(PublicLiveChannel(row.Channel),row.Source,row.Preset,row.Favorite,row.Watched,SafeProgramme(row.Current is null?null:GetLiveProgramme(row.Current)),
            SafeProgramme(Query(db,ProgrammeSelect+" WHERE channel_id=$id AND starts_at>$now ORDER BY starts_at LIMIT 1",ReadProgramme,("$id",row.Channel.Id),("$now",now)).FirstOrDefault()),row.SourceEnabled&&row.Channel.Enabled&&row.Channel.Available)).ToArray();
        return new(items,total,offset,60,groups,summary.Channels,summary.Favorites,summary.Sources);
    }
    public void SetLiveFavorite(string id,Account actor,bool favorite)
    {
        lock(_writeGate)
        {
            var current=GetAccount(actor.Id)??throw new DomainException("Oturum gerekli.",401);var channel=GetLiveChannel(id);if(!CanWatchChannel(channel,current))throw new DomainException("Kanal profil erişimi dışında.",403);
            using var db=Open();if(favorite&&Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM live_channel_preferences WHERE user_id=$user AND favorite=1",("$user",actor.Id)))>=5000)throw new DomainException("TV favori sınırına ulaşıldı.",409);
            Execute(db,"INSERT INTO live_channel_preferences(user_id,channel_id,favorite) VALUES($user,$id,$favorite) ON CONFLICT(user_id,channel_id) DO UPDATE SET favorite=$favorite",("$user",actor.Id),("$id",id),("$favorite",favorite));
        }
    }
    public void MarkLiveWatched(string id,PlaybackActor actor)
    {lock(_writeGate){var session=RequireLiveSession(id,actor);using var db=Open();Execute(db,"INSERT INTO live_channel_preferences(user_id,channel_id,last_watched_at) VALUES($user,$channel,$now) ON CONFLICT(user_id,channel_id) DO UPDATE SET last_watched_at=$now",("$user",actor.UserId),("$channel",session.ChannelId),("$now",Now()));}}
    public LiveChannelCard LiveChannelDetails(string id,Account actor)
    {
        var channel=GetLiveChannel(id);if(!CanWatchChannel(channel,actor))throw new DomainException("Kanal profil erişimi dışında.",403);var source=GetLiveSource(channel.SourceId);using var db=Open();var pref=Query(db,"SELECT favorite,last_watched_at FROM live_channel_preferences WHERE user_id=$user AND channel_id=$id",r=>(Favorite:r.GetBoolean(0),Watched:Text(r,1)),("$user",actor.Id),("$id",id)).FirstOrDefault();var time=DateTimeOffset.UtcNow;
        var current=LiveGuide(id,time,time.AddSeconds(1)).FirstOrDefault();var next=Query(db,ProgrammeSelect+" WHERE channel_id=$id AND starts_at>$now ORDER BY starts_at LIMIT 1",ReadProgramme,("$id",id),("$now",time.ToString("O"))).FirstOrDefault();
        return new(PublicLiveChannel(channel),source.Name,source.PresetId,pref.Favorite,pref.Watched,VisibleProgramme(current,actor),VisibleProgramme(next,actor),source.Enabled&&channel.Enabled&&channel.Available);
    }
    public object LiveNeighbours(string id,Account actor)
    {
        var channel=RequireLiveChannel(id,actor);using var db=Open();var now=Now();var age="CASE WHEN c.minimum_age IS NOT NULL AND cur.minimum_age IS NOT NULL THEN MAX(c.minimum_age,cur.minimum_age) ELSE COALESCE(c.minimum_age,cur.minimum_age) END";
        var rows=Query(db,$"SELECT c.id FROM live_channels c LEFT JOIN live_programmes cur ON cur.id=(SELECT p.id FROM live_programmes p WHERE p.channel_id=c.id AND p.starts_at<=$now AND p.ends_at>$now ORDER BY p.starts_at LIMIT 1) WHERE c.source_id=$source AND c.enabled=1 AND c.available=1 AND ($admin=1 OR $maxage=-1 OR ({age})<=$maxage OR ({age}) IS NULL AND $unrated=1) ORDER BY c.ordinal,c.name,c.id LIMIT 10000",r=>r.GetString(0),[("$source",channel.SourceId),("$now",now),..ViewerArgs(actor)]).ToArray();
        var index=Array.IndexOf(rows,id);return new{previous=index>0?PublicLiveChannel(GetLiveChannel(rows[index-1])):null,next=index>=0&&index+1<rows.Length?PublicLiveChannel(GetLiveChannel(rows[index+1])):null};
    }
}

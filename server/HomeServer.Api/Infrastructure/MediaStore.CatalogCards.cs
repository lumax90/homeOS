using HomeServer.Core;
using Microsoft.Data.Sqlite;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    private const string CatalogKeySql="""
        CASE WHEN m.kind='video' AND c.kind='show' THEN 'show:'||c.id
             WHEN m.kind='video' AND c.kind='movie' THEN 'movie:'||c.id
             WHEN m.kind='video' AND c.id IS NULL AND home_series_key(m.relative_path,lm.metadata_json) IS NOT NULL
               THEN 'local:'||m.library_id||':'||home_series_key(m.relative_path,lm.metadata_json)
             ELSE 'file:'||m.id END
        """;
    private static void RegisterCatalogFunctions(SqliteConnection db)
    {
        var cache=new Dictionary<(string,string?),LocalSeriesIdentity?>();
        LocalSeriesIdentity? Identity(string path,string? json)
        {
            if(cache.TryGetValue((path,json),out var result))return result;
            if(cache.Count>=512)cache.Clear();return cache[(path,json)]=CatalogSeriesIdentity.Local(path,json);
        }
        db.CreateFunction("home_series_key",(string path,string? json)=>Identity(path,json)?.Key,isDeterministic:true);
        db.CreateFunction("home_series_title",(string path,string? json)=>Identity(path,json)?.Title,isDeterministic:true);
        db.CreateFunction("home_series_season",(string path,string? json)=>Identity(path,json)?.Season,isDeterministic:true);
        db.CreateFunction("home_series_episode",(string path,string? json)=>Identity(path,json)?.Episode,isDeterministic:true);
    }
    private static string CatalogCandidates(string filter)=>$"""
        WITH candidates AS (
          SELECT m.id,m.available,m.added_at,{CatalogKeySql} AS group_key,
            CASE WHEN c.kind IN ('movie','show') THEN c.kind WHEN m.kind='video' AND c.id IS NULL AND home_series_key(m.relative_path,lm.metadata_json) IS NOT NULL THEN 'show' ELSE m.kind END AS group_kind,
            CASE WHEN c.kind IN ('movie','show') THEN COALESCE(cp.title,c.title) ELSE COALESCE(home_series_title(m.relative_path,lm.metadata_json),m.title) END AS group_title,
            CASE WHEN c.id IS NULL THEN home_series_season(m.relative_path,lm.metadata_json) ELSE e.season_number END AS season,
            CASE WHEN c.id IS NULL THEN home_series_episode(m.relative_path,lm.metadata_json) ELSE e.episode_number END AS episode,
            CASE WHEN c.id IS NOT NULL THEN mm.episode_id ELSE printf('%d:%d',home_series_season(m.relative_path,lm.metadata_json),home_series_episode(m.relative_path,lm.metadata_json)) END AS episode_key
          {MediaSelect[MediaSelect.IndexOf("FROM media m JOIN libraries l",StringComparison.Ordinal)..]}
          {filter}
        )
        """;
    private static string CatalogSummarySql=>"""
        ,group_summary AS (
          SELECT group_key,MIN(group_kind) AS group_kind,MIN(group_title) AS group_title,
            COUNT(*) AS file_count,COUNT(DISTINCT episode_key) AS episode_count,COUNT(DISTINCT season) AS season_count,MAX(added_at) AS added_at
          FROM candidates GROUP BY group_key
        ), ranked AS (
          SELECT id,group_key,ROW_NUMBER() OVER(PARTITION BY group_key ORDER BY available DESC,added_at DESC,id) AS card_rank FROM candidates
        )
        """;
    private static string CatalogCardSelect=>MediaSelect.Replace("FROM media m JOIN libraries l",
        ",g.group_key,g.group_kind,g.group_title,g.file_count,g.episode_count,g.season_count,g.added_at FROM media m JOIN libraries l",StringComparison.Ordinal);
    private static MediaItem ReadCatalogCard(SqliteDataReader row)=>ReadMedia(row) with
    {CatalogGroup=new(row.GetString(44),row.GetString(45),row.GetString(46),row.GetInt32(47),row.GetInt32(48),row.GetInt32(49),row.GetString(50))};

    public CatalogPage GetCatalogCards(string? search,string? kind,string? libraryId,int offset,int limit,Account viewer)
    {
        offset=Math.Max(0,offset);limit=Math.Clamp(limit,1,120);
        var filter=CatalogFilter;var args=CatalogArgs(search,kind,libraryId,offset,limit,viewer);
        using var db=Open();RegisterCatalogFunctions(db);var candidates=CatalogCandidates(filter);
        var counts=Query(db,candidates+" SELECT COUNT(DISTINCT group_key),COUNT(*) FROM candidates",row=>(Cards:row.GetInt32(0),Files:row.GetInt32(1)),args).Single();
        var cards=Query(db,candidates+CatalogSummarySql+CatalogCardSelect+
            " JOIN ranked r ON r.id=m.id AND r.card_rank=1 JOIN group_summary g ON g.group_key=r.group_key ORDER BY g.added_at DESC,g.group_key LIMIT $limit OFFSET $offset",ReadCatalogCard,args);
        return new(cards,counts.Cards,offset,limit,counts.Files);
    }
    public LibrarySeriesPage GetLibrarySeries(string mediaId,int? season,int offset,int limit,Account viewer)
    {
        _=GetMedia(mediaId,viewer)??throw new DomainException("Dizi dosyası bulunamadı.",404);
        if(season is <0 or >1000)throw new DomainException("Sezon filtresi geçersiz.");
        offset=Math.Max(0,offset);limit=Math.Clamp(limit,1,120);
        using var db=Open();RegisterCatalogFunctions(db);
        var candidates=CatalogCandidates(" WHERE l.enabled=1 AND "+LibraryAccess+" AND "+MediaAccess);
        (string,object?)[] args=[("$id",mediaId),("$season",season),("$offset",offset),("$limit",limit),..ViewerArgs(viewer)];
        var key=Scalar(db,candidates+" SELECT group_key FROM candidates WHERE id=$id AND group_kind='show'",args) as string??throw new DomainException("Bu dosya için dizi grubu bulunamadı.",404);
        args=[..args,("$group",key)];
        var item=Query(db,candidates+CatalogSummarySql+CatalogCardSelect+" JOIN group_summary g ON g.group_key=$group WHERE m.id=$id",ReadCatalogCard,args).Single();
        var total=Convert.ToInt32(Scalar(db,candidates+" SELECT COUNT(*) FROM candidates WHERE group_key=$group AND ($season IS NULL OR season=$season)",args));
        var seasons=Query(db,candidates+" SELECT DISTINCT season FROM candidates WHERE group_key=$group AND season IS NOT NULL ORDER BY season",row=>row.GetInt32(0),args).ToArray();
        var select=MediaSelect.Replace("FROM media m JOIN libraries l",",p.season,p.episode FROM media m JOIN libraries l",StringComparison.Ordinal);
        var files=Query(db,candidates+select+" JOIN candidates p ON p.id=m.id WHERE p.group_key=$group AND ($season IS NULL OR p.season=$season) ORDER BY p.season,p.episode,m.available DESC,m.id LIMIT $limit OFFSET $offset",
            row=>new SeriesFile(ReadMedia(row),row.IsDBNull(44)?null:row.GetInt32(44),row.IsDBNull(45)?null:row.GetInt32(45),ReadMedia(row).Content?.EpisodeTitle??ReadMedia(row).Title),args);
        return new(item,files,total,offset,limit,seasons);
    }
}

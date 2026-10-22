using System.Text.Json;
using HomeServer.Core;
using HomeServer.Playback;
using HomeServer.Scanning;

namespace HomeServer.Infrastructure;
public sealed partial class MediaStore
{
 private const string ShelfAccess="a.excluded=0 AND ($admin=1 OR (a.blocked=0 AND ($maxage<0 OR a.minimum_age<=$maxage OR (a.minimum_age IS NULL AND $unrated=1))))";
 private const string ShelfSelect="SELECT a.id,a.library_id,a.relative_path,a.title,a.folder,a.kind,a.mime,a.size_bytes,a.modified_at,a.fingerprint,a.available,a.minimum_age,EXISTS(SELECT 1 FROM shelf_favorites f WHERE f.asset_id=a.id AND f.user_id=$viewer) FROM shelf_assets a JOIN libraries l ON l.id=a.library_id";
 private static ShelfAsset ReadShelf(Microsoft.Data.Sqlite.SqliteDataReader r)=>new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetInt64(7),r.GetString(8),r.GetString(9),r.GetBoolean(10),r.IsDBNull(11)?null:r.GetInt32(11),r.GetBoolean(12));
 public ShelfPage ShelfAssets(Account actor,string? library,string? kind,string? folder,string? search,int offset)
 {
  using var db=Open();var key=MediaPaths.SearchKey(search??"");var filter=$" WHERE l.enabled=1 AND {LibraryAccess} AND {ShelfAccess} AND ($library='' OR a.library_id=$library) AND ($kind='' OR a.kind=$kind OR $kind='books' AND a.kind IN ('pdf','epub','comic','text')) AND ($folder IS NULL OR a.folder=$folder) AND ($q='' OR a.search_text LIKE $pattern ESCAPE '~')";
  (string,object?)[] args=[("$library",library??""),("$kind",kind??""),("$folder",folder),("$q",key),("$pattern","%"+key.Replace("~","~~").Replace("%","~%").Replace("_","~_")+"%"),("$offset",Math.Max(0,offset)),..ViewerArgs(actor)];
  var count=Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM shelf_assets a JOIN libraries l ON l.id=a.library_id"+filter,args));return new(Query(db,ShelfSelect+filter+" ORDER BY a.folder,a.relative_path LIMIT 60 OFFSET $offset",ReadShelf,args).ToArray(),count,Math.Max(0,offset),60);
 }
 public ShelfAsset RequireShelfAsset(string id,Account actor)
 {using var db=Open();return Query(db,ShelfSelect+$" WHERE a.id=$id AND l.enabled=1 AND {LibraryAccess} AND {ShelfAccess}",ReadShelf,[("$id",id),..ViewerArgs(actor)]).FirstOrDefault()??throw new DomainException("Belge/fotoğraf bulunamadı veya erişim yok.",404);}
 public (ShelfAsset Asset,string Path) ShelfSource(string id,Account actor)
 {
  var asset=RequireShelfAsset(id,actor);if(!asset.Available)throw new DomainException("Dosya çevrimdışı.",409);var library=GetLibrary(asset.LibraryId,actor)??throw new DomainException("Kütüphaneye erişilemiyor.",404);var path=MediaPaths.ResolveFile(library.Path,asset.RelativePath);
  if(LibraryTraversal.Excluded(asset.RelativePath,GetLibraryConfiguration(library.Id).Options))throw new DomainException("Dosya kapsam dışında.",403);
  if(MediaAnalysisService.Fingerprint(new FileInfo(path))!=asset.Fingerprint)throw new DomainException("Dosya değişti; kütüphaneyi yeniden tara.",409);return(asset,path);
 }
 public bool UpsertShelfAsset(Library library,string path,string kind,string mime,string scan)
 {
  var info=new FileInfo(path);var relative=Path.GetRelativePath(library.Path,path);var title=Path.GetFileNameWithoutExtension(path);var key=MediaPaths.Key(relative);var identity=StableFileIdentity.Read(path);
  lock(_writeGate)
  {
   using var db=Open();var id=Scalar(db,"SELECT id FROM shelf_assets WHERE library_id=$library AND path_key=$key",("$library",library.Id),("$key",key)) as string;var added=id is null;
   if(id is null&&identity is not null){var moved=Query(db,"SELECT id,relative_path FROM shelf_assets WHERE library_id=$library AND filesystem_id=$identity",r=>(Id:r.GetString(0),Path:r.GetString(1)),("$library",library.Id),("$identity",identity)).FirstOrDefault();if(moved.Id is not null&&!File.Exists(Path.Combine(library.Path,moved.Path))){id=moved.Id;added=false;}}
   id??=Guid.NewGuid().ToString("N");Execute(db,"INSERT INTO shelf_assets(id,library_id,relative_path,path_key,filesystem_id,title,search_text,folder,kind,mime,size_bytes,modified_at,fingerprint,last_seen) VALUES($id,$library,$relative,$key,$identity,$title,$search,$folder,$kind,$mime,$size,$modified,$fp,$scan) ON CONFLICT(id) DO UPDATE SET relative_path=$relative,path_key=$key,filesystem_id=$identity,title=$title,search_text=$search,folder=$folder,kind=$kind,mime=$mime,size_bytes=$size,modified_at=$modified,fingerprint=$fp,available=1,excluded=0,last_seen=$scan",("$id",id),("$library",library.Id),("$relative",relative),("$key",key),("$identity",identity),("$title",title),("$search",MediaPaths.SearchKey(title+" "+relative)),("$folder",(Path.GetDirectoryName(relative)??"").Replace('\\','/')),("$kind",kind),("$mime",mime),("$size",info.Length),("$modified",new DateTimeOffset(info.LastWriteTimeUtc).ToString("O")),("$fp",MediaAnalysisService.Fingerprint(info)),("$scan",scan));return added;
  }
 }
 public void FinishShelfScan(string library,string scan){lock(_writeGate){using var db=Open();Execute(db,"UPDATE shelf_assets SET available=0 WHERE library_id=$library AND last_seen<>$scan",("$library",library),("$scan",scan));}}
 public ShelfPosition ShelfProgress(string id,Account actor)
 {var asset=RequireShelfAsset(id,actor);using var db=Open();return Scalar(db,"SELECT position_json FROM shelf_progress WHERE asset_id=$id AND user_id=$user AND fingerprint=$fp",("$id",id),("$user",actor.Id),("$fp",asset.Fingerprint)) is string json?JsonSerializer.Deserialize<ShelfPosition>(json,WebJson)!:new();}
 public void SaveShelfProgress(string id,ShelfProgressInput input,Account actor)
 {
  var asset=ShelfSource(id,actor).Asset;_=AppFiles.Id(input.ClientId);if(input.Sequence<1||input.Position.Page is<1 or>100000||input.Position.Chapter is<0 or>10000||!double.IsFinite(input.Position.Fraction)||input.Position.Fraction is<0 or>1||!double.IsFinite(input.Position.Seconds)||input.Position.Seconds is<0 or>86400||asset.Fingerprint!=input.Fingerprint)throw new DomainException("Okuma konumu veya dosya revizyonu geçersiz.",409);
  lock(_writeGate){using var db=Open();Execute(db,"INSERT INTO shelf_progress(user_id,asset_id,fingerprint,position_json,client_id,sequence,updated_at) VALUES($user,$id,$fp,$json,$client,$sequence,$now) ON CONFLICT(user_id,asset_id) DO UPDATE SET fingerprint=$fp,position_json=$json,client_id=$client,sequence=$sequence,updated_at=$now WHERE shelf_progress.client_id<>$client OR shelf_progress.sequence<$sequence",("$user",actor.Id),("$id",id),("$fp",asset.Fingerprint),("$json",JsonSerializer.Serialize(input.Position,WebJson)),("$client",input.ClientId),("$sequence",input.Sequence),("$now",Now()));}
 }
 public void FavoriteShelf(string id,bool favorite,Account actor){_=RequireShelfAsset(id,actor);lock(_writeGate){using var db=Open();Execute(db,favorite?"INSERT OR IGNORE INTO shelf_favorites(user_id,asset_id) VALUES($user,$id)":"DELETE FROM shelf_favorites WHERE user_id=$user AND asset_id=$id",("$user",actor.Id),("$id",id));}}
 public void ShelfAccessSettings(string id,MediaAccessInput input){if(input.MinimumAge is<0 or>21)throw new DomainException("Yaş sınırı geçersiz.");lock(_writeGate){using var db=Open();Execute(db,"UPDATE shelf_assets SET minimum_age=$age,blocked=$blocked WHERE id=$id",("$id",id),("$age",input.MinimumAge),("$blocked",input.Blocked));}}
 public void SetAudiobookTitle(string id,string title){if(string.IsNullOrWhiteSpace(title))return;lock(_writeGate){using var db=Open();Execute(db,"UPDATE media SET title=$title,search_text=$search WHERE id=$id",("$id",id),("$title",title[..Math.Min(500,title.Length)]),("$search",MediaPaths.SearchKey(title)));}}
}

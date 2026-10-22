using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;
public sealed partial class MediaStore
{
 private const string DvrSelect="SELECT id,user_id,user_version,channel_id,programme_id,rule_id,episode_key,title,starts_at,ends_at,minimum_age,priority,status,revision,lease,message,bytes,media_id,created_at,source_revision FROM dvr_recordings";
 private static DvrRecording ReadDvr(Microsoft.Data.Sqlite.SqliteDataReader r)=>new(r.GetString(0),r.GetString(1),r.GetInt32(2),r.GetString(3),Text(r,4),Text(r,5),r.GetString(6),r.GetString(7),r.GetString(8),r.GetString(9),r.IsDBNull(10)?null:r.GetInt32(10),r.GetInt32(11),r.GetString(12),r.GetInt32(13),Text(r,14),r.GetString(15),r.GetInt64(16),Text(r,17),r.GetString(18),r.IsDBNull(19)?null:r.GetInt32(19));
 public DvrRecording? GetDvr(string id){using var db=Open();return Query(db,DvrSelect+" WHERE id=$id",ReadDvr,("$id",id)).FirstOrDefault();}
 public IReadOnlyList<DvrRecording> DvrRecordings(Account actor){using var db=Open();return Query(db,DvrSelect+" WHERE user_id=$user OR $admin=1 ORDER BY starts_at DESC LIMIT 500",ReadDvr,("$user",actor.Id),("$admin",actor.IsAdmin)).Where(row=>actor.IsAdmin||actor.MaximumAge is null||row.MinimumAge is{}age&&age<=actor.MaximumAge||row.MinimumAge is null&&actor.AllowUnrated).ToArray();}
 public DvrRecording QueueDvr(DvrInput input,Account actor,string? ruleId=null)
 {
  if(input.PaddingBefore is<0 or>3600||input.PaddingAfter is<0 or>3600||input.Priority is<1 or>50)throw new DomainException("Kayıt payı veya öncelik geçersiz.");lock(_writeGate)
  {
    var secret=GetAccountSecret(actor.Id);if(secret is not{Account.Enabled:true}||!secret.Account.IsAdmin&&!secret.Account.CanAcquire)throw new DomainException("Kayıt için edinme izni gerekli.",403);actor=secret.Account;var programme=GetLiveProgramme(input.ProgrammeId);var channel=RequireLiveChannel(programme.ChannelId,actor);
    if(!actor.IsAdmin&&actor.MaximumAge is{}limit&&(programme.MinimumAge is{}age?age>limit:!actor.AllowUnrated))throw new DomainException("Program hesap yaş sınırının dışında.",403);
   var start=DateTimeOffset.Parse(programme.StartsAt).AddSeconds(-input.PaddingBefore);var end=DateTimeOffset.Parse(programme.EndsAt).AddSeconds(input.PaddingAfter);if(end<=DateTimeOffset.UtcNow||end-start>TimeSpan.FromHours(12))throw new DomainException("Kayıt zamanı geçmiş veya 12 saat sınırını aşıyor.");
    using var db=Open();
    if(ruleId is not null)
    {
     var rule=DvrRules(actor).FirstOrDefault(value=>value.Id==ruleId);
     if(rule is not{Enabled:true}||rule.ChannelId!=channel.Id||MediaPaths.SearchKey(rule.Title)!=MediaPaths.SearchKey(programme.Title)||rule.OnlyNew&&programme.Repeat!=false||rule.PaddingBefore!=input.PaddingBefore||rule.PaddingAfter!=input.PaddingAfter||rule.Priority!=input.Priority||Convert.ToInt32(Scalar(db,"SELECT user_version FROM dvr_rules WHERE id=$rule",("$rule",ruleId)))!=secret.Version||Scalar(db,"SELECT episode_key FROM dvr_exclusions WHERE user_id=$user AND episode_key=$episode",("$user",actor.Id),("$episode",programme.EpisodeKey)) is string)throw new DomainException("Kayıt kuralı veya istisnası değişti.",409);
    }
    var existing=Query(db,DvrSelect+" WHERE user_id=$user AND episode_key=$episode AND status NOT IN ('failed','cancelled')",ReadDvr,("$user",actor.Id),("$episode",programme.EpisodeKey)).FirstOrDefault();if(existing is not null)return existing;
     RequireDvrCapacity(db);var id=Guid.NewGuid().ToString("N");var minimumAge=programme.MinimumAge is{}programmeAge&&channel.MinimumAge is{}channelAge?Math.Max(programmeAge,channelAge):programme.MinimumAge??channel.MinimumAge;
     Execute(db,"INSERT INTO dvr_recordings(id,user_id,user_version,channel_id,programme_id,rule_id,episode_key,title,starts_at,ends_at,minimum_age,priority,status,created_at,updated_at,source_revision) VALUES($id,$user,$version,$channel,$programme,$rule,$episode,$title,$start,$end,$age,$priority,'scheduled',$now,$now,$sourceRevision)",("$id",id),("$user",actor.Id),("$version",secret.Version),("$channel",programme.ChannelId),("$programme",programme.Id),("$rule",ruleId),("$episode",programme.EpisodeKey),("$title",programme.Title),("$start",start.ToString("O")),("$end",end.ToString("O")),("$age",minimumAge),("$priority",input.Priority),("$now",Now()),("$sourceRevision",GetLiveSource(channel.SourceId).Revision));return GetDvr(id)!;
  }
 }
 public IReadOnlyList<DvrRecording> DueDvr(){using var db=Open();return Query(db,DvrSelect+" WHERE (status IN ('publishing','finalizing') AND lease IS NULL AND updated_at<$retry) OR (status IN ('scheduled','conflict') AND starts_at<=$now AND (ends_at<=$now OR channel_id IN (SELECT c.id FROM live_channels c JOIN live_sources s ON s.id=c.source_id WHERE c.enabled=1 AND c.available=1 AND s.enabled=1))) ORDER BY CASE WHEN status='publishing' THEN 0 ELSE 1 END,priority,starts_at LIMIT 20",ReadDvr,("$now",Now()),("$retry",DateTimeOffset.UtcNow.AddSeconds(-10).ToString("O")));}
 public DvrRecording QueueTimedDvr(TimedDvrInput input,Account actor)
 {
  _=AppFiles.Id(input.CommandId);if(input.Minutes is<1 or>720||input.Title?.Length>500)throw new DomainException("Kayıt süresi 1–720 dakika olmalı.");lock(_writeGate)
  {
   var user=GetAccountSecret(actor.Id);if(user is not{Account.Enabled:true}||!user.Account.IsAdmin&&!user.Account.CanAcquire)throw new DomainException("Kayıt yetkisi gerekli.",403);
   var hash=Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input,WebJson)));using var db=Open();var old=GetDvr(input.CommandId);
    if(old is not null){if(old.UserId!=actor.Id||(Scalar(db,"SELECT request_hash FROM dvr_recordings WHERE id=$id",("$id",old.Id)) as string)!=hash)throw new DomainException("Komut kimliği farklı kayıt isteğiyle kullanılmış.",409);return old;}
   var channel=RequireLiveChannel(input.ChannelId,user.Account);var start=input.StartsAt is null?DateTimeOffset.UtcNow:DateTimeOffset.TryParse(input.StartsAt,out var parsed)?parsed.ToUniversalTime():throw new DomainException("Başlangıç zamanı geçersiz.");if(start<DateTimeOffset.UtcNow.AddMinutes(-1)||start>DateTimeOffset.UtcNow.AddDays(30))throw new DomainException("Kayıt zamanı desteklenen aralık dışında.");
   RequireDvrCapacity(db);var id=input.CommandId;Execute(db,"INSERT INTO dvr_recordings(id,user_id,user_version,channel_id,episode_key,title,starts_at,ends_at,minimum_age,priority,status,created_at,updated_at,source_revision,request_hash) VALUES($id,$user,$version,$channel,$episode,$title,$start,$end,$age,25,'scheduled',$now,$now,$sourceRevision,$hash)",("$id",id),("$user",actor.Id),("$version",user.Version),("$channel",channel.Id),("$episode","manual:"+id),("$title",string.IsNullOrWhiteSpace(input.Title)?channel.Name+" kaydı":input.Title),("$start",start.ToString("O")),("$end",start.AddMinutes(input.Minutes).ToString("O")),("$age",channel.MinimumAge),("$now",Now()),("$sourceRevision",GetLiveSource(channel.SourceId).Revision),("$hash",hash));return GetDvr(id)!;
  }
 }
 public DvrRecording? ClaimDvr(string id)
 {
   lock(_writeGate){var row=GetDvr(id);if(row is null||row.Status is not("scheduled" or"conflict" or"finalizing" or"publishing")||row.Lease is not null)return null;using var db=Open();
    if(row.Status is "publishing" or "finalizing"||DateTimeOffset.Parse(row.EndsAt)<=DateTimeOffset.UtcNow){Execute(db,"UPDATE dvr_recordings SET status=CASE WHEN status='publishing' THEN status ELSE 'finalizing' END,revision=revision+1,lease=$lease,updated_at=$now WHERE id=$id",("$id",id),("$lease",Guid.NewGuid().ToString("N")),("$now",Now()));return GetDvr(id);}
    if(DateTimeOffset.Parse(row.StartsAt)>DateTimeOffset.UtcNow)return null;
    var channel=GetLiveChannel(row.ChannelId);var source=GetLiveSource(channel.SourceId);if(!source.Enabled||!channel.Enabled||!channel.Available)return null;
    if(row.SourceRevision is{}revision&&revision!=source.Revision){Execute(db,"UPDATE dvr_recordings SET status='failed',message='Kayıt kaynağı değişti; yeni bir kayıt isteği gerekli.' WHERE id=$id",("$id",id));return null;}
   if(SourceTunerUse(source.Id)>=source.Tuners&&DateTimeOffset.Parse(row.EndsAt)>DateTimeOffset.UtcNow){Execute(db,"UPDATE dvr_recordings SET status='conflict',message='Tuner/akış kapasitesi dolu; kaynak boşalınca kalan bölüm kaydedilecek.' WHERE id=$id",("$id",id));return null;}
    Execute(db,"UPDATE dvr_recordings SET status='recording',source_revision=$sourceRevision,revision=revision+1,lease=$lease,updated_at=$now,message='Kayıt hazırlanıyor.' WHERE id=$id",("$id",id),("$sourceRevision",source.Revision),("$lease",Guid.NewGuid().ToString("N")),("$now",Now()));return GetDvr(id);
  }
 }
 public Account RequireDvr(DvrRecording work)
 {var current=GetDvr(work.Id);var user=GetAccountSecret(work.UserId);if(current is null||current.Status is not("recording" or"finalizing")||current.Revision!=work.Revision||current.Lease!=work.Lease)throw new OperationCanceledException();if(user is null||user.Version!=work.UserVersion||!user.Account.Enabled||!user.Account.IsAdmin&&!user.Account.CanAcquire)throw new DomainException("Kayıt hesabı yetkisi değişti.",403);if(!user.Account.IsAdmin&&user.Account.MaximumAge is{}limit&&(current.MinimumAge is{}age?age>limit:!user.Account.AllowUnrated))throw new DomainException("Kayıt profil yaş sınırının dışında.",403);if(current.Status=="recording"){var channel=RequireLiveChannel(work.ChannelId,user.Account);if(current.SourceRevision!=GetLiveSource(channel.SourceId).Revision)throw new DomainException("Kayıt kaynağı değişti.",409);}return user.Account;}
 public void DvrProgress(DvrRecording work,long bytes){lock(_writeGate){using var db=Open();Execute(db,"UPDATE dvr_recordings SET bytes=$bytes,updated_at=$now,message='Kanal kaydediliyor.' WHERE id=$id AND status='recording' AND revision=$revision AND lease=$lease",("$id",work.Id),("$revision",work.Revision),("$lease",work.Lease),("$bytes",bytes),("$now",Now()));}}
 public void FinishDvr(DvrRecording work,string status,string message,string? mediaId=null){lock(_writeGate){using var db=Open();Execute(db,"UPDATE dvr_recordings SET status=$status,message=$message,media_id=COALESCE($media,media_id),lease=NULL,updated_at=$now WHERE id=$id AND revision=$revision AND lease=$lease AND status IN ('recording','finalizing')",("$id",work.Id),("$revision",work.Revision),("$lease",work.Lease),("$status",status),("$message",message),("$media",mediaId),("$now",Now()));}}
 public void CancelDvr(string id,int revision,Account actor)
 {lock(_writeGate){var row=GetDvr(id)??throw new DomainException("Kayıt bulunamadı.",404);if(row.UserId!=actor.Id&&!actor.IsAdmin)throw new DomainException("Kayıt bulunamadı.",404);if(row.Revision!=revision)throw new DomainException("Kayıt değişti.",409);if(row.Status=="publishing")throw new DomainException("Doğrulanan kayıt atomik sonlandırılıyor; yayın günlüğü tamamlanmalı.",409);using var db=Open();Execute(db,"UPDATE dvr_recordings SET status='cancelled',revision=revision+1,lease=NULL,message='Kullanıcı kaydı iptal etti.',updated_at=$now WHERE id=$id AND status IN ('scheduled','conflict','recording','finalizing'); INSERT OR IGNORE INTO dvr_exclusions(user_id,episode_key) VALUES($user,$episode)",("$id",id),("$user",row.UserId),("$episode",row.EpisodeKey),("$now",Now()));}}
 public void RecoverDvr(){lock(_writeGate){using var db=Open();Execute(db,"UPDATE dvr_recordings SET status=CASE WHEN status='recording' THEN 'scheduled' ELSE status END,revision=revision+1,lease=NULL,message='Kesilen kayıt kendi aşamasından toparlanacak.' WHERE status IN ('recording','finalizing','publishing')");}}
 private static void RequireDvrCapacity(Microsoft.Data.Sqlite.SqliteConnection db){if(Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM dvr_recordings WHERE status IN ('scheduled','conflict','recording','finalizing','publishing')"))>=500)throw new DomainException("Kayıt planı sınırı dolu.",429);}
 public IReadOnlyList<DvrRule> DvrRules(Account actor){using var db=Open();return Query(db,"SELECT id,user_id,channel_id,title,enabled,only_new,padding_before,padding_after,priority,revision FROM dvr_rules WHERE user_id=$user",r=>new DvrRule(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetBoolean(4),r.GetBoolean(5),r.GetInt32(6),r.GetInt32(7),r.GetInt32(8),r.GetInt32(9)),("$user",actor.Id));}
 public DvrRule SaveDvrRule(string? id,DvrRuleInput input,Account actor)
 {
  lock(_writeGate)
  {
   var user=GetAccountSecret(actor.Id);if(user is not{Account.Enabled:true})throw new DomainException("Etkin hesap gerekli.",403);actor=user.Account;
   if(string.IsNullOrWhiteSpace(input.Title)||input.Title.Length>500||input.PaddingBefore is<0 or>3600||input.PaddingAfter is<0 or>3600||input.Priority is<1 or>50)throw new DomainException("Dizi kayıt kuralı geçersiz.");
   var old=id is null?null:DvrRules(actor).FirstOrDefault(rule=>rule.Id==id)??throw new DomainException("Kayıt kuralı bulunamadı.",404);
   if((old is null||input.Enabled)&&!actor.IsAdmin&&!actor.CanAcquire)throw new DomainException("Kayıt yetkisi gerekli.",403);
   if(input.Enabled)_=RequireLiveChannel(input.ChannelId,actor);else _=GetLiveChannel(input.ChannelId);
   if((old?.Revision??0)!=input.ExpectedRevision)throw new DomainException("Kayıt kuralı değişti.",409);
   if(old is null&&DvrRules(actor).Count>=100)throw new DomainException("En fazla 100 kayıt kuralı saklanabilir.");
   id??=Guid.NewGuid().ToString("N");using var db=Open();
   Execute(db,"""
    INSERT INTO dvr_rules(id,user_id,user_version,channel_id,title,title_key,enabled,only_new,padding_before,padding_after,priority)
    VALUES($id,$user,$version,$channel,$title,$key,$enabled,$new,$before,$after,$priority)
    ON CONFLICT(id) DO UPDATE SET user_version=$version,channel_id=$channel,title=$title,title_key=$key,enabled=$enabled,only_new=$new,padding_before=$before,padding_after=$after,priority=$priority,revision=revision+1
    """,("$id",id),("$user",actor.Id),("$version",user.Version),("$channel",input.ChannelId),("$title",input.Title),("$key",MediaPaths.SearchKey(input.Title)),("$enabled",input.Enabled),("$new",input.OnlyNew),("$before",input.PaddingBefore),("$after",input.PaddingAfter),("$priority",input.Priority));
   return DvrRules(actor).Single(rule=>rule.Id==id);
  }
 }
 public void ScheduleDvrRules()
 {
  using var db=Open();var users=Query(db,"SELECT DISTINCT user_id FROM dvr_rules WHERE enabled=1",r=>r.GetString(0));foreach(var userId in users){var user=GetAccountSecret(userId);if(user is null)continue;foreach(var rule in DvrRules(user.Account).Where(rule=>rule.Enabled))
   {var version=Convert.ToInt32(Scalar(db,"SELECT user_version FROM dvr_rules WHERE id=$id",("$id",rule.Id)));if(version!=user.Version)continue;foreach(var programme in LiveGuide(rule.ChannelId,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddDays(14)).Where(programme=>MediaPaths.SearchKey(programme.Title)==MediaPaths.SearchKey(rule.Title)&&(!rule.OnlyNew||programme.Repeat==false)))
    {if(Scalar(db,"SELECT episode_key FROM dvr_exclusions WHERE user_id=$user AND episode_key=$episode",("$user",userId),("$episode",programme.EpisodeKey)) is string)continue;try{QueueDvr(new(programme.Id,rule.PaddingBefore,rule.PaddingAfter,rule.Priority),user.Account,rule.Id);}catch(DomainException){}}
   }
  }
 }
}

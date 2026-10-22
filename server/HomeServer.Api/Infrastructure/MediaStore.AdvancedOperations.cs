using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Subtitles;

namespace HomeServer.Infrastructure;
public sealed partial class MediaStore
{
 private static readonly string[] ConfigurationKeys=["metadata.language","playback.limits","playback.encoder","speech.settings","receivers.settings","maintenance.preferences"];
 public void RecordOperationMetric(OperationMetric metric){lock(_writeGate){using var db=Open();Execute(db,"INSERT OR REPLACE INTO operation_metrics(at,metrics_json) VALUES($at,$json); DELETE FROM operation_metrics WHERE at<$old",("$at",metric.At),("$json",JsonSerializer.Serialize(metric,WebJson)),("$old",DateTimeOffset.UtcNow.AddDays(-3).ToString("O")));}}
 public IReadOnlyList<OperationMetric> OperationMetrics(int hours){using var db=Open();return Query(db,"SELECT metrics_json FROM operation_metrics WHERE at>=$from ORDER BY at LIMIT 8640",r=>JsonSerializer.Deserialize<OperationMetric>(r.GetString(0),WebJson)!, ("$from",DateTimeOffset.UtcNow.AddHours(-Math.Clamp(hours,1,72)).ToString("O")));}
 public (int Jobs,long DatabaseBytes) OperationCounts(){using var db=Open();return(Convert.ToInt32(Scalar(db,"SELECT (SELECT COUNT(*) FROM product_jobs WHERE status IN ('queued','running','applying'))+(SELECT COUNT(*) FROM jobs WHERE status IN ('queued','running'))")),Convert.ToInt64(Scalar(db,"PRAGMA page_count"))*Convert.ToInt64(Scalar(db,"PRAGMA page_size")));}
 public ConfigurationBundle ExportConfiguration(){using var db=Open();var settings=Query(db,"SELECT key,value FROM settings WHERE key IN (SELECT value FROM json_each($keys)) ORDER BY key",r=>(Key:r.GetString(0),Value:r.GetString(1)),("$keys",JsonSerializer.Serialize(ConfigurationKeys))).ToDictionary(row=>row.Key,row=>row.Value);return new(1,settings,GetQualityProfiles().OrderBy(profile=>profile.Id).ToArray());}
 public static string ConfigurationHash(ConfigurationBundle bundle)=>Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(bundle,WebJson)));
 public void ValidateConfiguration(ConfigurationBundle bundle)
 {
  if(bundle.FormatVersion!=1||bundle.Settings is null||bundle.Profiles is null||bundle.Settings.Count>ConfigurationKeys.Length||bundle.Profiles.Length>100||bundle.Profiles.Select(profile=>profile.Id).Distinct().Count()!=bundle.Profiles.Length)throw new DomainException("Yapılandırma paketi sürümü veya boyutu geçersiz.");
  foreach(var entry in bundle.Settings)
  {
   if(!ConfigurationKeys.Contains(entry.Key)||entry.Value.Length>64000)throw new DomainException("Yapılandırmada desteklenmeyen alan var.");
   if(entry.Key=="metadata.language"&&entry.Value is not("tr-TR" or "en-US"))throw new DomainException("Metadata dili desteklenmiyor.");
   if(entry.Key=="playback.limits"){var value=JsonSerializer.Deserialize<PlaybackLimits>(entry.Value,WebJson);if(value is null||value.MaximumStreams is<1 or>64||value.MaximumStreamsPerDevice is<1 or>16)throw new DomainException("Oynatma limitleri geçersiz.");}
   if(entry.Key=="playback.encoder"&&entry.Value is not("auto" or "software" or "nvenc"))throw new DomainException("Kodlayıcı tercihi geçersiz.");
   if(entry.Key=="speech.settings"){var value=JsonSerializer.Deserialize<SpeechSettings>(entry.Value,WebJson);if(value is null||value.Threads is<1 or>16||value.Executable?.Length>2000||value.ModelPath?.Length>2000)throw new DomainException("Konuşma aracı ayarları geçersiz.");}
   if(entry.Key=="receivers.settings"){var value=JsonSerializer.Deserialize<ReceiverSettings>(entry.Value,WebJson);if(value?.PublicBaseUrl is{Length:>0}url&&(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https")||uri.UserInfo.Length>0||uri.Query.Length>0||uri.IsLoopback))throw new DomainException("Alıcı adresi geçersiz.");}
   if(entry.Key=="maintenance.preferences"){var value=JsonSerializer.Deserialize<MaintenancePreferences>(entry.Value,WebJson);if(value is null||value.IntervalHours is<1 or>720||value.CacheDays is<1 or>365)throw new DomainException("Bakım tercihleri geçersiz.");}
  }
  foreach(var profile in bundle.Profiles){if(string.IsNullOrEmpty(profile.Id)||profile.Id.Length>64)throw new DomainException("Profil kimliği geçersiz.");ValidateQualityProfile(new(profile.Name,profile.Rules));}
 }
 public object ApplyConfiguration(ConfigurationApply input)
 {
  _=AppFiles.Id(input.CommandId);ValidateConfiguration(input.Bundle);lock(_writeGate)
  {
   using var db=Open();var requestHash=ConfigurationHash(input.Bundle);var prior=Scalar(db,"SELECT request_hash FROM configuration_receipts WHERE id=$id",("$id",input.CommandId)) as string;
   if(prior is not null)return prior==requestHash?new{applied=true,repeated=true}:throw new DomainException("Komut kimliği farklı yapılandırmayla kullanıldı.",409);
   if(ConfigurationHash(ExportConfiguration())!=input.ExpectedCurrentHash)throw new DomainException("Yapılandırma önizlemeden sonra değişti.",409);var profiles=GetQualityProfiles();if(profiles.Select(profile=>profile.Id).Union(input.Bundle.Profiles.Select(profile=>profile.Id)).Count()>100)throw new DomainException("Profil sınırı aşılıyor.");
   using var tx=db.BeginTransaction();foreach(var pair in input.Bundle.Settings)
   {
    var value=pair.Value;if(pair.Key=="playback.limits")value=JsonSerializer.Serialize(JsonSerializer.Deserialize<PlaybackLimits>(value,WebJson)! with{Revision=GetPlaybackLimits().Revision+1},WebJson);
    MusicSql(db,tx,"INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=$value",("$key",pair.Key),("$value",value));
   }
   foreach(var profile in input.Bundle.Profiles)MusicSql(db,tx,"INSERT INTO quality_profiles(id,name,settings_json,updated_at) VALUES($id,$name,$json,$now) ON CONFLICT(id) DO UPDATE SET name=$name,settings_json=$json,updated_at=$now; UPDATE automation_rules SET revision=revision+1,next_run=$now WHERE content_id IN (SELECT content_id FROM tracking WHERE quality_profile_id=$id); UPDATE automation_jobs SET status='cancelled',message='Yapılandırma profili değişti.' WHERE status IN ('queued','waiting','running') AND content_id IN (SELECT content_id FROM tracking WHERE quality_profile_id=$id)",("$id",profile.Id),("$name",profile.Name),("$json",JsonSerializer.Serialize(profile.Rules,WebJson)),("$now",Now()));
   MusicSql(db,tx,"INSERT INTO configuration_receipts(id,request_hash,result_json,created_at) VALUES($id,$hash,'{}',$now); DELETE FROM configuration_receipts WHERE created_at<$old",("$id",input.CommandId),("$hash",requestHash),("$now",Now()),("$old",DateTimeOffset.UtcNow.AddDays(-30).ToString("O")));tx.Commit();return new{applied=true,repeated=false};
  }
 }
 public string DatabaseMaintenance(string action)
 {
  lock(_writeGate)
  {
   using var db=Open();if(action=="integrity"){var result=Convert.ToString(Scalar(db,"PRAGMA quick_check"));var keys=Query(db,"PRAGMA foreign_key_check",r=>r.GetString(0));if(result!="ok"||keys.Count>0)throw new DomainException("Veritabanı bütünlük kontrolü sorun bildirdi.",409);return "SQLite quick_check ve ilişki kontrolü başarılı.";}
   if(action!="compact")throw new DomainException("Bakım işlemi geçersiz.");if(ActivePlaybackLeases().Count>0||GetDownloads().Any(download=>download.Status is"downloading" or"importing"))throw new DomainException("Veritabanı sıkıştırması için etkin oynatma/aktarımın bitmesini bekle.",409);Execute(db,"PRAGMA wal_checkpoint(TRUNCATE); VACUUM;");return "SQLite günlük dosyası birleştirildi ve veritabanı sıkıştırıldı.";
  }
 }
}

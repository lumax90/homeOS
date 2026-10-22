using System.Security.Cryptography;
using HomeServer.Core;

namespace HomeServer.Infrastructure;
public sealed partial class MediaStore
{
 public DeviceLinkChallenge StartDeviceLink(string name)
 {
  if(string.IsNullOrWhiteSpace(name)||name.Length>120||name.Any(char.IsControl))throw new DomainException("Cihaz adı geçersiz.");lock(_writeGate)
  {
   using var db=Open();Execute(db,"DELETE FROM device_links WHERE expires_at<$now OR consumed_at IS NOT NULL",("$now",Now()));if(Convert.ToInt32(Scalar(db,"SELECT COUNT(*) FROM device_links"))>=128)throw new DomainException("Cihaz bağlantı kuyruğu dolu.",429);
   var id=Guid.NewGuid().ToString("N");var secret=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));string code;do{code=RandomNumberGenerator.GetInt32(10000000,100000000).ToString();}while(Scalar(db,"SELECT id FROM device_links WHERE code=$code",("$code",code)) is string);
   var expires=DateTimeOffset.UtcNow.AddMinutes(5).ToString("O");Execute(db,"INSERT INTO device_links(id,secret_hash,code,device_name,created_at,expires_at) VALUES($id,$secret,$code,$name,$now,$expires)",("$id",id),("$secret",TokenHash(secret)),("$code",code),("$name",name.Trim()),("$now",Now()),("$expires",expires));return new(id,secret,code,expires);
  }
 }
 public object DeviceLinkInfo(string code,Account actor)
 {if(GetAccount(actor.Id) is not{Enabled:true})throw new DomainException("Oturum gerekli.",401);using var db=Open();return Query(db,"SELECT device_name,created_at,expires_at FROM device_links WHERE code=$code AND expires_at>$now AND consumed_at IS NULL AND user_id IS NULL",r=>new{deviceName=r.GetString(0),createdAt=r.GetString(1),expiresAt=r.GetString(2)},("$code",code),("$now",Now())).FirstOrDefault()??throw new DomainException("Bekleyen cihaz kodu bulunamadı.",404);}
 public void ApproveDeviceLink(string code,string userId,string? session)
 {
  lock(_writeGate){if(session is null||ValidateLoginSession(session,userId) is null)throw new DomainException("Oturum gerekli.",401);var user=GetAccountSecret(userId)!;using var db=Open();if(Execute(db,"UPDATE device_links SET user_id=$user,user_version=$version,approved_session=$session WHERE code=$code AND user_id IS NULL AND expires_at>$now AND consumed_at IS NULL",("$user",userId),("$version",user.Version),("$session",session),("$code",code),("$now",Now()))!=1)throw new DomainException("Cihaz kodu eskidi veya kullanıldı.",409);}
 }
 public AccountSecret? ConsumeDeviceLink(DeviceLinkPoll input)
 {
  if(input.Secret is not{Length:64})throw new DomainException("Cihaz isteği geçersiz.",404);lock(_writeGate)
  {
   using var db=Open();var row=Query(db,"SELECT id,user_id,user_version,approved_session FROM device_links WHERE id=$id AND secret_hash=$hash AND expires_at>$now AND consumed_at IS NULL",r=>(Id:r.GetString(0),User:Text(r,1),Version:r.IsDBNull(2)?0:r.GetInt32(2),Session:Text(r,3)),("$id",input.Id),("$hash",TokenHash(input.Secret)),("$now",Now())).FirstOrDefault();
   if(row.Id is null)throw new DomainException("Cihaz bağlantı isteğinin süresi doldu.",410);if(row.User is null)return null;var user=GetAccountSecret(row.User);
   if(user is null||user.Version!=row.Version||row.Session is null||ValidateLoginSession(row.Session,row.User) is null)throw new DomainException("Bağlantıyı onaylayan hesabın oturumu değişti.",403);Execute(db,"UPDATE device_links SET consumed_at=$now WHERE id=$id",("$id",input.Id),("$now",Now()));return user;
  }
 }
 private const string RenderSelect="SELECT id,udn,name,address,transport_url,transport_type,render_url,render_type,protocols,last_seen FROM render_devices";
 private static RenderDevice ReadRenderer(Microsoft.Data.Sqlite.SqliteDataReader r)=>new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),Text(r,6),Text(r,7),r.GetString(8),r.GetString(9));
 public IReadOnlyList<RenderDevice> RenderDevices(){using var db=Open();return Query(db,RenderSelect+" ORDER BY name LIMIT 100",ReadRenderer);}
 public RenderDevice? GetRenderDevice(string id){using var db=Open();return Query(db,RenderSelect+" WHERE id=$id",ReadRenderer,("$id",id)).FirstOrDefault();}
 public RenderDevice SaveRenderDevice(RenderDevice value)
 {lock(_writeGate){using var db=Open();var id=Scalar(db,"SELECT id FROM render_devices WHERE udn=$udn",("$udn",value.Udn)) as string??Guid.NewGuid().ToString("N");Execute(db,"INSERT INTO render_devices(id,udn,name,address,transport_url,transport_type,render_url,render_type,protocols,last_seen) VALUES($id,$udn,$name,$address,$transport,$type,$render,$renderType,$protocols,$now) ON CONFLICT(id) DO UPDATE SET name=$name,address=$address,transport_url=$transport,transport_type=$type,render_url=$render,render_type=$renderType,protocols=$protocols,last_seen=$now",("$id",id),("$udn",value.Udn),("$name",value.Name),("$address",value.Address),("$transport",value.TransportUrl),("$type",value.TransportType),("$render",value.RenderUrl),("$renderType",value.RenderType),("$protocols",value.Protocols),("$now",Now()));return GetRenderDevice(id)!;}}
}

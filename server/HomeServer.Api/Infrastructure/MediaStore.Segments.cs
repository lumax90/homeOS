using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;
public sealed partial class MediaStore
{
 public MediaSegmentSet GetMediaSegments(string id,Account actor)
 {
  var source=PlaybackSource(id,actor);using var db=Open();var row=Query(db,"SELECT revision,fingerprint,match_revision,segments_json FROM media_segment_sets WHERE media_id=$id",r=>new MediaSegmentSet(id,r.GetInt32(0),r.GetString(1),r.GetInt32(2),JsonSerializer.Deserialize<MediaSegment[]>(r.GetString(3),WebJson)!), ("$id",id)).FirstOrDefault();
  return row is null?new(id,0,source.Fingerprint,source.Media.MatchRevision,[]):row with{Segments=row.Fingerprint==source.Fingerprint&&row.MatchRevision==source.Media.MatchRevision?row.Segments:[]};
 }
 public MediaSegmentSet SaveMediaSegments(string id,SegmentInput input,Account actor)
 {
  lock(_writeGate)
  {
   if(GetAccount(actor.Id) is not{Enabled:true,IsAdmin:true})throw new DomainException("Atlama aralıkları yönetici gerektirir.",403);var source=PlaybackSource(id,actor);var old=GetMediaSegments(id,actor);
   if(input.ExpectedRevision!=old.Revision||input.ExpectedMatchRevision!=source.Media.MatchRevision||input.Fingerprint!=source.Fingerprint)throw new DomainException("Dosya veya atlama aralıkları değişti.",409);
   if(input.Segments is null||input.Segments.Length>100||source.Media.DurationSeconds is not>0)throw new DomainException("Geçerli dosya süresi ve en fazla 100 aralık gerekli.");
   foreach(var segment in input.Segments)if(segment.Kind is not("intro" or "recap" or "credits" or "commercial")||!double.IsFinite(segment.StartSeconds)||!double.IsFinite(segment.EndSeconds)||segment.StartSeconds<0||segment.EndSeconds<=segment.StartSeconds||segment.EndSeconds>source.Media.DurationSeconds+.1||!double.IsFinite(segment.Confidence)||segment.Confidence is <0 or >1)throw new DomainException("Atlama aralığı geçersiz.");
   var segments=input.Segments.OrderBy(segment=>segment.StartSeconds).ToArray();using var db=Open();Execute(db,"INSERT INTO media_segment_sets(media_id,fingerprint,match_revision,segments_json,updated_by,updated_at) VALUES($id,$fp,$match,$json,$user,$now) ON CONFLICT(media_id) DO UPDATE SET fingerprint=$fp,match_revision=$match,segments_json=$json,revision=revision+1,updated_by=$user,updated_at=$now",("$id",id),("$fp",source.Fingerprint),("$match",source.Media.MatchRevision),("$json",JsonSerializer.Serialize(segments,WebJson)),("$user",actor.Id),("$now",Now()));return GetMediaSegments(id,actor);
  }
 }
 public PlayerPreferences GetPlayerPreferences(string user){using var db=Open();return Scalar(db,"SELECT preferences_json FROM player_preferences WHERE user_id=$user",("$user",user)) is string json?JsonSerializer.Deserialize<PlayerPreferences>(json,WebJson)!:new();}
 public void SavePlayerPreferences(string user,PlayerPreferences input){lock(_writeGate){using var db=Open();Execute(db,"INSERT INTO player_preferences(user_id,preferences_json) VALUES($user,$json) ON CONFLICT(user_id) DO UPDATE SET preferences_json=$json",("$user",user),("$json",JsonSerializer.Serialize(input,WebJson)));}}
}

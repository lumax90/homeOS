using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public ProbeResult? GetAnalysis(string mediaId, string fingerprint)
    {
        using var db = Open();
        var json = Scalar(db, "SELECT analysis_json FROM media_analysis WHERE media_id=$id AND fingerprint=$fingerprint AND schema_version=2 AND NOT EXISTS(SELECT 1 FROM music_tracks t WHERE t.media_id=$id AND t.credit_version=0)",
            ("$id", mediaId), ("$fingerprint", fingerprint)) as string;
        return json is null ? null : JsonSerializer.Deserialize<ProbeResult>(json, WebJson);
    }

    public void SaveAnalysis(string mediaId, string fingerprint, ProbeResult analysis)
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, """
                INSERT INTO media_analysis(media_id,fingerprint,analysis_json,updated_at,schema_version) VALUES($id,$fingerprint,$json,$now,2)
                ON CONFLICT(media_id) DO UPDATE SET fingerprint=$fingerprint,analysis_json=$json,updated_at=$now,schema_version=2
                """, ("$id", mediaId), ("$fingerprint", fingerprint), ("$json", JsonSerializer.Serialize(analysis, WebJson)), ("$now", Now()));
            if (analysis.Music is { } music) SaveMusicTags(mediaId, music);
        }
    }

    public IReadOnlyList<ManagedSubtitle> GetManagedSubtitles(string mediaId, bool includeRetired = false)
    {
        using var db = Open();
        return Query(db, "SELECT id,media_id,language,title,extension,created_at,forced,hearing_impaired,origin,score,provider_file_id,retired,media_fingerprint,match_revision,supersedes_id FROM managed_subtitles WHERE media_id=$id AND (retired=0 OR $history=1) ORDER BY created_at",
            r => new ManagedSubtitle(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetBoolean(6), r.GetBoolean(7), r.GetString(8), r.IsDBNull(9) ? null : r.GetInt32(9), r.IsDBNull(10) ? null : r.GetInt64(10), r.GetBoolean(11), Text(r, 12), r.IsDBNull(13) ? null : r.GetInt32(13), Text(r, 14)), ("$id", mediaId), ("$history", includeRetired));
    }

    public ManagedSubtitle AddManagedSubtitle(string mediaId, string id, string language, string title, string extension, bool forced = false, bool hearingImpaired = false, int? automationRevision = null, int? expectedMatchRevision = null, SubtitlePublication? publication = null, string? payloadHash = null)
    {
        lock (_writeGate)
        {
            var media = GetMedia(mediaId) ?? throw new DomainException("Medya bulunamadı.", 404);
            if (expectedMatchRevision is { } expected && media.MatchRevision != expected) throw new DomainException("Dosya kapsamı değişti; eski altyazı yayınlanmadı.", 409);
            if (publication is not null) ValidateSubtitlePublication(mediaId, language, publication);
            if (automationRevision is not null && publication is null)
            {
                var profile = GetSetting("subtitles.profile") is { } json ? JsonSerializer.Deserialize<SubtitleLanguageProfile>(json) : null;
                if (GetSetting("subtitles.profile.revision") != automationRevision.Value.ToString() || profile is not { Enabled: true, AutoDownload: true }) throw new DomainException("Altyazı otomasyonu arada değişti.", 409);
            }
            if (GetManagedSubtitles(mediaId).Count - (publication?.SupersedesId is not null ? 1 : 0) >= 16 || GetManagedSubtitles(mediaId, true).Count >= 128) throw new DomainException("Altyazı sürüm sınırı dolu; geçmişten gereksiz sürümleri temizle.", 409);
            if (publication?.Origin is not (null or "upload" or "provider" or "sync" or "generated") || publication?.Score is < 0 or > 150) throw new DomainException("Altyazı yayın bilgisi geçersiz.");
            using var db = Open();
            var created = Now();
            using var tx = db.BeginTransaction(); using var command = Command(db, """
                INSERT INTO managed_subtitles(id,media_id,language,title,extension,created_at,forced,hearing_impaired,origin,score,provider_file_id,media_fingerprint,match_revision,payload_hash,supersedes_id,sync_job_id)
                VALUES($id,$media,$language,$title,$extension,$now,$forced,$hi,$origin,$score,$provider,$fingerprint,$revision,$hash,$previous,$job);
                UPDATE managed_subtitles SET retired=1 WHERE id=$previous AND media_id=$media;
                UPDATE subtitle_sync_jobs SET status='completed',subtitle_id=$id,progress=100,message='Senkronlanmış WebVTT sürümü yayımlandı.',updated_at=$now WHERE id=$job AND revision=$jobRevision AND status='applying';
                """, ("$id", id), ("$media", mediaId), ("$language", language), ("$title", title), ("$extension", extension), ("$now", created), ("$forced", forced), ("$hi", hearingImpaired),
                ("$origin", publication?.Origin ?? "upload"), ("$score", publication?.Score), ("$provider", publication?.ProviderFileId), ("$fingerprint", publication?.Fingerprint), ("$revision", publication?.MatchRevision), ("$hash", payloadHash), ("$previous", publication?.SupersedesId), ("$job", publication?.JobId), ("$jobRevision", publication?.JobRevision));
            command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit();
            return GetManagedSubtitles(mediaId).Single(subtitle => subtitle.Id == id);
        }
    }

    public ManagedSubtitle RestoreSubtitleVersion(string mediaId, string id, string? replaceId)
    {
        lock (_writeGate)
        {
            var target = GetManagedSubtitles(mediaId, true).FirstOrDefault(item => item.Id == id) ?? throw new DomainException("Altyazı sürümü bulunamadı.", 404);
            if (!target.Retired) return target;
            if (target.Fingerprint is { } fingerprint && target.MatchRevision is { } revision) ValidateSubtitlePublication(mediaId, target.Language, new("upload", fingerprint, revision));
            var path = MediaPaths.ResolveFile(_paths.State, Path.Combine("subtitles", AppFiles.Id(mediaId), AppFiles.Id(id) + "." + target.Extension));
            using var db = Open(); var expectedHash = Scalar(db, "SELECT payload_hash FROM managed_subtitles WHERE id=$id", ("$id", id)) as string;
            if (expectedHash is not null && StateFolders.Hash(path) != expectedHash) throw new DomainException("Altyazı geri alma dosyası değişti.", 409);
            if (replaceId is not null && !GetManagedSubtitles(mediaId).Any(item => item.Id == replaceId && item.Language == target.Language)) throw new DomainException("Yerine geçilecek etkin altyazı değişti.", 409);
            if (replaceId is null && GetManagedSubtitles(mediaId).Count >= 16) throw new DomainException("Etkin altyazı sınırı dolu.", 409);
            using var tx = db.BeginTransaction(); using var command = Command(db, """
                UPDATE managed_subtitles SET retired=1 WHERE id=$replace AND media_id=$media;
                UPDATE managed_subtitles SET retired=0 WHERE id=$id AND media_id=$media;
                INSERT INTO media_subtitle_overrides(media_id,hold_upgrades,updated_at) VALUES($media,1,$now)
                ON CONFLICT(media_id) DO UPDATE SET hold_upgrades=1,revision=revision+1,updated_at=$now;
                UPDATE subtitle_wanted SET lease_token=NULL,lease_until=NULL,policy_json=NULL,status='waiting',next_run=$now WHERE media_id=$media;
                """, ("$media", mediaId), ("$id", id), ("$replace", replaceId), ("$now", Now())); command.Transaction = tx; command.ExecuteNonQuery(); tx.Commit(); return GetManagedSubtitles(mediaId).Single(item => item.Id == id);
        }
    }

    public void RemoveManagedSubtitle(string mediaId, string id)
    {
        lock (_writeGate)
        {
            using var db = Open();
            Execute(db, "DELETE FROM managed_subtitles WHERE media_id=$media AND id=$id", ("$media", mediaId), ("$id", id));
        }
    }
}

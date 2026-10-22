using System.Text.Json;
using HomeServer.Core;
using HomeServer.Playback;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public int NextDvrCaptureAttempt(DvrRecording work)
    {
        lock (_writeGate)
        {
            _ = RequireDvr(work); if (GetDvr(work.Id)?.Status != "recording") throw new OperationCanceledException(); using var db = Open();
            var attempts = Convert.ToInt32(Scalar(db, "SELECT capture_attempts FROM dvr_recordings WHERE id=$id", ("$id", work.Id)));
            if (attempts >= 6) return 0;
            Execute(db, "UPDATE dvr_recordings SET capture_attempts=capture_attempts+1 WHERE id=$id AND revision=$revision AND lease=$lease AND status='recording'", ("$id", work.Id), ("$revision", work.Revision), ("$lease", work.Lease));
            return attempts + 1;
        }
    }
    public DvrPublication? GetDvrPublication(string id)
    {
        using var db = Open(); var json = Scalar(db, "SELECT publication_json FROM dvr_recordings WHERE id=$id", ("$id", id)) as string;
        return json is null ? null : JsonSerializer.Deserialize<DvrPublication>(json, WebJson);
    }
    public DvrRecording BeginDvrFinalization(DvrRecording work)
    {
        lock (_writeGate)
        {
            _ = RequireDvr(work); using var db = Open();
            Execute(db, "UPDATE dvr_recordings SET status='finalizing',message='Alınan parçalar doğrulanıyor.',updated_at=$now WHERE id=$id AND revision=$revision AND lease=$lease AND status='recording'",
                ("$id", work.Id), ("$revision", work.Revision), ("$lease", work.Lease), ("$now", Now()));
            return GetDvr(work.Id)!;
        }
    }
    public bool BeginDvrPublication(DvrRecording work, DvrPublication plan)
    {
        lock (_writeGate)
        {
            _ = RequireDvr(work);
            if (GetDvr(work.Id)?.Status != "finalizing") throw new OperationCanceledException();
            if (plan.RecordingId != work.Id || plan.Length <= 0 || plan.Sha256.Length != 64 || !plan.Sha256.All(char.IsAsciiHexDigit) || plan.Probe.Duration is not > 0) throw new DomainException("Kayıt yayın günlüğü eksik.", 409);
            var library = GetLibraries(true).FirstOrDefault(value => value.Id == plan.LibraryId) ?? throw new DomainException("Kayıt kütüphanesi bulunamadı.", 404);
            if (!library.Enabled || GetLibraryConfiguration(library.Id).Revision != plan.LibraryRevision || MediaPaths.Key(library.Path) != MediaPaths.Key(Path.Combine(_paths.Data, "downloads", "live-library"))) throw new DomainException("Kayıt kütüphanesi değişti.", 409);
            _ = VerifiedFilePublication.FileName(plan.FileName); _ = VerifiedFilePublication.FileName(plan.PayloadName); _ = AppFiles.Id(plan.MediaId);
            using var db = Open();
            if (Scalar(db, "SELECT id FROM jobs WHERE library_id=$library AND status='running' LIMIT 1", ("$library", library.Id)) is string) return false;
            Execute(db, "UPDATE dvr_recordings SET status='publishing',publication_json=$json,message='Doğrulanan kayıt atomik yayımlanıyor.',updated_at=$now WHERE id=$id AND revision=$revision AND lease=$lease AND status='finalizing'",
                ("$id", work.Id), ("$revision", work.Revision), ("$lease", work.Lease), ("$json", JsonSerializer.Serialize(plan, WebJson)), ("$now", Now()));
            return GetDvr(work.Id)?.Status == "publishing";
        }
    }
    public DvrRecording CommitDvrPublication(DvrRecording work, Func<string> promote)
    {
        lock (_writeGate)
        {
            var current = GetDvr(work.Id) ?? throw new DomainException("Kayıt bulunamadı.", 404);
            if (current.Status is "completed" or "partial") return current;
            if (current.Status != "publishing" || current.Lease != work.Lease || current.Revision != work.Revision) throw new OperationCanceledException();
            var plan = GetDvrPublication(work.Id) ?? throw new DomainException("Yayın günlüğü bulunamadı.", 409);
            var library = GetLibraries(true).FirstOrDefault(value => value.Id == plan.LibraryId) ?? throw new DomainException("Kayıt kütüphanesi bulunamadı.", 404);
            if (MediaPaths.Key(library.Path) != MediaPaths.Key(Path.Combine(_paths.Data, "downloads", "live-library"))) throw new DomainException("Kayıt yayın kökü değişti.", 409);
            using var db = Open();
            if (Scalar(db, "SELECT id FROM media WHERE library_id=$library AND path_key=$path AND id<>$media", ("$library", plan.LibraryId), ("$path", MediaPaths.Key(plan.FileName)), ("$media", plan.MediaId)) is string)
                throw new DomainException("Kayıt adı başka bir katalog kimliğiyle çakışıyor.", 409);
            var promoted = promote(); var path = MediaPaths.ResolveFile(library.Path, plan.FileName);
            if (MediaPaths.Key(path) != MediaPaths.Key(promoted)) throw new DomainException("Kayıt yayın yolu uyuşmuyor.", 409);
            var info = new FileInfo(path); using var tx = db.BeginTransaction();
            MusicSql(db, tx, """
                INSERT INTO media(id,library_id,relative_path,path_key,title,search_text,kind,mime_type,size_bytes,modified_at,added_at,last_seen_scan,filesystem_id,duration_seconds,width,height,video_codec,audio_codec,minimum_age)
                VALUES($media,$library,$relative,$key,$title,$search,'video','video/mp2t',$size,$modified,$now,$scan,$fs,$duration,$width,$height,$video,$audio,$age)
                ON CONFLICT(id) DO NOTHING;
                INSERT INTO media_analysis(media_id,fingerprint,analysis_json,updated_at,schema_version) VALUES($media,$fingerprint,$probe,$now,2)
                ON CONFLICT(media_id) DO UPDATE SET fingerprint=$fingerprint,analysis_json=$probe,updated_at=$now,schema_version=2;
                UPDATE dvr_recordings SET status=$status,media_id=$media,bytes=$size,lease=NULL,message=$message,updated_at=$now WHERE id=$recording;
                """, ("$media", plan.MediaId), ("$library", plan.LibraryId), ("$relative", plan.FileName), ("$key", MediaPaths.Key(plan.FileName)), ("$title", work.Title), ("$search", MediaPaths.SearchKey(work.Title)),
                ("$size", info.Length), ("$modified", new DateTimeOffset(info.LastWriteTimeUtc).ToString("O")), ("$now", Now()), ("$scan", "dvr-" + work.Id), ("$fs", StableFileIdentity.Read(path)),
                ("$duration", plan.Probe.Duration), ("$width", plan.Probe.Width), ("$height", plan.Probe.Height), ("$video", plan.Probe.VideoCodec), ("$audio", plan.Probe.AudioCodec), ("$age", work.MinimumAge),
                ("$fingerprint", MediaAnalysisService.Fingerprint(info)), ("$probe", JsonSerializer.Serialize(plan.Probe, WebJson)), ("$recording", work.Id), ("$status", plan.Partial ? "partial" : "completed"),
                ("$message", plan.Partial ? "Alınabilen kayıt yayımlandı; kesinti veya geç başlangıç var." : "Kayıt doğrulandı ve kütüphaneye eklendi."));
            tx.Commit(); return GetDvr(work.Id)!;
        }
    }
    public void DvrPublicationError(DvrRecording work, string message)
    {
        lock (_writeGate) { using var db = Open(); Execute(db, "UPDATE dvr_recordings SET lease=NULL,message=$message,updated_at=$now WHERE id=$id AND status='publishing' AND revision=$revision AND lease=$lease",
            ("$id", work.Id), ("$revision", work.Revision), ("$lease", work.Lease), ("$message", message), ("$now", Now())); }
    }
}

using HomeServer.Core;
using HomeServer.Music;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public string? MusicRecordingForMedia(string id, Account viewer)
    {
        if (GetMedia(id, viewer) is not { Kind: "audio" }) throw new DomainException("Müzik parçası bulunamadı.", 404);
        using var db = Open(); return Scalar(db, "SELECT COALESCE(mm.recording_mbid,t.recording_mbid) FROM music_tracks t LEFT JOIN music_track_matches mm ON mm.media_id=t.media_id WHERE t.media_id=$id", ("$id", id)) as string;
    }
    public IReadOnlyList<MusicVideoLink> GetMusicVideos(string audioId, Account viewer, bool includePrevious = false)
    {
        var recording = MusicRecordingForMedia(audioId, viewer); using var db = Open();
        var rows = Query(db, "SELECT id,video_media_id,recording_mbid,video_recording_mbid,revision FROM music_video_links WHERE audio_media_id=$id AND (recording_mbid IS $recording OR $previous=1) ORDER BY created_at,id",
            r => (Id: r.GetString(0), Video: r.GetString(1), Recording: Text(r, 2), VideoRecording: Text(r, 3), Revision: r.GetInt32(4)), ("$id", audioId), ("$recording", recording), ("$previous", includePrevious && viewer.IsAdmin));
        return rows.Select(row => (row, media: GetMedia(row.Video, viewer))).Where(value => value.media is not null).Select(value => new MusicVideoLink(value.row.Id, value.media!, value.row.Recording, value.row.VideoRecording, value.row.Revision, value.row.Recording == recording)).ToArray();
    }
    public MusicVideoLink LinkMusicVideo(string audioId, MusicVideoInput input, Account actor)
    {
        lock (_writeGate)
        {
            if (GetAccount(actor.Id) is not { Enabled: true, IsAdmin: true } user) throw new DomainException("Müzik videosu eşlemesi yönetici gerektirir.", 403);
            var recording = MusicRecordingForMedia(audioId, user); var video = GetMedia(input.VideoId, user);
            if (recording != input.ExpectedRecordingMbid) throw new DomainException("Parça kimliği değişti; eşlemeyi yenile.", 409);
            if (video is not { Kind: "video" }) throw new DomainException("Yerel bir video dosyası seç.", 404);
            var videoRecording = input.VideoRecordingMbid is null ? null : MusicBrainzProvider.Id(input.VideoRecordingMbid);
            using var db = Open(); var old = Scalar(db, "SELECT id FROM music_video_links WHERE audio_media_id=$audio AND video_media_id=$video", ("$audio", audioId), ("$video", input.VideoId)) as string;
            if (old is null && Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM music_video_links WHERE audio_media_id=$id", ("$id", audioId))) >= 20) throw new DomainException("Bir parça en fazla 20 video bağlantısı içerebilir.");
            var id = old ?? Guid.NewGuid().ToString("N");
            Execute(db, "INSERT INTO music_video_links(id,audio_media_id,video_media_id,recording_mbid,video_recording_mbid,linked_by,created_at) VALUES($id,$audio,$video,$recording,$videoRecording,$user,$now) ON CONFLICT(id) DO UPDATE SET recording_mbid=$recording,video_recording_mbid=$videoRecording,linked_by=$user,revision=revision+1",
                ("$id", id), ("$audio", audioId), ("$video", input.VideoId), ("$recording", recording), ("$videoRecording", videoRecording), ("$user", actor.Id), ("$now", Now()));
            return GetMusicVideos(audioId, user).Single(link => link.Id == id);
        }
    }
    public void UnlinkMusicVideo(string id, int revision, Account actor)
    {
        lock (_writeGate)
        {
            if (GetAccount(actor.Id) is not { Enabled: true, IsAdmin: true }) throw new DomainException("Müzik videosu eşlemesi yönetici gerektirir.", 403);
            using var db = Open(); if (Execute(db, "DELETE FROM music_video_links WHERE id=$id AND revision=$revision", ("$id", id), ("$revision", revision)) == 0) throw new DomainException("Video eşlemesi bulunamadı veya değişti.", 409);
        }
    }
}

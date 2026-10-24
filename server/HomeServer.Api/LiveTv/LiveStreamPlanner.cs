using HomeServer.Core;

namespace HomeServer.LiveTv;

public static class LiveStreamPlanner
{
    public static LiveStreamPlan Decide(MediaTracks tracks, PlaybackClient client, bool forceCompatible, long budget)
    {
        var video = tracks.Streams.FirstOrDefault(stream => stream.Kind == "video");
        var audio = tracks.Streams.FirstOrDefault(stream => stream.Kind == "audio" && stream.IsDefault) ?? tracks.Streams.FirstOrDefault(stream => stream.Kind == "audio");
        var report = client?.Capabilities;
        if (client is null || !client.Hls || report?.Formats is not { Length: > 0 and <= 12 }
            || report.Formats.Any(format => format is null || format.Container is not ("mp4" or "ts") || format.Transport is not ("hls.js" or "native-hls")))
            throw new DomainException("Canlı yayın için geçerli tarayıcı yetenekleri gerekli.", 409);
        if (report.Fingerprint != tracks.Fingerprint || report.VideoStream != video?.Index || report.AudioStream != audio?.Index)
            throw new DomainException("Canlı yayın izleri veya oturumu değişti.", 409);
        if (video is null && audio is null) throw new DomainException("Kaynakta oynatılabilir görüntü veya ses bulunamadı.", 422);
        var candidates = new List<(PlaybackFormatSupport Format, bool Video, bool Audio)>();
        foreach (var format in report.Formats)
        {
            if (!forceCompatible && video?.DolbyVision != true)
            {
                if (format.Original) candidates.Add((format, true, true));
                if (format.VideoCopy) candidates.Add((format, true, audio is null));
            }
            if (!forceCompatible && format.AudioCopy) candidates.Add((format, video is null, true));
            if (format.Compatible) candidates.Add((format, video is null, audio is null));
        }
        if (candidates.Count == 0) throw new DomainException("Bu tarayıcı canlı yayının biçimini desteklemiyor.", 409);
        var selected = candidates.OrderByDescending(value => value.Video).ThenByDescending(value => value.Audio)
            .ThenBy(value => value.Format.Transport == "native-hls").ThenBy(value => value.Format.Container != "mp4").First();
        if (!selected.Video && video?.DolbyVision == true) throw new DomainException("Dolby Vision canlı dönüşümü bu profilde desteklenmiyor.", 409);
        var tone = !selected.Video && video?.ColorTransfer is "smpte2084" or "arib-std-b67";
        if(!selected.Video&&video?.ColorTransfer is null&&(video?.ColorPrimaries=="bt2020"||video?.ColorSpace is "bt2020nc" or "bt2020c"))
            throw new DomainException("Canlı HDR kaynağının renk bilgisi uygun değil.",409);
        if (tone && video?.ColorPrimaries != "bt2020") throw new DomainException("Canlı HDR kaynağının renk bilgisi uygun değil.", 409);
        var bitrate = selected.Video ? tracks.BitRate ?? video?.BitRate ?? 8_000_000 : 6_000_000;
        var segments = (int)Math.Clamp(budget * .35 * 8 / Math.Max(256000, bitrate) / 4, 6, 450);
        return new(video?.Index, audio?.Index, selected.Video, selected.Audio, selected.Format.Container, selected.Format.Transport,
            selected.Video ? selected.Audio ? "remux" : "audio-transcode" : "video-transcode", video is null ? null : selected.Video ? video.Codec : "h264",
            audio is null ? null : selected.Audio ? audio.Codec : "aac", segments, tone);
    }
}

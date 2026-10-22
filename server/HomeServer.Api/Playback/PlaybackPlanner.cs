using HomeServer.Core;

namespace HomeServer.Playback;

public static class PlaybackPlanner
{
    public static PlaybackDecision Decide(AnalyzedMedia media, PlaybackStart request, bool ffmpegAvailable, long sessionBudget, bool toneMappingAvailable = false, SubtitleTrack? selectedSubtitle = null)
    {
        var client = request.Client;
        if (client is null || client.Containers is null || client.VideoCodecs is null || client.AudioCodecs is null
            || client.Containers.Length > 20 || client.VideoCodecs.Length > 20 || client.AudioCodecs.Length > 20
            || client.Containers.Concat(client.VideoCodecs).Concat(client.AudioCodecs).Any(value => value is null || value.Length > 80)
            || client.MaxAudioChannels is < 1 or > 16 || request.MaxHeight is not (null or 480 or 720 or 1080 or 2160)
            || request.MaxBitrate is not null && request.MaxBitrate is < 256000 or > 100000000)
            throw new DomainException("İstemci oynatma profili geçersiz.");
        if (!double.IsFinite(request.StartSeconds) || request.StartSeconds < 0 || request.StartSeconds > 31_536_000)
            throw new DomainException("Oynatma konumu geçersiz.");
        if (!double.IsFinite(request.SubtitleDelaySeconds) || Math.Abs(request.SubtitleDelaySeconds) > 600 || request.SubtitleId?.Length > 100) throw new DomainException("Altyazı oynatma tercihi geçersiz.");
        if (request.SubtitleId is not null && selectedSubtitle?.Id != request.SubtitleId) throw new DomainException("Seçilen altyazı bu dosyada bulunamadı.", 404);
        var burn = selectedSubtitle is not null && (selectedSubtitle.Image || request.BurnSubtitle);
        if (burn && !selectedSubtitle!.Burnable) throw new DomainException("Seçilen altyazı görüntüye işlenemiyor.", 409);
        var streams = media.Probe.Streams ?? [];
        var container = Path.GetExtension(media.Path).TrimStart('.').ToLowerInvariant();
        if (streams.Length == 0 && !burn && !request.ForceCompatible && request.AudioStream is null && request.MaxHeight is null && request.MaxBitrate is null
            && client.Containers.Contains(container, StringComparer.OrdinalIgnoreCase))
            return new PlaybackDecision("direct", ["İz analizi yok; tarayıcı özgün dosyayı açmayı deneyecek."], null, null, media.Probe.Height, 0, true, true);
        var video = streams.FirstOrDefault(s => s.Kind == "video");
        var audios = streams.Where(s => s.Kind == "audio").ToArray();
        var defaultAudio = audios.OrderByDescending(s => s.IsDefault).FirstOrDefault();
        var audio = request.AudioStream is null ? defaultAudio : audios.FirstOrDefault(s => s.Index == request.AudioStream)
            ?? throw new DomainException("Seçilen ses izi bu dosyada yok.");
        if (video is null && audio is null) throw new DomainException("Oynatılabilir medya izi bulunamadı.", 422);
        var reasons = new List<string>();
        var videoSupported = video is null || client.VideoCodecs.Contains(video.Codec, StringComparer.OrdinalIgnoreCase)
            && (video.Codec != "h264" || video.PixelFormat is null or "yuv420p" or "yuvj420p");
        var audioSupported = audio is null || client.AudioCodecs.Contains(audio.Codec, StringComparer.OrdinalIgnoreCase) && (audio.Channels ?? 2) <= client.MaxAudioChannels;
        var tooTall = video?.Height > request.MaxHeight;
        var sourceBitrate = media.Probe.BitRate ?? (media.Probe.Duration is > 0 ? (long)(media.Media.SizeBytes * 8 / media.Probe.Duration.Value) : 0);
        var tooLarge = request.MaxBitrate is not null && sourceBitrate > request.MaxBitrate;
        if (!burn && !request.ForceCompatible && videoSupported && audioSupported && !tooTall && !tooLarge
            && audio?.Index == defaultAudio?.Index && client.Containers.Contains(container, StringComparer.OrdinalIgnoreCase))
            return new PlaybackDecision("direct", ["Dosyanın biçimi bu istemci tarafından destekleniyor."], video?.Index, audio?.Index, video?.Height, 0, true, true);
        if (!ffmpegAvailable) throw new DomainException("Bu dosya/iz seçimi için FFmpeg gerekiyor. Sunucu araçlarını yapılandır.", 409);
        if (!client.Hls) throw new DomainException("Bu tarayıcı gereken HLS oynatma yöntemini desteklemiyor.", 409);
        if (media.Probe.Duration is null or <= 0 || media.Probe.Duration > 24 * 3600) throw new DomainException("Dönüştürme için geçerli bir medya süresi gerekli (en fazla 24 saat).", 409);
        if (request.StartSeconds >= media.Probe.Duration) throw new DomainException("Oynatma konumu dosyanın sonunda veya dışında.");
        if (video is not null && !client.VideoCodecs.Contains("h264", StringComparer.OrdinalIgnoreCase)
            || audio is not null && !client.AudioCodecs.Contains("aac", StringComparer.OrdinalIgnoreCase))
            throw new DomainException("Bu istemci H.264/AAC uyumluluk profilini desteklemiyor.", 409);
        if (burn && video is null) throw new DomainException("Altyazı işlemek için video izi gerekli.", 409);
        var copyVideo = video is null || video.Codec == "h264" && videoSupported && !tooTall && !tooLarge && !request.ForceCompatible && !burn;
        var copyAudio = audio is null || audio.Codec == "aac" && audioSupported && (audio.Channels ?? 2) <= 2 && !request.ForceCompatible;
        var duration = Math.Max(1, media.Probe.Duration.Value - request.StartSeconds);
        var estimatedOriginal = sourceBitrate / 8d * duration * 1.15;
        if (copyVideo && video is not null && (sourceBitrate <= 0 || estimatedOriginal > sessionBudget * .8))
        {
            copyVideo = false;
            reasons.Add("Özgün akış cache bütçesini aşıyor; sınırlı bit hızı kullanılacak.");
        }
        if (!videoSupported) reasons.Add("Video codec'i veya piksel biçimi tarayıcıya uyarlanıyor.");
        if (!audioSupported || !copyAudio) reasons.Add("Ses AAC stereo biçimine uyarlanıyor.");
        if (audio?.Index != defaultAudio?.Index) reasons.Add("Seçilen ses izi kullanılıyor.");
        if (tooTall || tooLarge || request.ForceCompatible) reasons.Add("Seçtiğin uyumluluk/kalite sınırı uygulanıyor.");
        if (burn) reasons.Add(selectedSubtitle!.Image ? "Resim altyazısı videoya işleniyor." : "Altyazı stili videoya işleniyor.");
        var toneMap = video is not null && !copyVideo && video.ColorTransfer is "smpte2084" or "arib-std-b67";
        if (!copyVideo && video is not null && video.ColorTransfer is null && (video.ColorPrimaries == "bt2020" || video.ColorSpace is "bt2020nc" or "bt2020c"))
            throw new DomainException("Geniş renk alanlı videonun aktarım bilgisi bilinmiyor; otomatik renk dönüşümü yapılamıyor.", 409);
        if (!copyVideo && video?.DolbyVision == true) throw new DomainException("Dolby Vision dönüştürme bu profilde doğrulanmadı. Uyumlu özgün sürüm seç.", 409);
        if (toneMap && (!toneMappingAvailable || video!.ColorPrimaries != "bt2020"))
            throw new DomainException("HDR renk bilgisi veya tone mapping filtresi bu profilde uygun değil.", 409);
        if (toneMap) reasons.Add("HDR10/HLG, SDR BT.709 aralığına ton eşleniyor.");
        if (reasons.Count == 0) reasons.Add("Video ve ses korunarak HLS kapsayıcısına aktarılıyor.");
        var maximumBitrate = Math.Min(request.MaxBitrate ?? 8_000_000, (int)Math.Min(int.MaxValue, sessionBudget * .75 * 8 / duration));
        if (!copyVideo && maximumBitrate < 600_000) throw new DomainException("Bu süre için oynatma cache bütçesi yetersiz. Sunucu cache sınırını artır.", 409);
        return new PlaybackDecision(copyVideo ? copyAudio ? "remux" : "audio-transcode" : "video-transcode", reasons.ToArray(), video?.Index, audio?.Index,
            video is null ? null : Math.Min(video.Height ?? 1080, request.MaxHeight ?? 1080), maximumBitrate, copyVideo, copyAudio, toneMap);
    }
}

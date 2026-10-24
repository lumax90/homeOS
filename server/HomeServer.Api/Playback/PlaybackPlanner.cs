using HomeServer.Core;

namespace HomeServer.Playback;

public static class PlaybackPlanner
{
    public static PlaybackDecision Decide(AnalyzedMedia media, PlaybackStart request, bool ffmpegAvailable, long sessionBudget, bool toneMappingAvailable = false, SubtitleTrack? selectedSubtitle = null, bool completeFile = false)
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
        if (streams.Length == 0 && !burn && !request.ForceCompatible && !request.ForceRemux && request.AudioStream is null && request.MaxHeight is null && request.MaxBitrate is null
            && client.Containers.Contains(container, StringComparer.OrdinalIgnoreCase))
            return new PlaybackDecision("direct", ["İz analizi yok; tarayıcı özgün dosyayı açmayı deneyecek."], null, null, media.Probe.Height, 0, true, true);
        var video = streams.FirstOrDefault(s => s.Kind == "video");
        var audios = streams.Where(s => s.Kind == "audio").ToArray();
        var defaultAudio = audios.OrderByDescending(s => s.IsDefault).FirstOrDefault();
        var audio = request.AudioStream is null ? defaultAudio : audios.FirstOrDefault(s => s.Index == request.AudioStream)
            ?? throw new DomainException("Seçilen ses izi bu dosyada yok.");
        if (video is null && audio is null) throw new DomainException("Oynatılabilir medya izi bulunamadı.", 422);
        var capabilities = client.Capabilities;
        if (capabilities is not null)
        {
            if (capabilities.Formats is null || capabilities.Formats.Length > 12 || capabilities.Formats.Any(format => format is null
                || format.Container is not ("mp4" or "m4v" or "webm" or "mkv" or "mov" or "avi" or "mp3" or "m4a" or "flac" or "ogg" or "opus" or "wav" or "ts")
                || format.Transport is not ("direct" or "hls.js" or "native-hls")
                || format.Transport != "direct" && format.Container is not ("mp4" or "ts")))
                throw new DomainException("İstemci oynatma profili geçersiz.");
            if (capabilities.Fingerprint != media.Fingerprint || capabilities.VideoStream != video?.Index || capabilities.AudioStream != audio?.Index)
                throw new DomainException("Dosya veya seçilen izler değişti. Oynatmayı yeniden başlat.", 409);
        }
        var reasons = new List<string>();
        var videoSupported = video is null || client.VideoCodecs.Contains(video.Codec, StringComparer.OrdinalIgnoreCase)
            && (video.Codec != "h264" || video.PixelFormat is null or "yuv420p" or "yuvj420p");
        var audioSupported = audio is null || client.AudioCodecs.Contains(audio.Codec, StringComparer.OrdinalIgnoreCase) && (audio.Channels ?? 2) <= client.MaxAudioChannels;
        var tooTall = video?.Height > request.MaxHeight;
        var sourceBitrate = media.Probe.BitRate ?? (media.Probe.Duration is > 0 ? (long)(media.Media.SizeBytes * 8 / media.Probe.Duration.Value) : 0);
        var tooLarge = request.MaxBitrate is not null && sourceBitrate > request.MaxBitrate;
        var directSupported = capabilities is null ? videoSupported && audioSupported && client.Containers.Contains(container, StringComparer.OrdinalIgnoreCase)
            : capabilities.Formats.Any(format => format.Transport == "direct" && format.Container == container && format.Original);
        if (!burn && !request.ForceCompatible && !request.ForceRemux && directSupported && !tooTall && !tooLarge && audio?.Index == defaultAudio?.Index)
            return new PlaybackDecision("direct", ["Dosyanın biçimi bu istemci tarafından destekleniyor."], video?.Index, audio?.Index, video?.Height, 0, true, true,
                Container: container, Transport: "direct", VideoCodec: video?.Codec, AudioCodec: audio?.Codec);
        if (!ffmpegAvailable) throw new DomainException("Bu dosya/iz seçimi için FFmpeg gerekiyor. Sunucu araçlarını yapılandır.", 409);
        if (!client.Hls) throw new DomainException("Bu tarayıcı gereken HLS oynatma yöntemini desteklemiyor.", 409);
        if (media.Probe.Duration is null or <= 0 || media.Probe.Duration > 24 * 3600) throw new DomainException("Dönüştürme için geçerli bir medya süresi gerekli (en fazla 24 saat).", 409);
        if (request.StartSeconds >= media.Probe.Duration) throw new DomainException("Oynatma konumu dosyanın sonunda veya dışında.");
        if (burn && video is null) throw new DomainException("Altyazı işlemek için video izi gerekli.", 409);
        var formats = capabilities?.Formats.Where(format => format.Transport != "direct").ToArray() ?? [new PlaybackFormatSupport("ts", "legacy",
            (video is null || video.Codec == "h264" && videoSupported) && (audio is null || audio.Codec == "aac" && audioSupported && (audio.Channels ?? 2) <= 2),
            (video is null || video.Codec == "h264" && videoSupported) && (audio is null || client.AudioCodecs.Contains("aac")),
            (video is null || client.VideoCodecs.Contains("h264")) && (audio is null || audio.Codec == "aac" && audioSupported && (audio.Channels ?? 2) <= 2),
            (video is null || client.VideoCodecs.Contains("h264")) && (audio is null || client.AudioCodecs.Contains("aac")))];
        var choices = new List<(PlaybackFormatSupport Format, bool Video, bool Audio)>();
        foreach (var format in formats)
        {
            var mayCopyVideo = !tooTall && !tooLarge && !request.ForceCompatible && !burn && video?.DolbyVision != true;
            if (mayCopyVideo && format.Original) choices.Add((format, true, true));
            if (mayCopyVideo && format.VideoCopy) choices.Add((format, true, audio is null));
            if (!request.ForceCompatible && format.AudioCopy) choices.Add((format, video is null, true));
            if (format.Compatible) choices.Add((format, video is null, audio is null));
        }
        if (choices.Count == 0) throw new DomainException("Bu tarayıcı için uyumlu bir görüntü/ses taşıma yolu bulunamadı.", 409);
        var choice = choices.OrderByDescending(value => value.Video).ThenByDescending(value => value.Audio)
            .ThenBy(value => value.Format.Transport == "native-hls").ThenBy(value => value.Format.Container != "mp4").First();
        var copyVideo = choice.Video; var copyAudio = choice.Audio;
        var duration = Math.Max(1, media.Probe.Duration.Value - request.StartSeconds);
        if (copyVideo && video is not null) reasons.Add("Özgün görüntü yeniden kodlanmadan korunuyor.");
        else if (video is not null && !tooTall && !tooLarge && !request.ForceCompatible && !burn) reasons.Add("Kaynak görüntü bu tarayıcının taşıma profilinde desteklenmiyor; H.264'e uyarlanıyor.");
        if (!copyAudio && audio is not null) reasons.Add("Ses AAC stereo biçimine uyarlanıyor.");
        else if (audio is not null) reasons.Add("Özgün ses korunuyor.");
        if (audio?.Index != defaultAudio?.Index) reasons.Add("Seçilen ses izi kullanılıyor.");
        if (tooTall || tooLarge || request.ForceCompatible) reasons.Add("Seçtiğin uyumluluk/kalite sınırı uygulanıyor.");
        if (request.ForceRemux) reasons.Add("Doğrudan dosya yerine uyumlu akış kapsayıcısı kullanılıyor.");
        if (burn) reasons.Add(selectedSubtitle!.Image ? "Resim altyazısı videoya işleniyor." : "Altyazı stili videoya işleniyor.");
        var toneMap = video is not null && !copyVideo && video.ColorTransfer is "smpte2084" or "arib-std-b67";
        if (!copyVideo && video is not null && video.ColorTransfer is null && (video.ColorPrimaries == "bt2020" || video.ColorSpace is "bt2020nc" or "bt2020c"))
            throw new DomainException("Geniş renk alanlı videonun aktarım bilgisi bilinmiyor; otomatik renk dönüşümü yapılamıyor.", 409);
        if (!copyVideo && video?.DolbyVision == true) throw new DomainException("Dolby Vision dönüştürme bu profilde doğrulanmadı. Uyumlu özgün sürüm seç.", 409);
        if (toneMap && (!toneMappingAvailable || video!.ColorPrimaries != "bt2020"))
            throw new DomainException("HDR renk bilgisi veya tone mapping filtresi bu profilde uygun değil.", 409);
        if (toneMap) reasons.Add("HDR10/HLG, SDR BT.709 aralığına ton eşleniyor.");
        var maximumBitrate = request.MaxBitrate ?? 8_000_000;
        if (completeFile) maximumBitrate = Math.Min(maximumBitrate, (int)Math.Min(int.MaxValue, sessionBudget * .75 * 8 / duration));
        if (!copyVideo && maximumBitrate < 600_000) throw new DomainException("Bu süre için oynatma cache bütçesi yetersiz. Sunucu cache sınırını artır.", 409);
        return new PlaybackDecision(copyVideo ? copyAudio ? "remux" : "audio-transcode" : "video-transcode", reasons.ToArray(), video?.Index, audio?.Index,
            video is null ? null : copyVideo ? video.Height : Math.Min(video.Height ?? 1080, request.MaxHeight ?? 1080), maximumBitrate, copyVideo, copyAudio, toneMap,
            Container: choice.Format.Container, Transport: choice.Format.Transport == "legacy" ? null : choice.Format.Transport,
            VideoCodec: video is null ? null : copyVideo ? video.Codec : "h264", AudioCodec: audio is null ? null : copyAudio ? audio.Codec : "aac");
    }
}

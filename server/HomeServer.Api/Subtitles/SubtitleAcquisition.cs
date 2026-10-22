using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using HomeServer.Playback;

namespace HomeServer.Subtitles;

public sealed class SubtitleAcquisition(HomePaths paths, MediaStore store, MediaAnalysisService analysis, OpenSubtitlesProvider provider,
    SubtitleService subtitles, SubtitleSettings settings, EventHub events)
{
    private readonly SemaphoreSlim _downloads = new(1, 1);
    public async Task<SubtitleSearchPage> SearchAsync(string mediaId, SubtitleSearchInput input, CancellationToken token)
    {
        var connectionRevision = settings.ConnectionRevision;
        var media = await analysis.InspectAsync(mediaId, token, allowUnprobed: true);
        if (media.Media.Kind != "video") throw new DomainException("Altyazı için video seç.");
        var content = media.Media.Content is { } match ? store.GetContent(match.Id)?.Metadata : null;
        var page = await provider.SearchAsync(media, content, input, token);
        return page with { Items = page.Items.Select(candidate => store.SaveSubtitleCandidate(candidate, media, connectionRevision)).ToArray() };
    }
    public async Task<ManagedSubtitle> AcquireAsync(string mediaId, string candidateId, CancellationToken token, int? profileRevision = null, SubtitleLease? lease = null, string? replaceId = null)
    {
        await _downloads.WaitAsync(token);
        try
        {
            var saved = store.GetSubtitleCandidate(candidateId);
            if (saved.Candidate.MediaId != mediaId) throw new DomainException("Altyazı seçimi bu medyaya ait değil.", 404);
            var media = await analysis.InspectAsync(mediaId, token, allowUnprobed: true);
            if (media.Fingerprint != saved.Fingerprint || media.Media.Content?.Id != saved.ContentId || media.Media.MatchRevision != saved.MatchRevision || settings.ConnectionRevision != saved.ConnectionRevision) throw new DomainException("Dosya, eşleştirme veya bağlantı değişti; altyazıları yeniden ara.", 409);
            if (!saved.Candidate.Eligible) throw new DomainException("Altyazı kimliği veya dil profili eşleşmiyor.", 409);
            var existing = store.GetManagedSubtitles(mediaId, true).FirstOrDefault(item => item.Id == saved.ManagedId);
            if (existing is { Retired: true }) throw new DomainException("Bu altyazı geçmişte var; sürüm geçmişinden geri alabilirsin.", 409);
            if (existing is not null) { store.CompleteSubtitleDownload(candidateId); return existing; }
            SubtitlePolicyStamp? stamp = null;
            if (profileRevision is not null || lease is not null)
            {
                var effective = store.GetEffectiveSubtitlePolicy(mediaId);
                if (profileRevision is not null && effective.Stamp.GlobalRevision != profileRevision) throw new DomainException("Altyazı profili değişti.", 409);
                stamp = lease?.Stamp ?? effective.Stamp;
            }
            var publication = new SubtitlePublication("provider", media.Fingerprint, media.Media.MatchRevision, saved.Candidate.FileId, saved.Candidate.Score, replaceId, stamp, lease, saved.ConnectionRevision);
            store.ValidateSubtitlePublication(mediaId, saved.Candidate.Language, publication);
            var directory = AppFiles.DirectoryIn(paths.State, "subtitle-payloads");
            var payload = Path.Combine(directory, AppFiles.Id(candidateId) + ".srt");
            if (!File.Exists(payload))
            {
                if (DateTimeOffset.Parse(saved.ExpiresAt) < DateTimeOffset.UtcNow) throw new DomainException("Altyazı sonucu eskidi; aramayı yenile.", 409);
                string ticket;
                if (saved.ProtectedTicket is not null && DateTimeOffset.TryParse(saved.TicketExpiresAt, out var expires) && expires > DateTimeOffset.UtcNow) ticket = settings.UnprotectTicket(saved.ProtectedTicket);
                else
                {
                    store.ReserveSubtitleDownload(saved, publication);
                    ticket = await provider.RequestTicketAsync(saved.Candidate.FileId, token);
                    store.SaveSubtitleTicket(candidateId, saved.ConnectionRevision, settings.ProtectTicket(ticket));
                }
                var bytes = await provider.DownloadTicketAsync(ticket, token);
                _ = SubtitleService.DecodeText(bytes);
                var temporary = payload + ".part";
                await File.WriteAllBytesAsync(temporary, bytes, token);
                File.Move(temporary, payload, true);
            }
            await using var source = File.OpenRead(MediaPaths.ResolveFile(directory, Path.GetFileName(payload)));
            var contentBytes = await MetadataProvider.ReadLimitedAsync(source, 2 * 1024 * 1024, token);
            var name = media.Media.Title[..Math.Min(media.Media.Title.Length, 120)] + "." + saved.Candidate.Language + ".srt";
            var result = await subtitles.UploadAsync(mediaId, saved.Candidate.Language, name, "srt", contentBytes, token, saved.ManagedId,
                saved.Candidate.Forced, saved.Candidate.HearingImpaired, profileRevision, saved.MatchRevision, publication);
            store.CompleteSubtitleDownload(candidateId);
            source.Close(); File.Delete(payload);
            events.Publish("tracks.changed"); events.Publish("subtitles.changed");
            return result;
        }
        catch (HttpRequestException) { throw new DomainException("Altyazı dosyasına bağlanılamadı.", 502); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DomainException("Altyazı indirme isteği zaman aşımına uğradı.", 504); }
        finally { _downloads.Release(); }
    }
}

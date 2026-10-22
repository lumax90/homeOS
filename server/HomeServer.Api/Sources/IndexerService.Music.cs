using System.Collections.Concurrent;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Music;
using MonoTorrent;
using Microsoft.AspNetCore.DataProtection;

namespace HomeServer.Sources;

public sealed partial class IndexerService
{
    public async Task<MusicSearchResult> SearchMusicAsync(MusicSearchInput input, MusicBrainzProvider metadata, CancellationToken token)
    {
        MusicReleaseEvaluator.Validate(input.Rules); if (input.Page is < 0 or > 20) throw new DomainException("Müzik arama sayfası geçersiz.");
        var target = await metadata.ReleaseAsync(input.ReleaseId, token); var sources = store.GetIndexers().Where(source => source.Enabled).ToArray();
        if (sources.Length == 0) throw new DomainException("Albüm sürümlerini aramak için Kaynaklar bölümünden bir site seçip bağla.", 409);
        if (!await _searches.WaitAsync(0, token)) throw new DomainException("Kaynak arama sınırı dolu.", 429);
        try
        {
            var results = new ConcurrentBag<MusicSourceResult>(); var outcomes = new ConcurrentBag<SearchSourceOutcome>();
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(60));
            await Parallel.ForEachAsync(sources, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (info, _) =>
            {
                try
                {
                    var source = store.GetIndexer(info.Id); if (source.Info.Capabilities is null) { await TestAsync(info.Id, budget.Token); source = store.GetIndexer(info.Id); }
                    var caps = source.Info.Capabilities!; var parameters = new Dictionary<string, string?>();
                    if (caps.Searches.TryGetValue("music-search", out var supported))
                    {
                        parameters["t"] = "music";
                        if (supported.Contains("artist")) parameters["artist"] = target.Metadata.Artist;
                        if (supported.Contains("album")) parameters["album"] = target.Metadata.Title;
                        if (supported.Contains("q")) parameters["q"] = target.Metadata.Artist + " " + target.Metadata.Title;
                        if (!parameters.ContainsKey("q") && (!parameters.ContainsKey("artist") || !parameters.ContainsKey("album"))) throw new DomainException("Kaynak sanatçı/albüm aramasını desteklemiyor.");
                    }
                    else if (caps.Searches.TryGetValue("search", out var general) && general.Contains("q")) { parameters["t"] = "search"; parameters["q"] = target.Metadata.Artist + " " + target.Metadata.Title; }
                    else throw new DomainException("Kaynak müzik arama yeteneği bildirmedi.");
                    var limit = Math.Min(caps.MaxPageSize, 100); parameters["limit"] = limit.ToString(); parameters["offset"] = (input.Page * limit).ToString(); parameters["extended"] = "1";
                    if (source.Info.Categories.Length > 0) parameters["cat"] = source.Info.Categories;
                    var response = await ReadReleasesAsync(source,parameters,budget.Token);
                    foreach (var release in response.Items)
                        results.Add(store.SaveMusicSource(source.Info, release, target, input.Rules, MusicReleaseEvaluator.Evaluate(release, target, input.Rules), _protector.Protect(release.Url)));
                    outcomes.Add(new SearchSourceOutcome(info.Id, info.Name, response.Items.Count, null, response.More));
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { outcomes.Add(new SearchSourceOutcome(info.Id, info.Name, 0, "Arama süresi doldu.")); }
                catch (DomainException ex) { outcomes.Add(new SearchSourceOutcome(info.Id, info.Name, 0, ex.Message)); }
            });
            return new MusicSearchResult(results.OrderByDescending(result => result.Evaluation.Eligible).ThenByDescending(result => result.Evaluation.Score).ToArray(), outcomes.ToArray());
        }
        finally { _searches.Release(); }
    }
    public Task<DownloadRecord> GrabMusicAsync(string id, CancellationToken token, MusicAutomationLease? automation = null)
        => GrabMusicForAccountAsync(id, null, token, automation);
    public async Task<DownloadRecord> GrabMusicForAccountAsync(string id, DownloadPrincipal? principal, CancellationToken token, MusicAutomationLease? automation = null)
    {
        await _grabs.WaitAsync(token);
        try
        {
            var saved = store.GetMusicSource(id);
            if (principal is not null) store.RequireDownloadPrincipal(principal);
            if (saved.Result.DownloadId is { } previous) return principal is null ? store.GetDownload(previous)! : store.RequireDownloadAccess(previous, store.RequireDownloadPrincipal(principal));
            if (!saved.Result.Evaluation.Eligible || DateTimeOffset.Parse(saved.ExpiresAt) <= DateTimeOffset.UtcNow) throw new DomainException("Müzik sürümü profile uymuyor veya arama sonucu eskidi.", 409);
            var source = store.GetIndexer(saved.Result.IndexerId); if (!source.Info.Enabled) throw new DomainException("Kaynak devre dışı.", 409);
            var url = Unprotect(saved.ProtectedUrl);
            var rules = saved.Rules; MusicAudioQuality? floor = null;
            if (automation is not null)
            {
                var quality = saved.Result.Evaluation.Quality ?? throw new DomainException("Otomatik müzik kalitesi doğrulanamadı.", 409);
                rules = rules with { Codecs = [quality.Codec], LosslessOnly = quality.Tier >= 2, MinimumBitrateKbps = Math.Max(rules.MinimumBitrateKbps, quality.Tier == 1 ? quality.BitrateKbps : 0),
                    MinimumBitDepth = Math.Max(rules.MinimumBitDepth, quality.BitDepth), MinimumSampleRate = Math.Max(rules.MinimumSampleRate, quality.SampleRate) };
                floor = store.MusicTarget(saved.Release).Quality;
            }
            var context = new MusicDownloadContext(id, saved.Release, rules, automation, floor); DownloadRecord download;
            principal = store.ResolveDownloadPrincipal(principal, music: context); store.CheckDownloadAdmission(principal, DateTimeOffset.UtcNow);
            var payload = url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ? (Data: (byte[]?)null, Magnet: (string?)url) : await FetchReleaseAsync(source, url, token);
            token.ThrowIfCancellationRequested();
            if (saved.Result.Protocol == "usenet")
            {
                if (nzb is null || payload.Data is null) throw new DomainException("Usenet NZB motoru veya payload eksik.", 409);
                download = nzb.AddNzb(payload.Data, name: saved.Result.Title, music: context, principal: principal);
            }
            else if (payload.Magnet is { } magnet)
            {
                if (!MagnetLink.TryParse(magnet, out var link)) throw new DomainException("Geçerli magnet bulunamadı.", 502);
                VerifyHash(saved.InfoHash, TorrentEngine.Hashes(link.InfoHashes)); download = downloads.AddMagnet(magnet, music: context, principal: principal);
            }
            else
            {
                var torrent = TorrentEngine.Parse(payload.Data!); VerifyHash(saved.InfoHash, TorrentEngine.Hashes(torrent.InfoHashes)); download = downloads.AddTorrent(payload.Data!, music: context, principal: principal);
            }
            events.Publish("downloads.changed"); return download;
        }
        finally { _grabs.Release(); }
    }
}

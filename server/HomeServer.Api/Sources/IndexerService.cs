using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using HomeServer.Metadata;
using HomeServer.Usenet;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using MonoTorrent;

namespace HomeServer.Sources;

public sealed partial class IndexerService(MediaStore store, IHttpClientFactory clients, IDataProtectionProvider protection,
    TorrentEngine downloads, EventHub events, NzbEngine? nzb = null, SourceTransportService? network = null) : IReleaseAcquisition, Music.IMusicAcquisition
{
    private readonly IDataProtector _protector = protection.CreateProtector("Home.Indexers.v1");
    private readonly SourceTransportService _network=network??new SourceTransportService(store,clients,protection);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _next = new();
    private readonly ConcurrentDictionary<string,(string Cursor,DateTimeOffset Expires)> _adapterCursors = new();
    private readonly SemaphoreSlim _grabs = new(1, 1);
    private readonly SemaphoreSlim _searches = new(2, 2);

    public IReadOnlyList<IndexerInfo> List() => store.GetIndexers().Select(info => Public(store.GetIndexer(info.Id))).ToArray();
    public void PruneRuntime(IReadOnlySet<string> ids)
    {
        foreach(var id in _next.Keys.Where(id=>!ids.Contains(id)).ToArray())_next.TryRemove(id,out _);
        foreach(var id in _gates.Keys.Where(id=>!ids.Contains(id)).ToArray())_gates.TryRemove(id,out _);
        foreach(var key in _adapterCursors.Keys.Where(key=>!ids.Contains(key.Split(':',2)[0])).ToArray())_adapterCursors.TryRemove(key,out _);
    }
    private IndexerInfo Public(IndexerSecret source)
    {
        try { return source.Info with { Configured = !string.IsNullOrWhiteSpace(_protector.Unprotect(source.Secret)) }; }
        catch (CryptographicException) { return source.Info with { Configured = false, LastError = "Kaynak anahtarı okunamadı; yeniden kaydet." }; }
    }

    public IndexerInfo Save(string? id, IndexerInput input)
    {
        input = ValidateInput(input);
        if (id is null && store.GetIndexers().Count >= 30) throw new DomainException("En fazla 30 kaynak bağlanabilir.");
        if (id is not null) store.RequirePublishedIndexer(id);
        var secret = input.ApiKey is null && id is not null ? store.GetIndexer(id).Secret : ProtectKey(input.ApiKey?.Trim() ?? "");
        var saved = store.SaveIndexer(id, input, secret);
        return Public(store.GetIndexer(saved.Id));
    }
    public string ProtectKey(string value) => _protector.Protect(value);
    public string ReadKey(string id) => Unprotect(store.GetIndexer(id).Secret);
    public static IndexerInput ValidateInput(IndexerInput input)
    {
        input = input with { Categories = input.Categories ?? "", AllowedHosts = input.AllowedHosts ?? "" };
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 80 || input.Protocol is not ("torznab" or "newznab")
            || !Uri.TryCreate(input.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || input.Endpoint.Length > 2000)
            throw new DomainException("Kaynak adı ve API adresini kontrol et. Anahtarı ayrı alana gir.");
        if (input.Priority is < 1 or > 50) throw new DomainException("Kaynak önceliği 1–50 arasında olmalı.");
        if (input.Categories.Length > 1000 || input.Categories.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(c => !int.TryParse(c, out var n) || n < 0))
            throw new DomainException("Kategoriler virgülle ayrılmış sayılar olmalı.");
        if (input.AllowedHosts.Length > 1000 || input.AllowedHosts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(h => Uri.CheckHostName(h) == UriHostNameType.Unknown))
            throw new DomainException("İndirme alan adları geçerli değil.");
        if (input.ApiKey?.Length > 4096 || input.ApiKey?.Any(char.IsControl)==true) throw new DomainException("API anahtarı geçersiz veya çok uzun.");
        return input with { Name=input.Name.Trim(), Endpoint = uri.AbsoluteUri };
    }

    public async Task<IndexerInfo> TestAsync(string id, CancellationToken token)
    {
        var source = store.GetIndexer(id);
        try
        {
            if(_network.Read(id).AdapterId is{}adapterId)
            {
                var manifest=SourceAdapterRegistry.Find(adapterId,store);_=await ReadReleasesAsync(source,SourceAdapterRegistry.ProbeParameters(manifest),token);store.RequireIndexerSnapshot(source.Info);store.SetIndexerHealth(id,SourceAdapterRegistry.Capabilities(manifest),null,expectedRevision:source.Info.Revision);return Public(store.GetIndexer(id));
            }
            var data = await QueryAsync(source, new Dictionary<string, string?> { ["t"] = "caps" }, token);
            var caps = ZnabParser.Capabilities(data);
            if (caps.Searches.Count == 0) throw new DomainException("Kaynak desteklenen bir arama yeteneği bildirmedi.", 502);
            store.RequireIndexerSnapshot(source.Info);store.SetIndexerHealth(id, caps, null,expectedRevision:source.Info.Revision);
            return Public(store.GetIndexer(id));
        }
        catch (DomainException ex) { store.SetIndexerHealth(id, null, ex.Message,expectedRevision:source.Info.Revision); throw; }
    }
    public async Task<IndexerInfo> TestSetupAsync(string id,CancellationToken token)
    {
        var result=await TestAsync(id,token);
        if(_network.Read(id).AdapterId is null)
        {
            var source=store.GetIndexer(id);
            if(!source.Info.Capabilities!.Searches.TryGetValue("search",out var parameters)||!parameters.Contains("q",StringComparer.OrdinalIgnoreCase))throw new DomainException("Kaynak bağlantısı genel arama yeteneği bildirmedi.",409);
            _=await ReadReleasesAsync(source,new(){["t"]="search",["q"]="Big Buck Bunny",["limit"]="1",["offset"]="0"},token);
            store.RequireIndexerSnapshot(source.Info);store.SetIndexerHealth(id,null,null,expectedRevision:source.Info.Revision);
        }
        return result;
    }

    public async Task<ReleaseSearchResult> SearchAsync(ReleaseSearchInput input, CancellationToken token, QualityProfile? profileOverride = null)
    {
        var target = store.GetContent(input.ContentId) ?? throw new DomainException("İçerik önce takip listesine eklenmeli.", 404);
        input = input with { ContentId = target.Id };
        if (input.Season is < 0 or > 1000 || input.Episode is < 1 or > 10000 || input.Page is < 0 or > 20
            || input.Episode is not null && input.Season is null || target.Metadata.Kind == "movie" && (input.Season is not null || input.Episode is not null))
            throw new DomainException("Arama kapsamı geçersiz.");
        var profile = profileOverride ?? store.GetQualityProfile(input.ProfileId ?? target.QualityProfileId);
        var identity = store.ReleaseIdentity(target.Id, input.Season, input.Episode);
        var sources = store.GetIndexers().Where(i => i.Enabled).ToArray();
        if (sources.Length == 0) throw new DomainException("İsteğin hazır. Sürüm bulmak için Kaynaklar bölümünden bir site seçip bağla.", 409);
        if (!await _searches.WaitAsync(0, token)) throw new DomainException("İki kaynak araması zaten çalışıyor. Tamamlanmasını bekle.", 429);
        try
        {
            store.PruneReleaseResults();
            var results = new ConcurrentBag<ReleaseResult>();
            var outcomes = new ConcurrentBag<SearchSourceOutcome>();
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromSeconds(60));
            await Parallel.ForEachAsync(sources, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (info, _) =>
            {
                try
                {
                    var source = store.GetIndexer(info.Id);
                    if (source.Info.Capabilities is null) { await TestAsync(info.Id, budget.Token); source = store.GetIndexer(info.Id); }
                    var caps = source.Info.Capabilities!;
                    var parameters = Parameters(target.Metadata, caps, input, identity);
                    var limit = Math.Min(caps.MaxPageSize, 100);
                    parameters["limit"] = limit.ToString(CultureInfo.InvariantCulture);
                    parameters["offset"] = (input.Page * limit).ToString(CultureInfo.InvariantCulture);
                    parameters["extended"] = "1";
                    if (source.Info.Categories.Length > 0) parameters["cat"] = source.Info.Categories;
                    var parsed = await ReadReleasesAsync(source,parameters,budget.Token);
                    foreach (var release in parsed.Items)
                    {
                        budget.Token.ThrowIfCancellationRequested();
                        var evaluation = ReleaseEvaluator.Evaluate(release, target.Metadata, profile.Rules, input.Season, input.Episode, identity);
                        results.Add(store.SaveRelease(source.Info, target, input, profile, release, evaluation, _protector.Protect(release.Url)));
                    }
                    store.SetIndexerHealth(info.Id, null, null,expectedRevision:source.Info.Revision);
                    outcomes.Add(new SearchSourceOutcome(info.Id, info.Name, parsed.Items.Count, null, parsed.More));
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { outcomes.Add(new SearchSourceOutcome(info.Id, info.Name, 0, "Arama süresi doldu; bu kaynak tamamlanamadı.")); }
                catch (DomainException ex)
                {
                    store.SetIndexerHealth(info.Id, null, ex.Message,expectedRevision:info.Revision);
                    outcomes.Add(new SearchSourceOutcome(info.Id, info.Name, 0, ex.Message));
                }
            });
            return new ReleaseSearchResult(results.OrderByDescending(r => r.Evaluation.Eligible).ThenByDescending(r => r.Evaluation.Score)
                .ThenBy(r => sources.First(s => s.Id == r.IndexerId).Priority).ToArray(), outcomes.OrderBy(s => s.Name).ToArray());
        }
        finally { _searches.Release(); }
    }

    public Task<DownloadRecord> GrabAsync(string id, bool allowRejected, CancellationToken token, AutomationLease? automation = null)
        => GrabForAccountAsync(id, allowRejected, null, token, automation);
    public async Task<DownloadRecord> GrabForAccountAsync(string id, bool allowRejected, DownloadPrincipal? principal, CancellationToken token, AutomationLease? automation = null)
    {
        await _grabs.WaitAsync(token);
        try
        {
            var saved = store.GetRelease(id);
            if (principal is not null) store.RequireDownloadPrincipal(principal);
            if (saved.Result.DownloadId is { } existing) return principal is null ? store.GetDownload(existing) ?? throw new DomainException("İndirme kaydı bulunamadı.", 404) : store.RequireDownloadAccess(existing, store.RequireDownloadPrincipal(principal));
            store.RequireCurrentRelease(id);
            if (DateTimeOffset.Parse(saved.ExpiresAt, CultureInfo.InvariantCulture) < DateTimeOffset.UtcNow)
                throw new DomainException("Arama sonucu eskidi. Kaynakları yeniden ara.", 409);
            if (!saved.Result.Evaluation.Eligible && !allowRejected) throw new DomainException("Bu sürüm profile uymuyor. Ret nedenlerini incele.", 409);
            if (saved.Result.Protocol is not ("torrent" or "usenet")) throw new DomainException("Kaynak aktarım protokolü desteklenmiyor.", 409);
            if (saved.Result.Protocol == "usenet" && nzb is null) throw new DomainException("Usenet motoru yapılandırılmadı.", 409);
            var source = store.GetIndexer(saved.Result.IndexerId);
            if (!source.Info.Enabled) throw new DomainException("Bu kaynak devre dışı.", 409);
            var url = Unprotect(saved.ProtectedUrl);
            var context = new AcquisitionContext(saved.ContentId, saved.Season, saved.Episode, id, automation);
            principal = store.ResolveDownloadPrincipal(principal, context); store.CheckDownloadAdmission(principal, DateTimeOffset.UtcNow);
            var payload = url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ? (Data: (byte[]?)null, Magnet: (string?)url) : await FetchReleaseAsync(source, url, token);
            token.ThrowIfCancellationRequested();
            DownloadRecord result;
            if (saved.Result.Protocol == "usenet")
            {
                if (payload.Data is null) throw new DomainException("Usenet kaynağı bir NZB belgesi döndürmedi.", 502);
                result = nzb!.AddNzb(payload.Data, context, saved.Result.Title, principal: principal);
            }
            else if (payload.Magnet is { } magnet)
            {
                if (!MagnetLink.TryParse(magnet, out var parsed)) throw new DomainException("Kaynak geçerli bir magnet döndürmedi.", 502);
                VerifyHash(saved.InfoHash, TorrentEngine.Hashes(parsed.InfoHashes));
                result = downloads.AddMagnet(magnet, context, principal: principal);
            }
            else
            {
                var torrent = TorrentEngine.Parse(payload.Data!);
                VerifyHash(saved.InfoHash, TorrentEngine.Hashes(torrent.InfoHashes));
                result = downloads.AddTorrent(payload.Data!, context, principal: principal);
            }
            events.Publish("downloads.changed");
            return result;
        }
        finally { _grabs.Release(); }
    }

    private static void VerifyHash(string? expected, string[] actual)
    {
        if (expected is not null && !actual.Contains(expected, StringComparer.OrdinalIgnoreCase))
            throw new DomainException("Kaynağın verdiği torrent kimliği arama sonucuyla eşleşmedi.", 502);
    }

    private async Task<byte[]> QueryAsync(IndexerSecret source, Dictionary<string, string?> parameters, CancellationToken token)
    {
        var gate = _gates.GetOrAdd(source.Info.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            await WaitForSourceAsync(source.Info.Id, token);
            var secret = Unprotect(source.Secret);
            if (secret.Length > 0) parameters["apikey"] = secret;
            var url = QueryHelpers.AddQueryString(source.Info.Endpoint, parameters);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await _network.SendAsync(source,url,"query",timeout.Token);
            CheckResponse(source.Info, response);
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            var bytes = await MetadataProvider.ReadLimitedAsync(body, 8 * 1024 * 1024, timeout.Token);store.RequireIndexerSnapshot(source.Info);return bytes;
        }
        catch (HttpRequestException) { throw new DomainException("Kaynağa bağlanılamadı.", 502); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DomainException("Kaynak isteği zaman aşımına uğradı.", 504); }
        finally { gate.Release(); }
    }

    private async Task WaitForSourceAsync(string id, CancellationToken token)
    {
        var current = store.GetIndexer(id).Info;
        if (DateTimeOffset.TryParse(current.RetryAfter, out var retry) && retry > DateTimeOffset.UtcNow)
            throw new DomainException("Kaynak yeniden deneme aralığında. Biraz sonra dene.", 429);
        var delay = _next.GetValueOrDefault(id) - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
        _next[id] = DateTimeOffset.UtcNow.AddSeconds(1);
    }
    private async Task<AdapterReleasePage> ReadReleasesAsync(IndexerSecret source,Dictionary<string,string?> parameters,CancellationToken token)
    {
        var configured=_network.Read(source.Info.Id);
        if(configured.AdapterId is null){var bytes=await QueryAsync(source,parameters,token);var page=ZnabParser.Releases(bytes,source.Info.Protocol,new Uri(source.Info.Endpoint),int.TryParse(parameters.GetValueOrDefault("limit"),out var limit)?limit:100);return new(page.Items,page.More);}
        var gate=_gates.GetOrAdd(source.Info.Id,_=>new SemaphoreSlim(1,1));await gate.WaitAsync(token);
        try
        {
            await WaitForSourceAsync(source.Info.Id,token);var manifest=SourceAdapterRegistry.Find(configured.AdapterId,store);
            var offset=int.TryParse(parameters.GetValueOrDefault("offset"),out var n)?n:0;
            if(offset>0&&manifest.Format is "nyaa" or "apibay")return new([],false);
            string? cursorKey=null;var requestParameters=new Dictionary<string,string?>(parameters);
            if(manifest.Format=="torrents-csv")
            {
                cursorKey=source.Info.Id+":"+source.Info.Revision+":"+Convert.ToHexStringLower(SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(parameters.Where(pair=>pair.Key is not("offset" or "after")).OrderBy(pair=>pair.Key).ToArray())));
                if(offset>0)
                {
                    if(!_adapterCursors.TryGetValue(cursorKey+":"+offset,out var cursor)||cursor.Expires<=DateTimeOffset.UtcNow)throw new DomainException("Kaynak arama sayfasının süresi doldu; aramayı baştan yenile.",409);
                    requestParameters["after"]=cursor.Cursor;
                }
            }
            var request=SourceAdapterRegistry.Request(manifest,source,requestParameters,Unprotect(source.Secret));using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response=await _network.SendAsync(source,request.Url,"query",timeout.Token,headers:request.Headers);CheckResponse(source.Info,response);await using var body=await response.Content.ReadAsStreamAsync(timeout.Token);var bytes=await MetadataProvider.ReadLimitedAsync(body,8*1024*1024,timeout.Token);
            store.RequireIndexerSnapshot(source.Info);var parsed=SourceAdapterRegistry.Parse(manifest,bytes,new Uri(source.Info.Endpoint),offset);
            if(cursorKey is not null&&parsed.NextCursor is{}next)
            {
                if(long.Parse(next,CultureInfo.InvariantCulture)<=long.Parse(requestParameters.GetValueOrDefault("after")??"0",CultureInfo.InvariantCulture))throw new DomainException("Kaynağın arama cursor'u ilerlemiyor.",502);
                foreach(var entry in _adapterCursors.Where(entry=>entry.Value.Expires<DateTimeOffset.UtcNow).ToArray())_adapterCursors.TryRemove(entry.Key,out _);
                if(_adapterCursors.Count>=2048)throw new DomainException("Kaynak sayfalama önbelleği dolu; biraz sonra yeniden dene.",429);
                _adapterCursors[cursorKey+":"+(offset+manifest.PageSize)]=(next,DateTimeOffset.UtcNow.AddMinutes(15));
            }
            return parsed;
        }
        catch(Exception ex)when(ex is System.Text.Json.JsonException or System.Xml.XmlException or InvalidOperationException or FormatException or ArgumentException or OverflowException){throw new DomainException("Adaptör yanıtı tanımlı şemaya uymuyor.",502);}
        catch(HttpRequestException){throw new DomainException("Doğrudan kaynağa bağlanılamadı.",502);}
        catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new DomainException("Doğrudan kaynak zaman aşımına uğradı.",504);}
        finally{gate.Release();}
    }

    private void CheckResponse(IndexerInfo source, HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var seconds = response.Headers.RetryAfter?.Delta?.TotalSeconds
                ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 30;
            store.SetIndexerHealth(source.Id, null, "Kaynak kota sınırına ulaştı.", DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(seconds, 1, 86400)),source.Revision);
            throw new DomainException("Kaynak kota sınırına ulaştı.", 429);
        }
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new DomainException("Kaynak kimlik bilgilerini reddetti.", 409);
        if (!response.IsSuccessStatusCode) throw new DomainException($"Kaynak HTTP {(int)response.StatusCode} döndürdü.", 502);
    }

    private async Task<(byte[]? Data, string? Magnet)> FetchReleaseAsync(IndexerSecret source, string address, CancellationToken token)
    {
        var gate = _gates.GetOrAdd(source.Info.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            await WaitForSourceAsync(source.Info.Id, token);
            var endpoint = new Uri(source.Info.Endpoint);
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) throw new DomainException("Kaynak indirme adresi geçersiz.", 502);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            for (var redirects = 0; redirects < 4; redirects++)
            {
                if (uri.Scheme == "magnet") return (null, uri.ToString());
                var sameOrigin = uri.Scheme == endpoint.Scheme && uri.IdnHost == endpoint.IdnHost && uri.Port == endpoint.Port;
                var additional = source.Info.AllowedHosts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var adapter=_network.Read(source.Info.Id).AdapterId;var archive=adapter=="internet-archive"&&endpoint.Host=="archive.org"&&uri.Host.EndsWith(".archive.org",StringComparison.OrdinalIgnoreCase);
                if (uri.UserInfo.Length > 0 || uri.Scheme is not ("http" or "https") || (!sameOrigin && !(uri.Scheme == "https" && uri.IsDefaultPort && (additional.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase)||archive))))
                    throw new DomainException("İndirme adresi kaynakta tanımlı alan adları dışında. Kaynak ayarını kontrol et.", 409);
                var secret = Unprotect(source.Secret);
                var url = adapter is null && sameOrigin && secret.Length > 0 && !QueryHelpers.ParseQuery(uri.Query).ContainsKey("apikey")
                    ? QueryHelpers.AddQueryString(uri.ToString(), "apikey", secret) : uri.ToString();
                var headers=new Dictionary<string,string>();
                if(adapter is not null&&sameOrigin&&secret.Length>0){var manifest=SourceAdapterRegistry.Find(adapter,store);if(manifest.Authentication=="query"&&!QueryHelpers.ParseQuery(uri.Query).ContainsKey(manifest.CredentialName))url=QueryHelpers.AddQueryString(url,manifest.CredentialName,secret);if(manifest.Authentication=="bearer")headers["Authorization"]="Bearer "+secret;if(manifest.Authentication=="header")headers[manifest.CredentialName]=secret;}
                using var response = await _network.SendAsync(source,url,"grab",timeout.Token,sameOrigin,headers);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    continue;
                }
                CheckResponse(source.Info, response);
                if (response.Content.Headers.ContentLength > 4 * 1024 * 1024) throw new DomainException("Kaynak torrent metadata boyutu sınırını aşıyor.", 413);
                await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
                return (await MetadataProvider.ReadLimitedAsync(body, NzbParser.MaximumBytes, timeout.Token), null);
            }
            throw new DomainException("Kaynak çok fazla yönlendirme yaptı.", 502);
        }
        catch (HttpRequestException) { throw new DomainException("Torrent metadata bağlantısı kurulamadı.", 502); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DomainException("Torrent metadata isteği zaman aşımına uğradı.", 504); }
        finally { gate.Release(); }
    }

    private string Unprotect(string value)
    {
        try { return _protector.Unprotect(value); }
        catch (CryptographicException) { throw new DomainException("Kaynak anahtarı veya arama sonucu okunamadı. Bağlantıyı yeniden kaydet.", 409); }
    }

    public async Task<RssReleaseBatch> ReadRssAsync(string id, CancellationToken token)
    {
        var source = store.GetIndexer(id);
        if (!source.Info.Enabled) throw new DomainException("Kaynak devre dışı.", 409);
        if (source.Info.Capabilities is null) { await TestAsync(id, token); source = store.GetIndexer(id); }
        if (!source.Info.Capabilities!.Searches.ContainsKey("search")) throw new DomainException("Kaynak standart RSS aramasını bildirmiyor.", 409);
        var args = new Dictionary<string, string?> { ["t"] = "search", ["limit"] = Math.Min(100, source.Info.Capabilities.MaxPageSize).ToString(), ["extended"] = "1" };
        if (source.Info.Categories.Length > 0) args["cat"] = source.Info.Categories;
        return new RssReleaseBatch(source.Info,(await ReadReleasesAsync(source,args,token)).Items);
    }

    private static Dictionary<string, string?> Parameters(ProviderContent content, IndexerCapabilities caps, ReleaseSearchInput input, ReleaseEpisodeIdentity? identity = null)
    {
        if (identity is { Mode: "daily" or "absolute" })
        {
            var methodWithQuery = caps.Searches.TryGetValue("tv-search", out var tv) && tv.Contains("q", StringComparer.OrdinalIgnoreCase) ? "tvsearch"
                : caps.Searches.TryGetValue("search", out var general) && general.Contains("q", StringComparer.OrdinalIgnoreCase) ? "search" : throw new DomainException("Kaynak günlük/mutlak bölüm için metin sorgusu desteklemiyor.", 409);
            return new Dictionary<string, string?> { ["t"] = methodWithQuery, ["q"] = content.OriginalTitle + (identity.Mode == "daily" ? " " + identity.AirDate : " - " + identity.AbsoluteNumber?.ToString("000", CultureInfo.InvariantCulture)) };
        }
        if (identity is { Mode: "alternative" }) input = input with { Season = identity.SourceSeason, Episode = identity.SourceEpisode };
        var type = content.Kind == "movie" ? "movie-search" : "tv-search";
        var method = content.Kind == "movie" ? "movie" : "tvsearch";
        if (!caps.Searches.TryGetValue(type, out var supported)) { method = "search"; supported = caps.Searches.GetValueOrDefault("search") ?? []; }
        var result = new Dictionary<string, string?> { ["t"] = method };
        bool Supports(string parameter) => supported.Contains(parameter, StringComparer.OrdinalIgnoreCase);
        if (Supports("tmdbid") && content.Provider == "tmdb") result["tmdbid"] = content.ProviderId.ToString(CultureInfo.InvariantCulture);
        else if (Supports("imdbid") && content.ImdbId is not null) result["imdbid"] = content.ImdbId.Replace("tt", "", StringComparison.OrdinalIgnoreCase);
        else if (Supports("tvdbid") && content.TvdbId is not null) result["tvdbid"] = content.TvdbId.ToString();
        else if (Supports("tvmazeid") && content.Provider == "tvmaze") result["tvmazeid"] = content.ProviderId.ToString(CultureInfo.InvariantCulture);
        else if (Supports("q")) result["q"] = content.OriginalTitle;
        else if (caps.Searches.TryGetValue("search", out var generic) && generic.Contains("q", StringComparer.OrdinalIgnoreCase))
        { result["t"] = "search"; result["q"] = content.OriginalTitle; supported = generic; }
        else throw new DomainException("Kaynak bu içerik için kullanılabilir sorgu parametresi sunmuyor.", 409);
        if (input.Season is not null && Supports("season")) result["season"] = input.Season.ToString();
        if (input.Episode is not null && Supports("ep")) result["ep"] = input.Episode.ToString();
        if (result.ContainsKey("q") && input.Season is not null && !Supports("season"))
            result["q"] += $" S{input.Season:00}" + (input.Episode is null ? "" : $"E{input.Episode:00}");
        return result;
    }
}

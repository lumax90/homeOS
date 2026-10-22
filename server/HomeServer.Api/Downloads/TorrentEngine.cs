using System.Net;
using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using MonoTorrent;
using MonoTorrent.BEncoding;
using MonoTorrent.Client;

namespace HomeServer.Downloads;

public sealed record TorrentFileSnapshot(string RelativePath, string FullPath, long Length);
public sealed record TorrentSnapshot(string Name, string Status, double Progress, long Size, long Down, long Up, int Peers, bool Complete, string? Message = null, long Uploaded = 0, long Received = 0);

public interface ITorrentSession : IAsyncDisposable
{
    TorrentSnapshot Snapshot { get; }
    IReadOnlyList<TorrentFileSnapshot> Files { get; }
    string Directory { get; }
    Task StartAsync();
    Task StopAsync();
}

public interface ITorrentSessions
{
    Task<ITorrentSession> OpenAsync(DownloadSource source, CancellationToken token);
}

public sealed class TorrentEngine(DownloadFiles files, MediaStore store, IConfiguration configuration, IDataProtectionProvider protection, TorrentMetadataResolver? resolver = null)
    : ITorrentSessions, IDisposable
{
    private readonly IDataProtector _protector = protection.CreateProtector("Home.Download.Source.v1");
    private readonly SemaphoreSlim _createGate = new(1, 1);
    private ClientEngine? _engine;
    private int _disposed;
    private int _nntpSessions;
    private int _openSessions;
    public bool HasOpenSessions => Volatile.Read(ref _openSessions) > 0;

    public IReadOnlyList<TransferFile> ListFiles(string id)
    {
        var source = store.GetDownloadSource(id); if (source.Record.SourceKind != "torrent") return [];
        var torrent = Parse(Convert.FromBase64String(_protector.Unprotect(source.Source))); var policy = store.GetTransferPolicy(id);
        return torrent.Files.Select(file => new TransferFile(DownloadFiles.ValidateRelative(file.Path), file.Length,
            policy.Files is null || policy.Files.Contains(DownloadFiles.ValidateRelative(file.Path), StringComparer.OrdinalIgnoreCase),
            policy.FilePriorities?.GetValueOrDefault(DownloadFiles.ValidateRelative(file.Path), 3) ?? 3)).ToArray();
    }
    public void ValidateSelection(string id, TransferPolicy policy)
    {
        if (policy.Files is null && policy.FilePriorities is not { Count: > 0 }) return;
        var files = ListFiles(id);
        if (files.Count == 0) throw new DomainException("Dosya seçimi torrent metadata'sı geldikten sonra kullanılabilir.", 409);
        var names = files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (policy.Files is { } selected && (selected.Length == 0 || selected.Any(path => !names.Contains(path)) || !selected.Any(MediaPaths.IsMedia))) throw new DomainException("Seçimde en az bir geçerli medya dosyası olmalı.");
        if (policy.FilePriorities?.Any(value => !names.Contains(value.Key) || value.Value is < 1 or > 5) == true) throw new DomainException("Dosya önceliği seçimi geçersiz.");
    }
    public async Task SetNntpActiveAsync(bool active)
    {
        if (active) Interlocked.Increment(ref _nntpSessions); else Interlocked.Decrement(ref _nntpSessions);
        try { await ApplyLimitsAsync(CancellationToken.None); }
        catch { if (active) Interlocked.Decrement(ref _nntpSessions); throw; }
    }
    public async Task ApplyLimitsAsync(CancellationToken token)
    {
        await _createGate.WaitAsync(token);
        try
        {
            if (_engine is null) return;
            var limits = store.GetTransferDefaults(configuration);
            await _engine.UpdateSettingsAsync(new EngineSettingsBuilder(_engine.Settings)
            {
                MaximumDownloadRate = limits.DownloadKiB * 1024 / (Volatile.Read(ref _nntpSessions) > 0 ? 2 : 1),
                MaximumUploadRate = limits.UploadKiB * 1024, MaximumConnections = limits.MaximumConnections
            }.ToSettings());
        }
        finally { _createGate.Release(); }
    }
    public void CleanPayload(string id)
    {
        var source = store.GetDownloadSource(id);
        var directory = Path.Combine(DownloadFiles.EnsureDirectory(files.Root, "staging"), DownloadFiles.ValidId(id));
        if (!Directory.Exists(directory)) return;
        AppFiles.RejectLink(directory);
        var pending = new Stack<string>(); pending.Push(directory);
        while (pending.TryPop(out var folder))
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                AppFiles.RejectLink(entry); if (Directory.Exists(entry)) pending.Push(entry);
            }
        if (source.Record.SourceKind == "nzb")
        {
            var marker = MediaPaths.ResolveFile(directory, ".home-nzb.json");
            if (File.ReadAllText(marker) != source.Record.InfoHash) throw new DomainException("NZB staging sahipliği doğrulanamadı.", 409);
        }
        else
        {
            if (source.Record.SourceKind != "torrent") throw new DomainException("Torrent sahipliği henüz doğrulanmadı.", 409);
            var torrent = Parse(Convert.FromBase64String(_protector.Unprotect(source.Source)));
            OwnDirectory(directory, source.Record, torrent);
            var settings = new EngineSettingsBuilder { CacheDirectory = files.EngineCache }.ToSettings();
            var resume = settings.GetFastResumePath(torrent.InfoHashes);
            if (File.Exists(resume)) File.Delete(MediaPaths.ResolveFile(files.EngineCache, Path.GetRelativePath(files.EngineCache, resume)));
        }
        AppFiles.DeleteOwnedDirectory(directory);
    }
    private static void OwnDirectory(string directory, DownloadRecord record, Torrent torrent)
    {
        var marker = Path.Combine(directory, ".home-transfer.json");
        if (File.Exists(marker))
        {
            if (File.ReadAllText(MediaPaths.ResolveFile(directory, ".home-transfer.json")) != record.InfoHash) throw new DomainException("Staging alanı başka aktarıma ait.", 409);
            return;
        }
        var expected = torrent.Files.Select(file => DownloadFiles.ValidateRelative(file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, path); _ = MediaPaths.ResolveFile(directory, relative);
            if (!expected.Contains(relative) && !(relative.EndsWith(".!mt", StringComparison.Ordinal) && expected.Contains(relative[..^4]))) throw new DomainException("Staging alanında bu torrente ait olmayan dosya var.", 409);
        }
        File.WriteAllText(marker, record.InfoHash);
    }

    public DownloadRecord AddMagnet(string value, AcquisitionContext? acquisition = null, MusicDownloadContext? music = null, DownloadPrincipal? principal = null)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16000 || !MagnetLink.TryParse(value.Trim(), out var magnet))
            throw new DomainException("Geçerli bir magnet bağlantısı gir.");
        var name = string.IsNullOrWhiteSpace(magnet.Name) ? "Magnet indirmesi" : magnet.Name;
        return store.AddDownload(Hashes(magnet.InfoHashes), name[..Math.Min(name.Length, 200)], "magnet", _protector.Protect(value.Trim()), 0, acquisition, music, principal);
    }

    public DownloadRecord AddTorrent(byte[] bytes, AcquisitionContext? acquisition = null, MusicDownloadContext? music = null, DownloadPrincipal? principal = null)
    {
        var torrent = Parse(bytes);
        return store.AddDownload(Hashes(torrent.InfoHashes), torrent.Name, "torrent", _protector.Protect(Convert.ToBase64String(bytes)), torrent.Size, acquisition, music, principal);
    }

    public async Task<ITorrentSession> OpenAsync(DownloadSource source, CancellationToken token)
    {
        if (source.Record.Ownership?.UserId is not null && source.Budget is null) throw new DomainException("Önce hesabın aktarım bütçesi ayrılmalı.", 428);
        var engine = await GetEngineAsync(token);
        var secret = _protector.Unprotect(source.Source);
        Torrent torrent;
        if (source.Record.SourceKind == "magnet")
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var magnet = MagnetLink.Parse(secret);
            var publicNetwork = resolver is not null && !configuration.GetValue<bool>("Home:Downloads:LoopbackOnly");
            if (publicNetwork) magnet = resolver!.WithTrackers(source, magnet);
            ReadOnlyMemory<byte> metadata;
            try
            {
                var supplied = publicNetwork ? await resolver!.TryHttpAsync(source, magnet, timeout.Token) : null;
                metadata = supplied is not null ? supplied : await engine.DownloadMetadataAsync(magnet, timeout.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new DomainException("Torrent bilgileri iki dakika içinde alınamadı. Kaynak eşleri/izleyicileri yanıt vermiyor; yeniden deneyebilir veya başka bir sürüm seçebilirsin.", 504); }
            if (metadata.Length > 4 * 1024 * 1024) throw new DomainException("Torrent bilgileri çok büyük.");
            var encoded = BEncodedValue.Decode<BEncodedDictionary>(metadata.Span);
            var tiers = new BEncodedList();
            foreach (var tracker in magnet.AnnounceUrls) tiers.Add(new BEncodedList { new BEncodedString(tracker) });
            if (tiers.Count > 0 && !encoded.ContainsKey("announce-list") && !encoded.ContainsKey("announce")) encoded["announce-list"] = tiers;
            var bytes = encoded.Encode();
            torrent = Parse(bytes);
            if (!Hashes(torrent.InfoHashes).Intersect(Hashes(magnet.InfoHashes)).Any()) throw new DomainException("Magnet kimliği doğrulanamadı.");
            token.ThrowIfCancellationRequested();
            store.RegisterDownloadHashes(source.Record.Id, Hashes(torrent.InfoHashes));
            store.SaveResolvedTorrent(source.Record.Id, _protector.Protect(Convert.ToBase64String(bytes)), source.Record.Revision);
        }
        else torrent = Parse(Convert.FromBase64String(secret));
        store.RegisterDownloadHashes(source.Record.Id, Hashes(torrent.InfoHashes));
        var policy = store.GetTransferPolicy(source.Record.Id);
        var selection = policy.Files?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var payloadBytes = torrent.Files.Where(file => selection is null || selection.Contains(DownloadFiles.ValidateRelative(file.Path))).Sum(file => file.Length);
        store.UpdateDownload(source.Record.Id, source.Record.Revision, "metadata", "Paket boyutu doğrulandı; hesap bütçesi denetleniyor.", source.Record.Progress, payloadBytes, name: torrent.Name);
        store.ReserveDownloadPayload(source.Budget, payloadBytes, DateTimeOffset.UtcNow);
        var directory = files.Payload(source.Record.Id); OwnDirectory(directory, source.Record, torrent);
        var loaded = await engine.AddAsync(torrent, directory, Settings(policy));
        try
        {
            if (policy.Files is not null || policy.FilePriorities is { Count: > 0 })
            {
                var wanted = (policy.Files ?? torrent.Files.Select(file => DownloadFiles.ValidateRelative(file.Path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (wanted.Count == 0 || wanted.Any(path => !torrent.Files.Any(file => DownloadFiles.ValidateRelative(file.Path).Equals(path, StringComparison.OrdinalIgnoreCase)))) throw new DomainException("Torrent dosya seçimi geçersiz.");
                var priorities = new Dictionary<string, int>(policy.FilePriorities ?? [], StringComparer.OrdinalIgnoreCase);
                foreach (var file in loaded.Files)
                {
                    var path = DownloadFiles.ValidateRelative(file.Path);
                    var priority = priorities.GetValueOrDefault(path, 3) switch { 1 => Priority.Lowest, 2 => Priority.Low, 4 => Priority.High, 5 => Priority.Highest, _ => Priority.Normal };
                    await loaded.SetFilePriorityAsync(file, wanted.Contains(path) ? priority : Priority.DoNotDownload);
                }
            }
            if (source.Record.PayloadCleanedAt is not null) await loaded.SetNeedsHashCheckAsync();
            Interlocked.Increment(ref _openSessions);
            return new Session(engine, loaded, directory, () => Interlocked.Decrement(ref _openSessions), () => { token.ThrowIfCancellationRequested(); store.CommitDownloadPayloadStart(source.Budget, payloadBytes, DateTimeOffset.UtcNow); });
        }
        catch { await engine.RemoveAsync(loaded, RemoveMode.KeepAllData); throw; }
    }

    private TorrentSettings Settings(TransferPolicy policy) => new TorrentSettingsBuilder
    {
        CreateContainingDirectory = false,
        MaximumConnections = 40,
        MaximumDownloadRate = policy.DownloadKiB * 1024,
        MaximumUploadRate = policy.UploadKiB * 1024,
        AllowDht = !configuration.GetValue<bool>("Home:Downloads:LoopbackOnly"),
        AllowPeerExchange = !configuration.GetValue<bool>("Home:Downloads:LoopbackOnly")
    }.ToSettings();

    private async Task<ClientEngine> GetEngineAsync(CancellationToken token)
    {
        await _createGate.WaitAsync(token);
        try
        {
            if (_engine is not null) return _engine;
            var isolated = configuration.GetValue<bool>("Home:Downloads:LoopbackOnly");
            var address = isolated ? IPAddress.Loopback : IPAddress.Any;
            var port = configuration.GetValue("Home:Downloads:ListenPort", 55123);
            var limits = store.GetTransferDefaults(configuration);
            _engine = new ClientEngine(new EngineSettingsBuilder
            {
                CacheDirectory = files.EngineCache,
                ListenEndPoints = new Dictionary<string, IPEndPoint> { ["ipv4"] = new(address, isolated ? 0 : port) },
                DhtEndPoint = isolated ? null : new IPEndPoint(IPAddress.Any, port),
                AllowLocalPeerDiscovery = false,
                AllowPortForwarding = false,
                AutoSaveLoadFastResume = true,
                AutoSaveLoadMagnetLinkMetadata = true,
                AutoSaveLoadDhtCache = !isolated,
                FastResumeMode = FastResumeMode.Accurate,
                MaximumConnections = limits.MaximumConnections,
                MaximumOpenFiles = 64,
                MaximumDownloadRate = limits.DownloadKiB * 1024 / (Volatile.Read(ref _nntpSessions) > 0 ? 2 : 1),
                MaximumUploadRate = limits.UploadKiB * 1024
            }.ToSettings());
            return _engine;
        }
        finally { _createGate.Release(); }
    }

    public static Torrent Parse(byte[] bytes)
    {
        if (bytes.Length is 0 or > 4 * 1024 * 1024) throw new DomainException("Torrent dosyası boş veya çok büyük.");
        ValidateEnvelope(bytes);
        try
        {
            var torrent = Torrent.Load(bytes.AsSpan());
            DownloadFiles.ValidateTorrent(torrent);
            return torrent;
        }
        catch (DomainException) { throw; }
        catch (Exception ex) when (ex is ArgumentException or TorrentException or FormatException or BEncodingException or InvalidOperationException or OverflowException)
        { throw new DomainException("Torrent dosyası okunamadı."); }
    }

    private static void ValidateEnvelope(byte[] data)
    {
        var depth = 0;
        if (data[0] != (byte)'d') throw new DomainException("Geçerli bir torrent sözlüğü gerekli.");
        for (var i = 0; i < data.Length;)
        {
            var c = data[i++];
            if (c is (byte)'l' or (byte)'d')
            {
                if (++depth > 32) throw new DomainException("Torrent dosyası çok fazla iç içe veri içeriyor.");
            }
            else if (c == (byte)'e')
            {
                if (--depth < 0 || depth == 0 && i != data.Length) throw new DomainException("Torrent yapısı geçerli değil.");
            }
            else if (c == (byte)'i')
            {
                var start = i;
                while (i < data.Length && data[i] != (byte)'e') i++;
                if (i == data.Length || i - start > 24) throw new DomainException("Torrent tamsayısı geçerli değil.");
                i++;
            }
            else if (c is >= (byte)'0' and <= (byte)'9')
            {
                long length = c - '0';
                while (i < data.Length && data[i] is >= (byte)'0' and <= (byte)'9')
                {
                    length = length * 10 + data[i++] - '0';
                    if (length > data.Length) throw new DomainException("Torrent veri uzunluğu geçerli değil.");
                }
                if (i >= data.Length || data[i++] != (byte)':' || length > data.Length - i) throw new DomainException("Torrent dosyası eksik.");
                i += (int)length;
            }
            else throw new DomainException("Torrent dosyası okunamadı.");
        }
        if (depth != 0) throw new DomainException("Torrent dosyası tamamlanmamış.");
    }

    public static string[] Hashes(InfoHashes hashes) => new[] { hashes.V1, hashes.V2 }.Where(h => h is not null).Select(h => h!.ToHex().ToLowerInvariant()).ToArray();
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _engine?.Dispose();
        _createGate.Dispose();
    }

    public sealed class Session(ClientEngine engine, TorrentManager manager, string directory, Action? closed = null, Action? starting = null) : ITorrentSession
    {
        public TorrentManager Manager => manager;
        public string Directory => directory;
        public TorrentSnapshot Snapshot => new(manager.Name, State(manager.State), manager.PartialProgress, manager.Files.Where(file => file.Priority != Priority.DoNotDownload).Sum(file => file.Length),
            manager.Monitor.DownloadRate, manager.Monitor.UploadRate, manager.OpenConnections,
            manager.HasMetadata && manager.HashChecked && manager.State is TorrentState.Seeding or TorrentState.Downloading && manager.Files.Any(file => file.Priority != Priority.DoNotDownload) && manager.Files.Where(file => file.Priority != Priority.DoNotDownload).All(file => file.BitField.AllTrue),
            Uploaded: manager.Monitor.DataBytesSent, Received: manager.Monitor.DataBytesReceived);
        public IReadOnlyList<TorrentFileSnapshot> Files => manager.Files.Where(file => file.Priority != Priority.DoNotDownload).Select(f => new TorrentFileSnapshot(
            DownloadFiles.ValidateRelative(f.Path), f.DownloadCompleteFullPath, f.Length)).ToArray();
        public async Task StartAsync()
        {
            starting?.Invoke();
            if (manager.State is TorrentState.Stopped or TorrentState.Paused) await manager.StartAsync();
        }
        public async Task StopAsync()
        {
            if (manager.State != TorrentState.Stopped) await manager.StopAsync(TimeSpan.FromSeconds(10));
        }
        public async ValueTask DisposeAsync()
        {
            try { await StopAsync(); await engine.RemoveAsync(manager, RemoveMode.KeepAllData); }
            finally { closed?.Invoke(); }
        }
        private static string State(TorrentState state) => state switch
        {
            TorrentState.Metadata => "metadata",
            TorrentState.Hashing or TorrentState.Starting => "verifying",
            TorrentState.Downloading => "downloading",
            TorrentState.Seeding => "seeding",
            TorrentState.Error => "failed",
            TorrentState.Paused or TorrentState.Stopped => "paused",
            _ => "verifying"
        };
    }
}

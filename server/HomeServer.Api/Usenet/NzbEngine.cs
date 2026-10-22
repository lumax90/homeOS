using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.DataProtection;

namespace HomeServer.Usenet;

public sealed class NzbEngine(DownloadFiles paths, MediaStore store, UsenetService servers, Par2Tool par2,
    PackageExtractor extractor, IDataProtectionProvider protection, IConfiguration configuration, TorrentEngine? torrents = null)
{
    private readonly IDataProtector _protector = protection.CreateProtector("Home.Usenet.Payload.v1");
    private long MaximumBytes => Math.Clamp(configuration.GetValue("Home:Downloads:MaximumPayloadGiB", 256L), 1, 4096) * 1024 * 1024 * 1024;
    public DownloadRecord AddNzb(byte[] bytes, AcquisitionContext? context = null, string? name = null, MusicDownloadContext? music = null, DownloadPrincipal? principal = null)
    {
        var manifest = NzbParser.Parse(bytes, name);
        if (manifest.EstimatedBytes > MaximumBytes) throw new DomainException("NZB paket boyutu sunucunun indirme sınırını aşıyor.");
        if (!servers.List().Any(server => server.Enabled)) throw new DomainException("Önce bir Usenet/NNTP sunucusu bağla.", 409);
        return store.AddDownload([manifest.Fingerprint], manifest.Name, "nzb", _protector.Protect(JsonSerializer.Serialize(manifest)), manifest.EstimatedBytes, context, music, principal);
    }
    public void SetPassword(string id, string password)
    {
        if (password is null || password.Length > 1024) throw new DomainException("Arşiv parolası geçersiz.");
        store.SetDownloadPassword(id, _protector.Protect(password));
    }
    public Task<ITorrentSession> OpenAsync(DownloadSource source, CancellationToken token)
    {
        if (source.Record.Ownership?.UserId is not null && source.Budget is null) throw new DomainException("Önce hesabın aktarım bütçesi ayrılmalı.", 428);
        try
        {
            var manifest = JsonSerializer.Deserialize<NzbManifest>(_protector.Unprotect(source.Source)) ?? throw new DomainException("NZB kaydı okunamadı.");
            store.ReserveDownloadPayload(source.Budget, manifest.EstimatedBytes, DateTimeOffset.UtcNow);
            var password = store.GetDownloadPassword(source.Record.Id) is { } secret ? _protector.Unprotect(secret) : manifest.Password;
            return Task.FromResult<ITorrentSession>(new Session(paths.Payload(source.Record.Id), manifest, password, servers, par2, extractor, MaximumBytes, token, torrents,
                () => { token.ThrowIfCancellationRequested(); store.CommitDownloadPayloadStart(source.Budget, manifest.EstimatedBytes, DateTimeOffset.UtcNow); }));
        }
        catch (CryptographicException) { throw new DomainException("NZB veya arşiv parolası çözülemedi; uygulama anahtarlarını kontrol et."); }
    }

    private sealed record ReadyFile(string Path, long Length, string Sha256);
    private sealed record Ready(string Fingerprint, ReadyFile[] Files);
    public sealed class Session : ITorrentSession
    {
        private readonly string _root;
        private readonly NzbManifest _manifest;
        private readonly string? _password;
        private readonly UsenetService _servers;
        private readonly Par2Tool _par2;
        private readonly PackageExtractor _extractor;
        private readonly long _maximum;
        private readonly CancellationTokenSource _cancellation;
        private readonly TorrentEngine? _torrents;
        private readonly Action? _starting;
        private Task? _task;
        private TorrentSnapshot _snapshot;
        private IReadOnlyList<TorrentFileSnapshot> _files = [];
        public string Directory => Path.Combine(_root, "payload");
        public TorrentSnapshot Snapshot => _snapshot;
        public IReadOnlyList<TorrentFileSnapshot> Files => _files;
        public Session(string root, NzbManifest manifest, string? password, UsenetService servers, Par2Tool par2, PackageExtractor extractor, long maximum, CancellationToken token, TorrentEngine? torrents = null, Action? starting = null)
        {
            _root = root; _manifest = manifest; _password = password; _servers = servers; _par2 = par2; _extractor = extractor; _maximum = maximum;
            _torrents = torrents; _starting = starting;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            _snapshot = new(manifest.Name, "queued", 0, manifest.EstimatedBytes, 0, 0, 0, false);
        }
        public Task StartAsync() { if (_task is null) { _starting?.Invoke(); _task = RunAsync(_cancellation.Token); } return Task.CompletedTask; }
        public async Task StopAsync()
        {
            if (_task is not null && !_task.IsCompleted) _cancellation.Cancel();
            if (_task is not null) await _task;
        }
        public async ValueTask DisposeAsync() { await StopAsync(); _cancellation.Dispose(); }

        private async Task RunAsync(CancellationToken token)
        {
            string? wire = null, expanded = null;
            var shared = false;
            try
            {
                if (_torrents is not null) { await _torrents.SetNntpActiveAsync(true); shared = true; }
                var marker = Path.Combine(_root, ".home-nzb.json");
                if (File.Exists(marker))
                {
                    AppFiles.RejectLink(marker);
                    if (await File.ReadAllTextAsync(marker, token) != _manifest.Fingerprint) throw new DomainException("NZB staging alanı başka pakete ait.");
                }
                else
                {
                    if (System.IO.Directory.EnumerateFileSystemEntries(_root).Any()) throw new DomainException("NZB staging alanında bilinmeyen dosyalar var.");
                    await File.WriteAllTextAsync(marker, _manifest.Fingerprint, token);
                }
                if (System.IO.Directory.Exists(Directory))
                {
                    var saved = JsonSerializer.Deserialize<Ready>(await File.ReadAllTextAsync(MediaPaths.ResolveFile(Directory, ".home-ready.json"), token)) ?? throw new DomainException("NZB yayın makbuzu okunamadı.");
                    if (saved.Fingerprint != _manifest.Fingerprint) throw new DomainException("NZB yayın kimliği eşleşmiyor.");
                    foreach (var file in saved.Files)
                    {
                        var path = MediaPaths.ResolveFile(Directory, DownloadFiles.ValidateRelative(file.Path));
                        if (new FileInfo(path).Length != file.Length || await HashAsync(path, token) != file.Sha256) throw new DomainException("Hazırlanan NZB dosyası sonradan değişti.");
                    }
                    Complete(saved); return;
                }
                var partsRoot = DownloadFiles.EnsureDirectory(_root, "parts");
                var parts = new ConcurrentDictionary<(int File, int Number), PartReceipt>();
                long done = 0; var total = _manifest.Files.Sum(file => file.Segments.Length); var completed = 0;
                var started = DateTimeOffset.UtcNow;
                var concurrency = Math.Clamp(_servers.List().Where(server => server.Enabled).Sum(server => server.Connections), 1, 16);
                _snapshot = _snapshot with { Status = "downloading", Peers = concurrency, Message = "NNTP parçaları okunuyor ve doğrulanıyor." };
                await Parallel.ForEachAsync(_manifest.Files.SelectMany(file => file.Segments.Select(segment => (File: file, Segment: segment))),
                    new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = token }, async (work, cancellation) =>
                    {
                        var folder = DownloadFiles.EnsureDirectory(partsRoot, work.File.Index.ToString());
                        var path = Path.Combine(folder, work.Segment.Number + ".bin"); var receiptPath = path + ".json";
                        PartReceipt? receipt = null;
                        if (File.Exists(path) && File.Exists(receiptPath))
                        {
                            AppFiles.RejectLink(path); AppFiles.RejectLink(receiptPath);
                            try { receipt = JsonSerializer.Deserialize<PartReceipt>(await File.ReadAllTextAsync(receiptPath, cancellation)); }
                            catch (JsonException) { }
                            if (receipt is null || receipt.Number != work.Segment.Number || receipt.Length != new FileInfo(path).Length || receipt.Length > work.Segment.Bytes || receipt.FileSize > _maximum
                                || await HashAsync(path, cancellation) != receipt.Sha256) receipt = null;
                        }
                        if (receipt is null)
                        {
                            var part = await _servers.ReadPartAsync(work.Segment, work.File.Groups, _maximum, cancellation);
                            if (part is not null)
                            {
                                var temporary = path + ".part";
                                if (File.Exists(temporary)) { AppFiles.RejectLink(temporary); File.Delete(temporary); }
                                if (File.Exists(path)) AppFiles.RejectLink(path);
                                if (File.Exists(receiptPath)) AppFiles.RejectLink(receiptPath);
                                await File.WriteAllBytesAsync(temporary, part.Bytes, cancellation);
                                File.Move(temporary, path, true);
                                receipt = new PartReceipt(part.Name, part.FileSize, part.Offset, part.Number, part.Bytes.Length, part.Crc, part.FileCrc, Convert.ToHexString(SHA256.HashData(part.Bytes)).ToLowerInvariant());
                                await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(receipt), cancellation);
                                Interlocked.Add(ref done, part.Bytes.Length);
                            }
                        }
                        if (receipt is not null) parts[(work.File.Index, work.Segment.Number)] = receipt;
                        var progress = Interlocked.Increment(ref completed) * 90d / total;
                        _snapshot = new(_manifest.Name, "downloading", progress, _manifest.EstimatedBytes, (long)(Interlocked.Read(ref done) / Math.Max(1, (DateTimeOffset.UtcNow - started).TotalSeconds)), 0, concurrency, false,
                            "NNTP parçaları alınıyor ve CRC ile doğrulanıyor.", Received: Interlocked.Read(ref done));
                    });
                _snapshot = _snapshot with { Status = "verifying", Progress = 92, Down = 0, Message = "Parçalar birleştiriliyor; dosya bütünlüğü denetleniyor." };
                wire = DownloadFiles.EnsureDirectory(_root, "assemble-" + Guid.NewGuid().ToString("N"));
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var incomplete = new List<string>(); long expandedSize = 0;
                foreach (var file in _manifest.Files)
                {
                    var received = file.Segments.Where(segment => parts.ContainsKey((file.Index, segment.Number))).Select(segment => parts[(file.Index, segment.Number)]).OrderBy(receipt => receipt.Offset).ToArray();
                    if (received.Length == 0) { incomplete.Add(file.Name); continue; }
                    var first = received[0]; var name = NzbParser.SafeName(first.Name);
                    if (!names.Add(name) || received.Any(receipt => receipt.Name != first.Name || receipt.FileSize != first.FileSize)) throw new DomainException("NZB parçaları farklı veya çakışan dosya kimlikleri içeriyor.");
                    expandedSize += first.FileSize;
                    if (expandedSize > _maximum || new DriveInfo(Path.GetPathRoot(wire)!).AvailableFreeSpace < first.FileSize + 16 * 1024 * 1024) throw new DomainException("NZB dosyaları için boş alan veya boyut sınırı yetersiz.");
                    var outputPath = Path.Combine(wire, name); long offset = 0; var covered = true;
                    await using (var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
                    {
                        output.SetLength(first.FileSize);
                        foreach (var receipt in received)
                        {
                            if (receipt.Offset < offset || receipt.Offset < 0 || receipt.Length < 1 || receipt.Offset > first.FileSize - receipt.Length) throw new DomainException("NZB parça aralıkları çakışıyor.");
                            if (receipt.Offset != offset) covered = false;
                            output.Position = receipt.Offset;
                            await using var part = File.OpenRead(MediaPaths.ResolveFile(partsRoot, Path.Combine(file.Index.ToString(), receipt.Number + ".bin")));
                            await part.CopyToAsync(output, token); offset = receipt.Offset + receipt.Length;
                        }
                        await output.FlushAsync(token);
                    }
                    if (offset != first.FileSize || !covered) incomplete.Add(name);
                    else
                    {
                        var crc = received.Select(part => part.FileCrc).Where(crc => crc is not null).Distinct().ToArray();
                        if (crc.Length > 1) throw new DomainException("yEnc dosya CRC değerleri çelişiyor.");
                        if (crc.Length == 1 && await FileCrcAsync(outputPath, token) != crc[0]) incomplete.Add(name);
                    }
                }
                _snapshot = _snapshot with { Status = "repairing", Progress = 94, Message = "PAR2 doğrulaması ve gereken recovery onarımı yapılıyor." };
                var parity = await _par2.RepairAsync(wire, _maximum, token);
                if (incomplete.Any(name => !name.EndsWith(".par2", StringComparison.OrdinalIgnoreCase) && !parity.Any(file => file.Name.Equals(name, StringComparison.OrdinalIgnoreCase))))
                    throw new DomainException($"{incomplete.Count} dosyada eksik/bozuk Usenet parçaları var; yeterli PAR2 tanımı bulunamadı.");
                _snapshot = _snapshot with { Status = "unpacking", Progress = 96, Message = "Arşivler açılıyor ve içerik checksum'ları doğrulanıyor." };
                expanded = DownloadFiles.EnsureDirectory(_root, "expand-" + Guid.NewGuid().ToString("N"));
                await _extractor.ExtractAsync(wire, expanded, _password, _maximum, token);
                var readyFiles = new List<ReadyFile>();
                foreach (var file in System.IO.Directory.EnumerateFiles(expanded, "*", SearchOption.AllDirectories))
                {
                    token.ThrowIfCancellationRequested(); readyFiles.Add(new ReadyFile(Path.GetRelativePath(expanded, file), new FileInfo(file).Length, await HashAsync(file, token)));
                }
                var ready = new Ready(_manifest.Fingerprint, readyFiles.ToArray());
                if (!readyFiles.Any(file => MediaPaths.IsMedia(file.Path))) throw new DomainException("Paketten desteklenen medya dosyası çıkmadı.");
                await File.WriteAllTextAsync(Path.Combine(expanded, ".home-ready.json"), JsonSerializer.Serialize(ready), token);
                System.IO.Directory.Move(expanded, Directory); expanded = null;
                Complete(ready);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { _snapshot = _snapshot with { Status = "paused", Down = 0, Message = "Doğrulanmış Usenet parçaları devam için saklandı." }; }
            catch (Exception ex) { _snapshot = _snapshot with { Status = "failed", Down = 0, Message = ex is DomainException ? ex.Message : "NZB paketi tamamlanamadı. Parçalar staging alanında korunuyor." }; }
            finally
            {
                if (wire is not null && System.IO.Directory.Exists(wire)) AppFiles.DeleteOwnedDirectory(wire);
                if (expanded is not null && System.IO.Directory.Exists(expanded)) AppFiles.DeleteOwnedDirectory(expanded);
                if (shared) await _torrents!.SetNntpActiveAsync(false);
            }
        }
        private void Complete(Ready ready)
        {
            _files = ready.Files.Select(file => new TorrentFileSnapshot(file.Path, Path.Combine(Directory, file.Path), file.Length)).ToArray();
            _snapshot = new(_manifest.Name, "completed", 100, ready.Files.Sum(file => file.Length), 0, 0, 0, true, "NZB paketi doğrulandı.", Received: _snapshot.Received);
        }
        private static async Task<string> HashAsync(string path, CancellationToken token)
        {
            await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
        }
        private static async Task<uint> FileCrcAsync(string path, CancellationToken token)
        {
            await using var stream = File.OpenRead(path); var crc = new Crc32(); var buffer = new byte[128 * 1024]; int count;
            while ((count = await stream.ReadAsync(buffer, token)) > 0) crc.Append(buffer.AsSpan(0, count));
            return crc.Value;
        }
    }
}

using System.Collections.Concurrent;
using HomeServer.Infrastructure;

namespace HomeServer.Scanning;

public sealed class LibraryMonitor(MediaStore store, EventHub events, ILogger<LibraryMonitor> logger) : BackgroundService
{
    private sealed record Watched(int Revision, FileSystemWatcher Watcher);
    private readonly Dictionary<string, Watched> _watchers = [];
    private readonly ConcurrentDictionary<string, (DateTimeOffset First, DateTimeOffset Last)> _changed = new();
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                store.ScheduleLibraryScans();
                var libraries = store.GetLibraries().Where(library => !library.IsDemo).ToArray();
                foreach (var id in _watchers.Keys.Where(id => !libraries.Any(library => library.Id == id)).ToArray()) Remove(id);
                foreach (var library in libraries)
                {
                    var config = store.GetLibraryConfiguration(library.Id);
                    if (_watchers.TryGetValue(library.Id, out var current) && (current.Revision != config.Revision || !config.Options.WatchChanges)) Remove(library.Id);
                    if (!config.Options.WatchChanges || _watchers.ContainsKey(library.Id)) continue;
                    try
                    {
                        var watcher = new FileSystemWatcher(library.Path) { IncludeSubdirectories = true, InternalBufferSize = 32 * 1024, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite };
                        void Changed(object sender, FileSystemEventArgs change)
                        {
                            if (LibraryTraversal.Ignored(change.FullPath, store.LibraryIgnoredRoots(library.Path))) return;
                            if (LibraryTraversal.Excluded(Path.GetRelativePath(library.Path, change.FullPath), config.Options)) return;
                            var now = DateTimeOffset.UtcNow; _changed.AddOrUpdate(library.Id, (now, now), (_, value) => (value.First, now));
                        }
                        watcher.Changed += Changed; watcher.Created += Changed; watcher.Deleted += Changed;
                        watcher.Renamed += (sender, change) =>
                        {
                            if (!LibraryTraversal.Excluded(Path.GetRelativePath(library.Path, change.OldFullPath), config.Options))
                                _changed[library.Id] = (DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                            else Changed(sender, change);
                        };
                        watcher.Error += (_, _) => _changed[library.Id] = (DateTimeOffset.UtcNow.AddSeconds(-30), DateTimeOffset.UtcNow);
                        watcher.EnableRaisingEvents = true; _watchers[library.Id] = new Watched(config.Revision, watcher);
                        _changed[library.Id] = (DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                    }
                    catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { logger.LogDebug("Kütüphane izleyicisi bekliyor: {Id}", library.Id); }
                }
                foreach (var (id, change) in _changed.ToArray())
                {
                    var now = DateTimeOffset.UtcNow;
                    if (now - change.Last < TimeSpan.FromSeconds(2) && now - change.First < TimeSpan.FromSeconds(30)) continue;
                    if (_changed.TryRemove(id, out _) && store.GetLibrary(id) is not null && store.GetLibraryConfiguration(id).Options.WatchChanges) { store.QueueScan(id); events.Publish("jobs.changed"); }
                }
                await Task.Delay(1000, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { foreach (var value in _watchers.Values) value.Watcher.Dispose(); _watchers.Clear(); }
    }
    private void Remove(string id) { if (_watchers.Remove(id, out var value)) value.Watcher.Dispose(); _changed.TryRemove(id, out _); }
}

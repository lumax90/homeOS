using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Scanning;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class TestWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "opencode", "home-tests", Guid.NewGuid().ToString("N"));
    public string Source => Path.Combine(Root, "media");
    public HomePaths Paths { get; }
    public MediaStore Store { get; }
    public LibraryScanner Scanner { get; }

    public TestWorkspace()
    {
        Directory.CreateDirectory(Source);
        Paths = new HomePaths(Path.Combine(Root, "data"), Path.Combine(Root, "data", "thumbnails"), null);
        Store = new MediaStore(Paths);
        Store.Initialize();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Home:FfmpegPath"] = Path.Combine(Root, "no-ffmpeg"),
            ["Home:FfprobePath"] = Path.Combine(Root, "no-ffprobe")
        }).Build();
        Scanner = new LibraryScanner(Store, new MediaTools(config, Paths, NullLogger<MediaTools>.Instance), new EventHub());
    }

    public Library AddLibrary() => Store.AddLibrary(new AddLibrary("Test kütüphanesi", Source, "movies"));
    public string Write(string name, byte[]? content = null)
    {
        var path = Path.Combine(Source, name);
        File.WriteAllBytes(path, content ?? [1, 2, 3, 4]);
        return path;
    }

    public async Task Scan(string id)
    {
        Store.QueueScan(id);
        var job = Store.TryClaimJob();
        Assert.NotNull(job);
        await Scanner.ScanAsync(job, CancellationToken.None);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Root)) Directory.Delete(Root, true);
    }
}

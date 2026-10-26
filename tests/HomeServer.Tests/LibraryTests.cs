using System.Diagnostics;
using HomeServer.Core;

namespace HomeServer.Tests;

public sealed class LibraryTests
{
    [Fact]
    public async Task RescanningUpdatesTheSameIdentityWithoutTouchingSourceFiles()
    {
        using var home = new TestWorkspace();
        var path = home.Write("İstanbul.Dağları.mp4");
        home.Write("notes.txt");
        var library = home.AddLibrary();
        await home.Scan(library.Id);
        var first = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        Assert.Equal("İstanbul Dağları", first.Title);
        File.WriteAllBytes(path, [7, 6, 5, 4, 3, 2]);
        await home.Scan(library.Id);
        var second = Assert.Single(home.Store.GetCatalog("ISTANBUL DAGLARI", null, null, 0, 60).Items);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(6, second.SizeBytes);
        Assert.Empty(home.Store.GetCatalog("%", null, null, 0, 60).Items);
        Assert.Empty(home.Store.GetCatalog("_", null, null, 0, 60).Items);
        Assert.Equal(new byte[] { 7, 6, 5, 4, 3, 2 }, File.ReadAllBytes(path));
        Assert.Equal(2, Directory.GetFiles(home.Source).Length);
    }

    [Fact]
    public async Task UnavailableRootDoesNotEraseOrMarkExistingItemsMissing()
    {
        using var home = new TestWorkspace();
        home.Write("Coast.mp4");
        var library = home.AddLibrary();
        await home.Scan(library.Id);
        Directory.Move(home.Source, Path.Combine(home.Root, "offline"));
        home.Store.QueueScan(library.Id);
        var job = home.Store.TryClaimJob()!;
        await Assert.ThrowsAsync<DomainException>(() => home.Scanner.ScanAsync(job, CancellationToken.None));
        var item = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        Assert.True(item.Available);
    }

    [Fact]
    public async Task ManagedDownloadRescanFindsManualNamedFoldersWithoutImportMarkersAndPreservesExistingMedia()
    {
        using var home=new TestWorkspace();var root=Path.Combine(home.Paths.Data,"downloads","library");Directory.CreateDirectory(root);
        var imported=Path.Combine(root,new string('a',32));Directory.CreateDirectory(imported);var original=Path.Combine(imported,"ORIGINAL Imported.mp4");await File.WriteAllBytesAsync(original,[1,2,3,4]);
        var library=home.Store.EnsureDownloadLibrary(root);await home.Scan(library.Id);var first=Assert.Single(home.Store.GetCatalog(null,null,null,0,60).Items);
        home.Store.SaveProbe(first.Id,new(100,320,180,"h264","aac"),false);home.Store.SaveProgress(first.Id,new(27,100));
        var files=new List<string>();
        foreach(var title in new[]{"ORIGINAL Manual One (1998)","ORIGINAL Manual Two (1999)"})
        {
            var directory=Path.Combine(root,title);Directory.CreateDirectory(directory);var file=Path.Combine(directory,title+".mkv");await File.WriteAllBytesAsync(file,[9,8,7]);files.Add(file);
            Assert.False(File.Exists(Path.Combine(directory,".home-import.json")));
        }
        // Copying bytes is not a download/import command and does not itself queue a scan.
        Assert.DoesNotContain(home.Store.GetJobs(),job=>job.Status=="queued");Assert.Single(home.Store.GetCatalog(null,null,null,0,60).Items);
        await home.Scan(library.Id);await home.Scan(library.Id);
        var catalog=home.Store.GetCatalog(null,null,null,0,60);Assert.Equal(3,catalog.Total);Assert.All(catalog.Items,item=>Assert.True(item.Available));
        Assert.Equal(27,home.Store.GetMedia(first.Id)!.PositionSeconds);Assert.Equal(first.RelativePath,home.Store.GetMedia(first.Id)!.RelativePath);
        Assert.Equal(new byte[]{1,2,3,4},await File.ReadAllBytesAsync(original));foreach(var file in files)Assert.Equal(new byte[]{9,8,7},await File.ReadAllBytesAsync(file));
        Assert.False(home.Store.GetLibraryConfiguration(library.Id).Options.WatchChanges);
    }

    [Fact]
    public async Task MissingFileIsRetainedAndRecoversWithTheSameIdentity()
    {
        using var home = new TestWorkspace();
        var path = home.Write("Return.mp4");
        home.Write("Stays.mp4");
        var library = home.AddLibrary();
        await home.Scan(library.Id);
        var item = Assert.Single(home.Store.GetCatalog("Return", null, null, 0, 60).Items);
        home.Store.SaveProgress(item.Id, new PlaybackProgress(9, 40));
        File.Delete(path);
        await home.Scan(library.Id);
        Assert.False(home.Store.GetMedia(item.Id)!.Available);
        Assert.Equal(2, home.Store.GetCatalog(null, null, null, 0, 60).Total);
        home.Write("Return.mp4");
        await home.Scan(library.Id);
        Assert.True(home.Store.GetMedia(item.Id)!.Available);
        Assert.Equal(9, home.Store.GetMedia(item.Id)!.PositionSeconds);
    }

    [Fact]
    public async Task CancelRequestedBeforeCommitPreventsMissingFileReconciliation()
    {
        using var home = new TestWorkspace();
        home.Write("Kept.mp4");
        var library = home.AddLibrary();
        await home.Scan(library.Id);
        home.Store.QueueScan(library.Id);
        var job = home.Store.TryClaimJob()!;
        home.Store.RequestCancellation(job.Id);
        Assert.Throws<OperationCanceledException>(() => home.Store.FinishScan(library.Id, "new-attempt", job.Id, 0, 0));
        Assert.True(Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items).Available);
        home.Store.RecoverInterruptedJobs();
        Assert.Equal("cancelled", home.Store.GetJob(job.Id)!.Status);
        Assert.Null(home.Store.TryClaimJob());
    }

    [Fact]
    public async Task RestartedScanUsesANewVisibilityToken()
    {
        using var home = new TestWorkspace();
        var path = home.Write("Gone.mp4");
        var library = home.AddLibrary();
        await home.Scan(library.Id);
        var item = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        var oldSource = MediaPaths.Inspect(home.Source, path)!;
        home.Store.QueueScan(library.Id);
        var interrupted = home.Store.TryClaimJob()!;
        home.Store.UpsertFile(library.Id, oldSource, "old-interrupted-attempt");
        File.Delete(path);
        home.Store.RecoverInterruptedJobs();
        var recovered = home.Store.TryClaimJob()!;
        Assert.Equal(interrupted.Id, recovered.Id);
        Assert.Equal(2, recovered.Attempt);
        await home.Scanner.ScanAsync(recovered, CancellationToken.None);
        Assert.False(home.Store.GetMedia(item.Id)!.Available);
        Assert.Equal("completed", home.Store.GetJob(recovered.Id)!.Status);
    }

    [Fact]
    public async Task ConcurrentScanRequestsShareOneDurableJob()
    {
        using var home = new TestWorkspace();
        var library = home.AddLibrary();
        var jobs = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => home.Store.QueueScan(library.Id))));
        Assert.Single(jobs.Select(job => job.Id).Distinct());
        Assert.Single(home.Store.GetJobs());
        home.Store.RequestCancellation(jobs[0].Id);
        Assert.Equal("cancelled", home.Store.GetJob(jobs[0].Id)!.Status);
        Assert.Null(home.Store.TryClaimJob());
    }

    [Fact]
    public async Task ProgressIsValidatedAndClampedToProbedDuration()
    {
        using var home = new TestWorkspace();
        home.Write("Test.mp4");
        var library = home.AddLibrary();
        await home.Scan(library.Id);
        var item = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items);
        Assert.Throws<DomainException>(() => home.Store.SaveProgress(item.Id, new PlaybackProgress(-1, 10)));
        Assert.Throws<DomainException>(() => home.Store.SaveProgress(item.Id, new PlaybackProgress(double.NaN, 10)));
        home.Store.SaveProbe(item.Id, new ProbeResult(120, 1280, 720, "h264", "aac"), false);
        home.Store.SaveProgress(item.Id, new PlaybackProgress(500, 999));
        Assert.Equal(120, home.Store.GetMedia(item.Id)!.PositionSeconds);
        Assert.True(home.Store.GetMedia(item.Id)!.IsWatched);
    }

    [Fact]
    public void PlaybackPathRejectsTraversalAndReparsePointAncestors()
    {
        using var home = new TestWorkspace();
        var outside = Path.Combine(home.Root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "private.mp4"), [1, 2, 3]);
        Assert.Throws<DomainException>(() => MediaPaths.ResolveFile(home.Source, "../outside/private.mp4"));
        Assert.Throws<DomainException>(() => MediaPaths.ResolveFile(home.Source, Path.Combine(outside, "private.mp4")));
        var link = Path.Combine(home.Source, "linked");
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
            {
                Arguments = $"/c mklink /J \"{link}\" \"{outside}\"",
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
            })!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, outside);
        try { Assert.Throws<DomainException>(() => MediaPaths.ResolveFile(home.Source, Path.Combine("linked", "private.mp4"))); }
        finally { Directory.Delete(link); }
    }
}

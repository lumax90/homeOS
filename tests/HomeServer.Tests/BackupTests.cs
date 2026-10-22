using System.IO.Compression;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;
using HomeServer.Music;
using Microsoft.AspNetCore.DataProtection;

namespace HomeServer.Tests;

public sealed class BackupTests
{
    [Fact]
    public async Task OnlineBackupRestoresStateAndKeysTogetherWithoutReplacingThePreviousState()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Original", "original-hash"));
        home.Write("Movie.mp4"); var library = home.AddLibrary(); await home.Scan(library.Id);
        var media = Assert.Single(home.Store.GetCatalog(null, null, null, 0, 60).Items); home.Store.SaveProgress(media.Id, new PlaybackProgress(37, 120));
        var protector = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(home.Paths.State, "keys")), config => config.SetApplicationName("HomeServer")).CreateProtector("backup-fixture");
        home.Store.SetSetting("protected-value", protector.Protect("retained-secret-fixture"));
        var content = home.Store.RequestContent(new ProviderContent("tmdb", 42, "movie", "Fixture", "Fixture", "", "2020-01-01", null, null, "https://www.themoviedb.org/movie/42", []), "all", "balanced");
        home.Store.SaveAutomationRule(content.Id, new AutomationInput(Enabled: true, AutoGrab: true));
        home.Store.AddDownload(["fixture-hash"], "Fixture", "torrent", "encrypted-fixture", 4);
        var service = new BackupService(home.Paths, home.Store); var backup = await service.CreateAsync(Guid.NewGuid().ToString("N"), CancellationToken.None);
        var destination = Path.Combine(home.Root, "restored"); var paths = new HomePaths(destination, Path.Combine(destination, "thumbnails"), null);
        var current = new MediaStore(paths); current.Initialize(); current.CreateOwner(new Owner("Current", "current-hash"));
        var restore = new BackupService(paths, current);
        RestorePlan plan;
        await using (var input = File.OpenRead(service.FilePath(backup.Id))) plan = await restore.PrepareRestoreAsync(input, CancellationToken.None);
        Assert.Equal("Current", current.GetOwner()!.DisplayName);
        using (var lease = new AppDataLease(destination)) StateFolders.ActivatePending(destination);
        var restoredPaths = paths with { StatePath = StateFolders.Current(destination) };
        var restored = new MediaStore(restoredPaths); restored.Initialize();
        Assert.Equal("Original", restored.GetOwner()!.DisplayName);
        Assert.Equal(37, restored.GetMedia(media.Id)!.PositionSeconds);
        Assert.Equal("Current", new MediaStore(paths).GetOwner()!.DisplayName);
        var recovered = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(restoredPaths.State, "keys")), config => config.SetApplicationName("HomeServer")).CreateProtector("backup-fixture");
        Assert.Equal("retained-secret-fixture", recovered.Unprotect(restored.GetSetting("protected-value")!));
        Assert.False(restored.GetAutomationRule(content.Id).Settings.Enabled);
        Assert.Equal("paused", Assert.Single(restored.GetDownloads()).DesiredState);
        Assert.Equal(plan.Id, StateFolders.ActiveId(destination));
        Assert.Null(restore.PendingRestore());
    }

    [Fact]
    public async Task MusicPreferencesAndFavoriteStationCategoriesSurviveBackupRestoreWithoutMediaPayloads()
    {
        using var home=new TestWorkspace();var(actor,login)=MusicExperienceTests.Owner(home);var prefs=new MusicExperiencePreferences(home.Paths,home.Store,new EventHub());prefs.Save(actor,login,new(OnlineMetadata:true,Scene:"aquarium",ShowNowPlaying:false));
        using var stations=new RadioDirectory(home.Paths,prefs,new MusicExperienceTests.Handler(_=>throw new InvalidOperationException("No network.")));var station=new RadioStation(Guid.NewGuid().ToString(),"Isolated Radio","https://fixture.invalid/radio","TR","turkish",["jazz"],"MP3",128,false);stations.Favorite(actor.Id,station,new(true,"Living room"));
        var backupService=new BackupService(home.Paths,home.Store);var backup=await backupService.CreateAsync(Guid.NewGuid().ToString("N"),CancellationToken.None);using(var archive=ZipFile.OpenRead(backupService.FilePath(backup.Id))){Assert.NotNull(archive.GetEntry("music/preferences/1.json"));Assert.NotNull(archive.GetEntry("music/radio/1.json"));Assert.DoesNotContain(archive.Entries,entry=>entry.Name.EndsWith(".mp3",StringComparison.Ordinal));}
        var data=Path.Combine(home.Root,"restored-music");var paths=new HomePaths(data,Path.Combine(data,"thumbnails"),null);var store=new MediaStore(paths);store.Initialize();var restore=new BackupService(paths,store);await using(var file=File.OpenRead(backupService.FilePath(backup.Id)))await restore.PrepareRestoreAsync(file,CancellationToken.None);StateFolders.ActivatePending(data);
        var restoredPaths=paths with{StatePath=StateFolders.Current(data)};var restoredStore=new MediaStore(restoredPaths);restoredStore.Initialize();var restoredPrefs=new MusicExperiencePreferences(restoredPaths,restoredStore,new EventHub());Assert.Equal("aquarium",restoredPrefs.Read(actor.Id).Scene);Assert.True(restoredPrefs.Read(actor.Id).OnlineMetadata);Assert.False(restoredPrefs.Read(actor.Id).ShowNowPlaying);
        using var restoredDirectory=new RadioDirectory(restoredPaths,restoredPrefs,new MusicExperienceTests.Handler(_=>throw new InvalidOperationException("No network.")));Assert.Equal("Living room",Assert.Single(restoredDirectory.Favorites(actor.Id)).Category);Assert.Empty(restoredStore.ActiveMusicRadioSessions());
    }
    [Fact]
    public async Task TraversalAndTamperingAreRejectedBeforeActivation()
    {
        using var home = new TestWorkspace(); home.Store.CreateOwner(new Owner("Original", "hash"));
        var service = new BackupService(home.Paths, home.Store);
        using var unsafeZip = new MemoryStream();
        using (var archive = new ZipArchive(unsafeZip, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("../escaped.txt").Open()); writer.Write("escape");
        }
        unsafeZip.Position = 0;
        await Assert.ThrowsAsync<DomainException>(() => service.PrepareRestoreAsync(unsafeZip, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(home.Paths.Data, "escaped.txt")));
        var info = await service.CreateAsync(Guid.NewGuid().ToString("N"), CancellationToken.None);
        using var bytes = new MemoryStream(await File.ReadAllBytesAsync(service.FilePath(info.Id)));
        var plan = await service.PrepareRestoreAsync(bytes, CancellationToken.None);
        File.AppendAllText(Path.Combine(home.Paths.Data, "states", plan.Id, "home.db"), "tampered");
        Assert.Throws<InvalidOperationException>(() => StateFolders.ActivatePending(home.Paths.Data));
        Assert.Null(StateFolders.ActiveId(home.Paths.Data));
        Assert.Equal("Original", home.Store.GetOwner()!.DisplayName);
        service.CancelRestore();
    }
}

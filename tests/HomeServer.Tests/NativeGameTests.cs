using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServer.Tests;

public sealed class NativeGameTests
{
    // Synthetic standard header, exclusively in isolated tests. Not executable gameplay or a demo catalog entry.
    private static byte[] FixtureRom()
    {
        var bytes = new byte[512]; bytes[3] = 0xea; bytes[0xb2] = 0x96;
        var sum = 0x19; for (var index = 0xa0; index <= 0xbc; index++) sum += bytes[index]; bytes[0xbd] = (byte)-sum;
        return bytes;
    }
    private static (Account Actor, LoginSession Login) Owner(TestWorkspace home)
    {
        home.Store.CreateOwner(new("Owner", "isolated-hash")); var actor = home.Store.GetAccount("1")!;
        return (actor, home.Store.CreateLoginSession(actor.Id, home.Store.GetAccountSecret(actor.Id)!.Version, "isolated-ROM-test"));
    }
    private static Task<GameRom> Import(NativeRomLibrary library, byte[] content, Account actor, LoginSession login, string name = "Isolated fixture.gba") => library.ImportAsync(new MemoryStream(content), name, content.Length, actor, login.Id, CancellationToken.None);
    [Fact]
    public async Task ImportCreatesVerifiedManagedCopyAndIdenticalRetryPreservesIdentityAndPreferences()
    {
        using var home = new TestWorkspace(); var (actor, login) = Owner(home); using var library = new NativeRomLibrary(home.Paths, home.Store); var bytes = FixtureRom();
        var source = home.Write("Isolated fixture.gba", bytes); var sourceHash = StateFolders.Hash(source);
        await using var stream = File.OpenRead(source); var rom = await library.ImportAsync(stream, Path.GetFileName(source), stream.Length, actor, login.Id, CancellationToken.None);
        Assert.Equal(sourceHash, rom.Sha256); Assert.Equal(sourceHash, StateFolders.Hash(source)); Assert.Equal("Isolated fixture", rom.Title);
        home.Store.SetGamePreference(rom.Id, new(false, true), actor);
        var retry = await Import(library, bytes, actor, login, "Renamed fixture.gba"); Assert.Equal(rom, retry);
        Assert.Single(home.Store.GetGameRoms(actor)); Assert.True(Assert.Single(home.Store.GetGameStates(actor.Id)).Favorite); Assert.False(Assert.Single(home.Store.GetGameStates(actor.Id)).InLibrary);
        await using var verified = library.OpenVerified(rom); Assert.Equal(bytes, await ReadAll(verified)); Assert.Empty(home.Store.GetActiveGameSessions(actor)); Assert.Empty(home.Store.GetProductJobs(actor, "game-install"));
        Assert.Empty(Directory.GetDirectories(Path.Combine(home.Paths.State, "games", "rom-staging")));
    }
    [Fact]
    public async Task IdenticalContentInDifferentProfilesHasPrivateIdentityAndSharedReadonlyVerifiedPayload()
    {
        using var home = new TestWorkspace(); var (owner, ownerLogin) = Owner(home);
        var member = home.Store.SaveAccount(null, new("Member", CanAcquire: true), "isolated-member-hash"); var memberLogin = home.Store.CreateLoginSession(member.Id, home.Store.GetAccountSecret(member.Id)!.Version, "isolated-member-ROM");
        using var library = new NativeRomLibrary(home.Paths, home.Store); var first = await Import(library, FixtureRom(), owner, ownerLogin); Assert.Empty(home.Store.GetGameRoms(member)); Assert.Null(home.Store.GetGameRom(first.Id, member));
        var second = await Import(library, FixtureRom(), member, memberLogin); Assert.NotEqual(first.Id, second.Id); Assert.Equal(first.Sha256, second.Sha256);
        Assert.Null(home.Store.GetGameRom(second.Id, owner)); Assert.Single(home.Store.GetGameRoms(owner)); Assert.Single(home.Store.GetGameRoms(member));
    }
    [Theory]
    [InlineData("../outside.gba")]
    [InlineData("C:\\outside.gba")]
    [InlineData("folder/game.gba")]
    [InlineData(".gba")]
    [InlineData("game.exe")]
    [InlineData("game.zip")]
    [InlineData("BIOS.bin")]
    [InlineData("game..gba")]
    public async Task UnsafeOrUnsupportedNamesCannotCreateAnyGameData(string name)
    {
        using var home = new TestWorkspace(); var (actor, login) = Owner(home); using var library = new NativeRomLibrary(home.Paths, home.Store);
        Assert.Equal(400, (await Assert.ThrowsAsync<DomainException>(() => Import(library, FixtureRom(), actor, login, name))).StatusCode);
        Assert.Empty(home.Store.GetGameRoms(actor)); Assert.False(Directory.Exists(Path.Combine(home.Paths.State, "games")));
    }
    [Fact]
    public async Task RenamedExecutableOrInvalidHeaderCannotEnterRomLibrary()
    {
        using var home = new TestWorkspace(); var (actor, login) = Owner(home); using var library = new NativeRomLibrary(home.Paths, home.Store); var bytes = new byte[512]; bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        Assert.Equal(422, (await Assert.ThrowsAsync<DomainException>(() => Import(library, bytes, actor, login))).StatusCode); Assert.Empty(home.Store.GetGameRoms(actor));
    }
    [Fact]
    public async Task TruncationAndDeclaredOversizeAreRejectedBeforePublication()
    {
        using var home = new TestWorkspace(); var (actor, login) = Owner(home); using var library = new NativeRomLibrary(home.Paths, home.Store);
        Assert.Equal(413, (await Assert.ThrowsAsync<DomainException>(() => library.ImportAsync(Stream.Null, "fixture.gba", NativeRomLibrary.MaximumBytes + 1, actor, login.Id, CancellationToken.None))).StatusCode);
        Assert.Equal(422, (await Assert.ThrowsAsync<DomainException>(() => library.ImportAsync(new MemoryStream(FixtureRom()), "fixture.gba", 1024, actor, login.Id, CancellationToken.None))).StatusCode);
        Assert.Empty(home.Store.GetGameRoms(actor));
    }
    [Fact]
    public async Task UnknownLengthStillHasABoundedStreamLimit()
    {
        using var home = new TestWorkspace(); var (actor, login) = Owner(home); using var library = new NativeRomLibrary(home.Paths, home.Store);
        await using var input = new SizedStream(NativeRomLibrary.MaximumBytes + 1);
        Assert.Equal(413, (await Assert.ThrowsAsync<DomainException>(() => library.ImportAsync(input, "fixture.gba", null, actor, login.Id, CancellationToken.None))).StatusCode);
        Assert.Empty(home.Store.GetGameRoms(actor)); Assert.Empty(Directory.GetDirectories(Path.Combine(home.Paths.State, "games", "rom-staging")));
    }
    [Fact]
    public async Task RevocationDuringUploadCannotPublishOrCreateARecord()
    {
        using var home = new TestWorkspace(); var (actor, login) = Owner(home); using var library = new NativeRomLibrary(home.Paths, home.Store);
        await using var input = new InterceptStream(FixtureRom(), () => home.Store.RevokeLoginSession(login.Id, actor.Id));
        Assert.Equal(401, (await Assert.ThrowsAsync<DomainException>(() => library.ImportAsync(input, "fixture.gba", 512, actor, login.Id, CancellationToken.None))).StatusCode);
        Assert.Empty(home.Store.GetGameRoms(actor)); Assert.Empty(Directory.GetFiles(Path.Combine(home.Paths.State, "games"), "*.gba", SearchOption.AllDirectories)); Assert.Empty(Directory.GetFiles(Path.Combine(home.Paths.State, "games"), "*.partial", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task NoAcquisitionPermissionAndRevokedLoginRejectImport()
    {
        using var home = new TestWorkspace(); var (actor, login) = Owner(home); var viewer = home.Store.SaveAccount(null, new("Viewer", Role: "viewer"), "isolated-viewer"); var viewerLogin = home.Store.CreateLoginSession(viewer.Id, home.Store.GetAccountSecret(viewer.Id)!.Version, "isolated-viewer");
        using var library = new NativeRomLibrary(home.Paths, home.Store);
        Assert.Equal(403, (await Assert.ThrowsAsync<DomainException>(() => Import(library, FixtureRom(), viewer, viewerLogin))).StatusCode);
        home.Store.RevokeLoginSession(login.Id, actor.Id); Assert.Equal(401, (await Assert.ThrowsAsync<DomainException>(() => Import(library, FixtureRom(), actor, login))).StatusCode); Assert.Empty(home.Store.GetGameRoms(actor));
    }
    [Fact]
    public async Task ConflictingContentAddressedTargetIsNeverOverwritten()
    {
        using var home = new TestWorkspace(); var (actor, login) = Owner(home); var bytes = FixtureRom(); var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var target = AppFiles.DirectoryIn(home.Paths.State, "games", "roms", "gba", hash); var file = Path.Combine(target, "game.gba"); File.WriteAllText(file, "unrelated existing bytes");
        using var library = new NativeRomLibrary(home.Paths, home.Store);
        Assert.Equal(409, (await Assert.ThrowsAsync<DomainException>(() => Import(library, bytes, actor, login))).StatusCode); Assert.Equal("unrelated existing bytes", File.ReadAllText(file)); Assert.Empty(home.Store.GetGameRoms(actor));
    }
    [Fact]
    public async Task CancelledUploadDoesNotLeaveARecordOrOwnedAttempt()
    {
        using var home = new TestWorkspace(); var (actor, login) = Owner(home); using var library = new NativeRomLibrary(home.Paths, home.Store); using var cancel = new CancellationTokenSource();
        await using var input = new InterceptStream(FixtureRom(), cancel.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.ImportAsync(input, "fixture.gba", 512, actor, login.Id, cancel.Token)); Assert.Empty(home.Store.GetGameRoms(actor)); Assert.Empty(Directory.GetDirectories(Path.Combine(home.Paths.State, "games", "rom-staging")));
    }
    [Fact]
    public async Task OnlyOneUploadCanOccupyTheBoundedStagingLane()
    {
        using var home = new TestWorkspace(); var (actor, login) = Owner(home); using var library = new NativeRomLibrary(home.Paths, home.Store);
        await using var input = new GatedStream(FixtureRom()); var first = library.ImportAsync(input, "fixture.gba", 512, actor, login.Id, CancellationToken.None);
        await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { Assert.Equal(409, (await Assert.ThrowsAsync<DomainException>(() => Import(library, FixtureRom(), actor, login))).StatusCode); Assert.Single(Directory.GetDirectories(Path.Combine(home.Paths.State, "games", "rom-staging"))); }
        finally { input.Release.TrySetResult(); }
        await first; Assert.Single(home.Store.GetGameRoms(actor));
    }
    [Fact]
    public async Task ChangedVerifiedPayloadFailsClosedWithoutDeletingMetadataOrSource()
    {
        using var home = new TestWorkspace(); var (actor, login) = Owner(home); using var library = new NativeRomLibrary(home.Paths, home.Store); var rom = await Import(library, FixtureRom(), actor, login);
        var path = Path.Combine(home.Paths.State, "games", "roms", "gba", rom.Sha256, "game.gba"); File.WriteAllBytes(path, new byte[512]);
        Assert.Equal(409, Assert.Throws<DomainException>(() => library.OpenVerified(rom)).StatusCode); Assert.True(File.Exists(path)); Assert.Single(home.Store.GetGameRoms(actor));
    }
    [Fact]
    public async Task RestartPreservesRomPreferencesAndSchemaFortyMigrationPreservesOldGameSaves()
    {
        using var home = new TestWorkspace();var data=AppFiles.DirectoryIn(home.Root,"actual-schema40");var paths=new HomePaths(data,Path.Combine(data,"thumbnails"),null);
        using(var db=new SqliteConnection($"Data Source={Path.Combine(data,"home.db")}"))
        {
            db.Open();using var command=db.CreateCommand();command.CommandText="PRAGMA foreign_keys=OFF";command.ExecuteNonQuery();
            foreach(var name in typeof(MediaStore).Assembly.GetManifestResourceNames().Where(name=>name.Contains(".Migrations.")&&int.TryParse(name.Split(".Migrations.")[1][..3],out var number)&&number<=40).Order())
            {using var stream=typeof(MediaStore).Assembly.GetManifestResourceStream(name)!;using var reader=new StreamReader(stream);command.CommandText=reader.ReadToEnd();command.ExecuteNonQuery();if(name.EndsWith("001_initial.sql",StringComparison.Ordinal)){command.CommandText="INSERT INTO owner VALUES(1,'Owner','preserved')";command.ExecuteNonQuery();}}
            command.CommandText="PRAGMA user_version=40";command.ExecuteNonQuery();
        }
        var store=new MediaStore(paths);var actor=store.GetAccount("1")!;var login=store.CreateLoginSession("1",store.GetAccountSecret("1")!.Version,"isolated-migration");
        var oldGame = new GameManifest("tiny-crate", "Isolated old game", "Fixture", "https://fixture.invalid/", "https://fixture.invalid/", "fixture", "preserved-build.1", "Fixture", "MIT", "web", "1/1", ["puzzle"], ["home-progress.json"], []);
        var oldSession = store.StartGameSession(oldGame, new(Guid.NewGuid().ToString("N"), new string('a', 64)), actor, login.Id);
        store.EndGameSession(oldSession.Id, oldGame, new(false, 0, new() { ["home-progress.json"] = "{\"map\":7}" }), actor, login.Id);
        store.SetGamePreference("tiny-crate", new(true, true), actor);
        store.Initialize(); store.Initialize(); Assert.True(Assert.Single(store.GetGameStates(actor.Id)).Favorite); Assert.Empty(store.GetGameRoms(actor)); Assert.Equal("{\"map\":7}", store.GetGameCheckpoint(oldGame.Id, oldGame.Version, actor.Id).Files["home-progress.json"]);
        using var library = new NativeRomLibrary(paths, store); var rom = await Import(library, FixtureRom(), actor, login);
        var restarted = new MediaStore(paths); restarted.Initialize(); Assert.Equal(rom, Assert.Single(restarted.GetGameRoms(actor))); Assert.Equal(2, restarted.GetGameStates(actor.Id).Count);
        using var verified = new SqliteConnection($"Data Source={Path.Combine(paths.State, "home.db")}"); verified.Open(); using var check = verified.CreateCommand(); check.CommandText = "PRAGMA user_version"; Assert.Equal((long)MediaStore.LatestSchema,check.ExecuteScalar()); check.CommandText = "PRAGMA foreign_key_check"; using var rows = check.ExecuteReader(); Assert.False(rows.Read());
    }
    [Fact]
    public void RuntimeCandidateInventoryCannotBeMistakenForAWorkingStream()
    {
        using var home = new TestWorkspace(); var status = new NativeGamingRuntime(home.Paths).Status();
        Assert.False(status.ExecutionAvailable); Assert.False(status.StreamingImplemented); Assert.Equal(["gba"], status.ImportSystems); Assert.Equal(3, status.Components.Length); Assert.Contains("capture-isolation-not-configured", status.Blockers);
        var candidate = NativeGamingRuntime.Candidates.Single(value => value.Id == "sunshine"); var directory = AppFiles.DirectoryIn(home.Paths.State, "games", "native-archives", candidate.Id); File.WriteAllText(Path.Combine(directory, candidate.ArchiveFileName!), "not a valid upstream package");
        var invalid = new NativeGamingRuntime(home.Paths).Status(); Assert.Equal("invalid-archive", invalid.Components.Single(value => value.Candidate.Id == "sunshine").Status); Assert.False(invalid.ExecutionAvailable);
    }
    [Fact]
    public async Task ApiRequiresActorCsrfAndOwnershipAndNeverStartsNativeExecutionOrExposesRomFiles()
    {
        using var home = new TestWorkspace(); await using var app = new Factory(home); using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/games/native/runtime")).StatusCode);
        await Csrf(client); (await client.PostAsJsonAsync("/api/setup", new Credentials("Owner", "isolated-password-2026"))).EnsureSuccessStatusCode(); await Csrf(client);
        using var file = new ByteArrayContent(FixtureRom()); file.Headers.ContentType = new("application/octet-stream"); client.DefaultRequestHeaders.Add("X-Home-Rom-Name", Uri.EscapeDataString("isolated.gba"));
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/games/roms", file)).StatusCode); await Csrf(client);
        client.DefaultRequestHeaders.Remove("X-Home-User"); client.DefaultRequestHeaders.Add("X-Home-User", "foreign"); Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/games/roms", file)).StatusCode); await Csrf(client);
        using var response = await client.PostAsync("/api/games/roms", file); response.EnsureSuccessStatusCode(); var receipt = await response.Content.ReadFromJsonAsync<JsonElement>(); var id = receipt.GetProperty("id").GetString()!; Assert.False(receipt.GetProperty("playable").GetBoolean());
        var game = await client.GetFromJsonAsync<GameEntry>("/api/games/" + id); Assert.Equal("native", game!.Kind); Assert.False(game.Playable); Assert.NotNull(game.RomSha256); Assert.DoesNotContain(home.Root, JsonSerializer.Serialize(game));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/games/" + id + "/sessions", new GameStart(Guid.NewGuid().ToString("N"), new string('a', 64)))).StatusCode);
        Assert.Empty(home.Store.GetActiveGameSessions()); Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/games/" + id + "/file")).StatusCode);
        Assert.False((await client.GetFromJsonAsync<JsonElement>("/api/games/native/runtime")).GetProperty("executionAvailable").GetBoolean());
    }
    private static async Task<byte[]> ReadAll(Stream input) { using var output = new MemoryStream(); await input.CopyToAsync(output); return output.ToArray(); }
    [Fact]
    public async Task BrowserStyleCueBinUploadWithBinFirstAndCaseDifferenceUsesAuthenticatedBoundedApi()
    {
        using var home=new TestWorkspace();await using var app=new Factory(home);using var client=app.CreateClient();await Csrf(client);(await client.PostAsJsonAsync("/api/setup",new Credentials("Owner","isolated-password-2026"))).EnsureSuccessStatusCode();await Csrf(client);
        var cue=System.Text.Encoding.UTF8.GetBytes("FILE \"original track.bin\" BINARY\r\n TRACK 01 MODE2/2352\r\n INDEX 01 00:00:00\r\n");
        var probe=new NativeRomProbeRequest([new("Original.cue",cue.Length,Convert.ToBase64String(cue)),new("Original Track.BIN",1024*1024)],"Original.cue");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/games/roms/detect",probe)).StatusCode);await Csrf(client);
        client.DefaultRequestHeaders.Remove("X-Home-User");client.DefaultRequestHeaders.Add("X-Home-User","foreign");Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/games/roms/detect",probe)).StatusCode);await Csrf(client);
        var detected=await client.PostAsJsonAsync("/api/games/roms/detect",probe);detected.EnsureSuccessStatusCode();var hint=(await detected.Content.ReadFromJsonAsync<NativeRomProbeResult>())!;Assert.Null(hint.SystemId);Assert.Empty(hint.MissingFiles);Assert.Empty(home.Store.GetGameRoms(home.Store.GetAccount("1")!));
        using var body=new MultipartFormDataContent("----OriginalBrowserBoundary");var track=new byte[1024*1024];
        foreach(var (name,bytes) in new[]{("Original Track.BIN",track),("Original.cue",cue)}){var part=new ByteArrayContent(bytes);part.Headers.ContentDisposition=new("form-data"){Name="\"files\"",FileName="\""+name+"\""};body.Add(part);}
        client.DefaultRequestHeaders.Add("X-Home-Rom-System","ps1");client.DefaultRequestHeaders.Add("X-Home-Rom-Name",Uri.EscapeDataString("Original.cue"));
        using var response=await client.PostAsync("/api/games/roms",body);response.EnsureSuccessStatusCode();var actor=home.Store.GetAccount("1")!;var rom=Assert.Single(home.Store.GetGameRoms(actor));Assert.Equal("ps1",rom.System);Assert.Equal("Original.cue",rom.FileName);
        using var library=new NativeRomLibrary(home.Paths,home.Store);var stage=AppFiles.DirectoryIn(home.Root,"isolated-disc-runtime");Assert.Equal("game.cue",await library.StageContentAsync(rom,stage,CancellationToken.None));
        Assert.Contains("FILE \"Original Track.BIN\" BINARY",await File.ReadAllTextAsync(Path.Combine(stage,"game.cue")));Assert.Equal(cue,await File.ReadAllBytesAsync(Path.Combine(home.Paths.State,"games","roms","ps1",rom.Sha256,"Original.cue")));Assert.Equal(track,await File.ReadAllBytesAsync(Path.Combine(stage,"Original Track.BIN")));Assert.Empty(home.Store.GetActiveGameSessions());
        var native=AppFiles.DirectoryIn(home.Root,"native-disc-runtime");Assert.Equal("game.cue",await library.StageContentAsync(rom,native,CancellationToken.None,canonicalizeCueTracks:true));Assert.Equal(track,await File.ReadAllBytesAsync(Path.Combine(native,"game.bin")));Assert.Contains("FILE \"game.bin\" BINARY",await File.ReadAllTextAsync(Path.Combine(native,"game.cue")));Assert.Equal((byte)'F',(await File.ReadAllBytesAsync(Path.Combine(native,"game.cue")))[0]);Assert.Equal(cue,await File.ReadAllBytesAsync(Path.Combine(home.Paths.State,"games","roms","ps1",rom.Sha256,"Original.cue")));
    }
    [Fact]
    public async Task SetupApiRequiresAuthenticationCsrfMatchingHomeActorAndPreparedSoftware()
    {
        using var home = new TestWorkspace(); await using var app = new Factory(home); using var client = app.CreateClient();
        await Csrf(client); Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/games/native/setup", new NativeSetupCommand(true,true))).StatusCode);
        (await client.PostAsJsonAsync("/api/setup", new Credentials("Owner", "isolated-password-2026"))).EnsureSuccessStatusCode(); await Csrf(client);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/games/native/setup", new NativeSetupCommand(true,true))).StatusCode); await Csrf(client);
        client.DefaultRequestHeaders.Remove("X-Home-User"); client.DefaultRequestHeaders.Add("X-Home-User","foreign");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/games/native/setup", new NativeSetupCommand(true,true))).StatusCode); await Csrf(client);
        // Authenticated remote owners may configure; missing software must still block activation.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/games/native/setup", new NativeSetupCommand(true,true))).StatusCode);
        Assert.Null(NativeRuntimeProfile.Read(home.Paths)); Assert.Empty(home.Store.GetActiveGameSessions());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeldInputFailureBlocksCatalogStatusAndNativeLaunchWithoutOpeningASession(bool assertedEvidence)
    {
        using var home = new TestWorkspace(); await using var app = new Factory(home); using var client = app.CreateClient();
        await Csrf(client); (await client.PostAsJsonAsync("/api/setup", new Credentials("Owner", "isolated-password-2026"))).EnsureSuccessStatusCode(); await Csrf(client);
        var actor = home.Store.GetAccount("1")!; var login = home.Store.CreateLoginSession(actor.Id, home.Store.GetAccountSecret(actor.Id)!.Version, "isolated-gate-test");
        using var library = new NativeRomLibrary(home.Paths, home.Store); var rom = await Import(library, FixtureRom(), actor, login);
        var asset = new NativeRuntimeFile(Path.Combine(home.Root, "never-launched.exe"), 1, new string('a', 64));
        var profile = new NativeRuntimeProfile(1, true, "0.10.5", "26b7884bc25a5933960f3cdcd98bac1ae14d42e2",
            asset with { Sha256 = NativeRuntimeProfile.RejectedRemotePadSha256 }, asset, asset, asset, [], assertedEvidence);
        File.WriteAllText(Path.Combine(home.Paths.State, "games", "native-runtime-profile.json"), JsonSerializer.Serialize(profile, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var entry = await client.GetFromJsonAsync<GameEntry>("/api/games/" + rom.Id);
        Assert.False(entry!.Playable); Assert.Equal(NativeRuntimeProfile.InputBlockReason, entry.BlockReason);
        var status = await client.GetFromJsonAsync<JsonElement>("/api/games/native/status");
        Assert.False(status.GetProperty("available").GetBoolean());
        Assert.Equal(NativeRuntimeProfile.InputBlockReason, app.Services.GetRequiredService<NativeRuntimeSetup>().Status(actor,true).Reason);
        var games = app.Services.GetRequiredService<NativeGameManager>();
        Assert.Equal(409, (await Assert.ThrowsAsync<DomainException>(() => games.LaunchAsync(rom.Id, new(Guid.NewGuid().ToString("N"), new string('a', 64)), actor, login.Id, CancellationToken.None))).StatusCode);
        Assert.Empty(home.Store.GetActiveGameSessions()); Assert.False(Directory.Exists(Path.Combine(home.Paths.State, "games", "native-work")));
        Assert.False(File.Exists(asset.Path)); Assert.Single(home.Store.GetGameRoms(actor));
    }
    private static async Task Csrf(HttpClient client)
    {
        var session = await client.GetFromJsonAsync<JsonElement>("/api/session");
        foreach (var (header, property) in new[] { ("X-CSRF-TOKEN", "csrfToken"), ("X-Home-User", "userId"), ("X-Home-Session", "sessionId") }) { client.DefaultRequestHeaders.Remove(header); if (session.GetProperty(property).ValueKind == JsonValueKind.String) client.DefaultRequestHeaders.Add(header, session.GetProperty(property).GetString()); }
    }
    [Fact]
    public async Task SupportedSystemsApiIsAuthenticatedReadOnlyAndDoesNotPresentTheCoreCatalogAsSupport()
    {
        using var home=new TestWorkspace();await using var app=new Factory(home,true);using var client=app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/games/supported-systems")).StatusCode);
        await Csrf(client);(await client.PostAsJsonAsync("/api/setup",new Credentials("Owner","isolated-password-2026"))).EnsureSuccessStatusCode();await Csrf(client);
        var result=await client.GetFromJsonAsync<GamingSupportView>("/api/games/supported-systems");Assert.NotNull(result);
        Assert.True(result.Items.Length<30);Assert.Equal("unsupported",result.Items.Single(item=>item.Id=="ps3").State);Assert.False(result.Items.Single(item=>item.Id=="3ds").Ready);
        Assert.Empty(home.Store.GetProductJobs(home.Store.GetAccount("1")!));Assert.Empty(home.Store.GetActiveGameSessions());
    }
    [Fact]
    public async Task ConfirmedUninstallApiChecksCsrfOwnerScopeAndActiveSessionsAndSupportsPreApplyCancellation()
    {
        using var home=new TestWorkspace();await using var app=new Factory(home,true);using var client=app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/games/rom-foreign/uninstall",new GameUninstallCommand(true))).StatusCode);
        await Csrf(client);(await client.PostAsJsonAsync("/api/setup",new Credentials("Owner","isolated-password-2026"))).EnsureSuccessStatusCode();await Csrf(client);
        var actor=home.Store.GetAccount("1")!;var login=home.Store.CreateLoginSession("1",home.Store.GetAccountSecret("1")!.Version,"uninstall-api-fixture");
        using var roms=new NativeRomLibrary(home.Paths,home.Store);var rom=await Import(roms,FixtureRom(),actor,login);
        var path=Path.Combine(home.Paths.State,"games","roms","gba",rom.Sha256,"game.gba");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync($"/api/games/{rom.Id}/uninstall",new GameUninstallCommand(true))).StatusCode);await Csrf(client);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync($"/api/games/{rom.Id}/uninstall",new GameUninstallCommand())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync($"/api/games/{rom.Id}/uninstall",new GameUninstallCommand(true,false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsJsonAsync("/api/games/rom-foreign/uninstall",new GameUninstallCommand(true))).StatusCode);
        var game=new GameManifest(rom.Id,rom.Title,"","","","","preserved-fixture-build","","user-supplied","native","16/9",[],["native-save.json"],[],"native");
        var session=home.Store.StartGameSession(game,new(Guid.NewGuid().ToString("N"),new string('a',64)),actor,login.Id);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync($"/api/games/{rom.Id}/uninstall",new GameUninstallCommand(true))).StatusCode);home.Store.CloseGameSession(session.Id,actor,login.Id);
        var response=await client.PostAsJsonAsync($"/api/games/{rom.Id}/uninstall",new GameUninstallCommand(true));Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
        var queued=(await response.Content.ReadFromJsonAsync<ProductJob>())!;Assert.True(File.Exists(path));var claimed=home.Store.ClaimProductJob(["game-uninstall"])!;
        home.Store.CancelProductJob(queued.Id,claimed.Revision,actor);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>app.Services.GetRequiredService<GameRemoval>().RunAsync(claimed,CancellationToken.None));
        Assert.True(File.Exists(path));Assert.True(home.Store.GetGameStates(actor.Id).Single().InLibrary);
    }
    [Fact]
    public async Task ScopedSettingsApiChecksActorCsrfRevisionAndGameOwnershipWithoutExecutingACore()
    {
        using var home=new TestWorkspace();await using var app=new Factory(home,true);using var client=app.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/games/rom-original/settings")).StatusCode);await Csrf(client);(await client.PostAsJsonAsync("/api/setup",new Credentials("Owner","isolated-password-2026"))).EnsureSuccessStatusCode();await Csrf(client);
        var actor=home.Store.GetAccount("1")!;var login=home.Store.CreateLoginSession("1",home.Store.GetAccountSecret("1")!.Version,"settings-fixture").Id;
        var romA=home.Store.RegisterGameRom("Original A.chd",new string('a',64),512,actor,login,"ps1");var romB=home.Store.RegisterGameRom("Original B.chd",new string('b',64),512,actor,login,"ps1");
        const string core="pcsx_rearmed",key="pcsx_rearmed_neon_enhancement_enable";var hash=new string('c',64);var info=app.Services.GetRequiredService<RetroArchCoreCatalog>().Require(core);var directory=AppFiles.DirectoryIn(home.Paths.State,"games","native-cores",core,hash);
        // Non-executable installed-receipt/schema fixtures; no prepared host exists and no core is loaded.
        File.WriteAllText(Path.Combine(directory,core+"_libretro.dll"),"ORIGINAL NONEXECUTABLE TEST FIXTURE");File.WriteAllText(Path.Combine(directory,core+"_libretro.info"),"ORIGINAL INFO FIXTURE");
        var installed=new InstalledNativeCore(1,core,RetroArchCoreCatalog.Commit,"upstream-nightly","","https://fixture.invalid/",new string('d',64),new("",33,hash),new("",21,info.InfoSha256));File.WriteAllText(Path.Combine(home.Paths.State,"games","native-cores",core,"installed.json"),JsonSerializer.Serialize(installed,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        app.Services.GetRequiredService<NativeGameSettings>().Observe("1",core,hash,[new(0,key,"Original enhanced resolution","","disabled",["disabled","enabled"])],[]);
        var change=new NativeSettingsChange("game",0,new(){{key,"enabled"}});client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync($"/api/games/{romA.Id}/settings",change)).StatusCode);await Csrf(client);client.DefaultRequestHeaders.Remove("X-Home-User");client.DefaultRequestHeaders.Add("X-Home-User","foreign");Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync($"/api/games/{romA.Id}/settings",change)).StatusCode);await Csrf(client);
        (await client.PutAsJsonAsync($"/api/games/{romA.Id}/settings",change)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync($"/api/games/{romA.Id}/settings",change)).StatusCode);var a=await client.GetFromJsonAsync<NativeGameSettingsView>($"/api/games/{romA.Id}/settings");var b=await client.GetFromJsonAsync<NativeGameSettingsView>($"/api/games/{romB.Id}/settings");Assert.Equal("enabled",a!.Options.Single().Value);Assert.Equal("disabled",b!.Options.Single().Value);Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/games/rom-foreign/settings")).StatusCode);
        (await client.GetAsync($"/api/games/{romA.Id}/metadata")).EnsureSuccessStatusCode();Assert.Empty(home.Store.GetProductJobs(actor));(await client.PostAsync($"/api/games/{romA.Id}/metadata/refresh",null)).EnsureSuccessStatusCode();Assert.Single(home.Store.GetProductJobs(actor,"rom-metadata"));Assert.Empty(home.Store.GetActiveGameSessions());Assert.Null(NativeRuntimeProfile.Read(home.Paths));
    }
    private sealed class LoopbackStartup : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next)=>app=>{app.Use(async(context,following)=>{context.Connection.RemoteIpAddress=IPAddress.Loopback;await following();});next(app);};
    }
    private sealed class Factory(TestWorkspace home,bool loopback=false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)=>builder.UseEnvironment("Testing").ConfigureAppConfiguration((_,configuration)=>configuration.AddInMemoryCollection(new Dictionary<string,string?>
        {["Home:DataPath"]=home.Paths.Data,["Home:Games:PackagePath"]=Path.Combine(home.Root,"empty-packages"),["Home:Games:StorePath"]=Path.Combine(home.Root,"empty-store"),["Home:Downloads:LoopbackOnly"]="true",["Home:FfmpegPath"]=Path.Combine(home.Root,"absent"),["Home:FfprobePath"]=Path.Combine(home.Root,"absent"),["Home:DemoPath"]=""})).ConfigureServices(services=>
        { // Fixture imports must never cause an Internet artwork/core acquisition in verification.
            foreach(var descriptor in services.Where(service=>service.ImplementationType==typeof(ProductWorker)||service.ImplementationType==typeof(RomMetadataAutomation)).ToArray())services.Remove(descriptor);
            if(loopback)services.AddSingleton<IStartupFilter,LoopbackStartup>();
        });
    }
    private sealed class InterceptStream(byte[] bytes, Action intercept) : MemoryStream(bytes)
    {
        private bool _called;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { var count = await base.ReadAsync(buffer, token); if (!_called) { _called = true; intercept(); } return count; }
    }
    private sealed class GatedStream(byte[] bytes) : MemoryStream(bytes)
    {
        private bool _entered;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { if (!_entered) { _entered = true; Entered.TrySetResult(); await Release.Task.WaitAsync(token); } return await base.ReadAsync(buffer, token); }
    }
    private sealed class SizedStream(long size) : Stream
    {
        private long _position;
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => size; public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { token.ThrowIfCancellationRequested(); var length = (int)Math.Min(buffer.Length, size - _position); buffer.Span[..length].Clear(); _position += length; return ValueTask.FromResult(length); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException(); public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

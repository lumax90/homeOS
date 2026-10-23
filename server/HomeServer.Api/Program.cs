using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Scanning;
using HomeServer.Metadata;
using HomeServer.Downloads;
using HomeServer.Sources;
using HomeServer.Playback;
using HomeServer.Identity;
using HomeServer.Operations;
using HomeServer.Music;
using HomeServer.Subtitles;
using HomeServer.Usenet;
using HomeServer.Notifications;
using HomeServer.Offline;
using HomeServer.Watching;
using HomeServer.Gaming;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using HomeServer.LiveTv;
using HomeServer.Shelves;
using EntityTagHeaderValue = Microsoft.Net.Http.Headers.EntityTagHeaderValue;

if (args is ["--verify-package", var directory, var trustPath])
{
    try { var package = await PackageTrust.VerifyDirectoryAsync(directory, File.ReadAllText(trustPath), CancellationToken.None); Console.WriteLine($"Home {package.Version} {package.Runtime}: imza ve dosyalar doğrulandı."); }
    catch (Exception ex) { Console.Error.WriteLine(ex is DomainException ? ex.Message : "Paket doğrulanamadı."); Environment.ExitCode = 1; }
    return;
}
if (args is ["--healthcheck"])
{
    try { using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) }; using var response = await client.GetAsync(Environment.GetEnvironmentVariable("HOME_HEALTH_URL") ?? "http://127.0.0.1:8080/api/health"); Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1; }
    catch (HttpRequestException) { Environment.ExitCode = 1; } catch (TaskCanceledException) { Environment.ExitCode = 1; }
    return;
}
if (args is ["--container-update-prepare"] or ["--container-update-complete"] or ["--container-update-cancel"])
{
    Environment.ExitCode = await ContainerUpdateControl.RunCommandAsync(args[0].Replace("--container-update-", "", StringComparison.Ordinal));
    return;
}
var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<HostOptions>(options=>options.ShutdownTimeout=TimeSpan.FromMinutes(3));
var settingsFile = Path.Combine(Path.GetFullPath(builder.Configuration["Home:DataPath"] ?? ".data"), "home-settings.json");
if (File.Exists(settingsFile))
{
    AppFiles.RejectLink(settingsFile);
    builder.Configuration.AddJsonFile(settingsFile, optional: false, reloadOnChange: false).AddEnvironmentVariables().AddCommandLine(args);
}
var networkPreferences = NetworkConfiguration.Validate(builder.Configuration.GetSection("Home:Network").Get<NetworkPreferences>() ?? new());
var operationJournal=new OperationJournal(Path.Combine(Path.GetFullPath(builder.Configuration["Home:DataPath"]??".data"),"logs","structured"));
builder.Logging.AddProvider(operationJournal);builder.Services.AddSingleton(operationJournal);
if (int.TryParse(builder.Configuration["Home:WaitForExitPid"], out var previousPid) && previousPid != Environment.ProcessId)
{
    try { using var previous = System.Diagnostics.Process.GetProcessById(previousPid); previous.WaitForExit(15000); }
    catch (ArgumentException) { }
}
if (await UpdateService.RedirectToActiveAsync(builder.Configuration)) return;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);
builder.Services.AddSingleton(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var data = Path.GetFullPath(configuration["Home:DataPath"] ?? ".data");
    _ = services.GetRequiredService<AppDataLease>();
    StateFolders.ActivatePending(data);
    var demo = configuration["Home:DemoPath"];
    var native=configuration["Home:Games:NativeRuntimePath"]??Path.Combine(AppContext.BaseDirectory,"NativeRuntime");
    return new HomePaths(data, Path.Combine(data, "thumbnails"), string.IsNullOrWhiteSpace(demo) ? null : Path.GetFullPath(demo), StateFolders.Current(data), Directory.Exists(native)?Path.GetFullPath(native):null);
});
builder.Services.AddSingleton<MediaStore>();
builder.Services.AddSingleton<GameStore>();
builder.Services.AddSingleton<GameCatalog>();
builder.Services.AddSingleton<NativeRomLibrary>();
builder.Services.AddSingleton<NativeGamingRuntime>();
builder.Services.AddSingleton<NativeSaveSnapshots>();
builder.Services.AddSingleton<NativeGameManager>();
builder.Services.AddSingleton<NativeStreamNetwork>();
builder.Services.AddSingleton<INativeRuntimeVerifier, NativeRuntimeVerifier>();
builder.Services.AddSingleton<NativeRuntimeSetup>();
builder.Services.AddSingleton<NativeEmulationSettings>();
builder.Services.AddSingleton<RetroArchCoreCatalog>();
builder.Services.AddSingleton<NativeFirmwareLibrary>();
builder.Services.AddSingleton<NativeCoreRegistry>();
builder.Services.AddSingleton<NativeGpuRuntime>();
builder.Services.AddSingleton<NativeCoreOptions>();
builder.Services.AddSingleton<NativeGameSettings>();
builder.Services.AddSingleton<NativeCoreInstaller>();
builder.Services.AddSingleton<IProductWorkHandler>(services=>services.GetRequiredService<NativeCoreInstaller>());
builder.Services.AddSingleton<RomArtwork>();
builder.Services.AddSingleton<HasheousMetadataClient>();
builder.Services.AddSingleton<RomMetadata>();
builder.Services.AddSingleton<GameLibraryPreferences>();
builder.Services.AddHostedService<RomMetadataAutomation>();
builder.Services.AddSingleton<IProductWorkHandler>(services=>services.GetRequiredService<RomMetadata>());
builder.Services.AddSingleton<IProductWorkHandler>(services=>services.GetRequiredService<RomArtwork>());
builder.Services.AddHostedService(services=>services.GetRequiredService<NativeGameManager>());
builder.Services.AddSingleton<IGamePackageSource, GamePackageSource>();
builder.Services.AddSingleton<GameInstaller>();
builder.Services.AddSingleton<IProductWorkHandler>(services => services.GetRequiredService<GameInstaller>());
builder.Services.AddSingleton(services => new AppDataLease(Path.GetFullPath(services.GetRequiredService<IConfiguration>()["Home:DataPath"] ?? ".data")));
builder.Services.AddSingleton<EventHub>();
builder.Services.AddMemoryCache(options => options.SizeLimit = 100);
builder.Services.AddSingleton<MetadataSettings>();
builder.Services.AddSingleton<MetadataProvider>();
builder.Services.AddSingleton<ArtworkCache>();
builder.Services.AddSingleton<IndexerService>();
builder.Services.AddSingleton<IReleaseAcquisition>(services => services.GetRequiredService<IndexerService>());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<NotificationSettings>();
builder.Services.AddSingleton<WebPushSettings>();
builder.Services.AddSingleton<WebPushTransport>();
builder.Services.AddSingleton<INotificationTransport, NotificationTransport>();
builder.Services.AddSingleton<NotificationDispatcher>();
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddSingleton<AutomationCoordinator>();
builder.Services.AddSingleton<MusicBrainzProvider>();
builder.Services.AddSingleton<MusicExperiencePreferences>();
builder.Services.AddSingleton<MusicOnlineMetadata>();
builder.Services.AddSingleton<MusicEnrichmentWorker>();
builder.Services.AddHostedService(services=>services.GetRequiredService<MusicEnrichmentWorker>());
builder.Services.AddSingleton<RadioDirectory>();
builder.Services.AddSingleton<IRadioStreamSource,RadioStreamSource>();
builder.Services.AddSingleton<RadioPlayback>();
builder.Services.AddHostedService(services=>services.GetRequiredService<RadioPlayback>());
builder.Services.AddSingleton<IMusicAcquisition>(services => services.GetRequiredService<IndexerService>());
builder.Services.AddSingleton<MusicAutomationCoordinator>();
builder.Services.AddHostedService<MusicAutomationWorker>();
builder.Services.AddSingleton<CoverArtCache>();
builder.Services.AddSingleton<SubtitleSettings>();
builder.Services.AddSingleton<OpenSubtitlesProvider>();
builder.Services.AddSingleton<SubtitleAcquisition>();
builder.Services.AddSingleton<SubtitleCoordinator>();
builder.Services.AddSingleton<SileroVad>();
builder.Services.AddSingleton<SubtitleSyncService>();
builder.Services.AddHostedService<SubtitleSyncWorker>();
builder.Services.AddHostedService<SubtitleWorker>();
builder.Services.AddSingleton<LyricsService>();
builder.Services.AddHostedService<ProductWorker>();
builder.Services.AddSingleton<CatalogFeedService>();
builder.Services.AddSingleton<SpeechGeneration>();
builder.Services.AddSingleton<SegmentAnalysis>();
builder.Services.AddSingleton<DlnaController>();
builder.Services.AddSingleton<ReceiverService>();
builder.Services.AddSingleton<AdvancedOperations>();
builder.Services.AddSingleton<SourceTransportService>();
builder.Services.AddSingleton<SourceCatalog>();
builder.Services.AddSingleton<SourceSetupService>();
builder.Services.AddHostedService(services=>services.GetRequiredService<SourceSetupService>());
builder.Services.AddSingleton<RemoteMediaHttp>();
builder.Services.AddSingleton<BookService>();
builder.Services.AddSingleton<PodcastService>();
builder.Services.AddSingleton<IProductWorkHandler>(services=>services.GetRequiredService<PodcastService>());
builder.Services.AddHostedService<PodcastScheduler>();
builder.Services.AddSingleton<ShelfThumbnails>();
builder.Services.AddSingleton<LiveInputRelay>();
builder.Services.AddSingleton<LiveSources>();
builder.Services.AddSingleton<ILiveHttp,LiveHttp>();
builder.Services.AddSingleton<LivePublicCatalog>();
builder.Services.AddSingleton<LiveArtwork>();
builder.Services.AddSingleton<IProductWorkHandler>(services=>services.GetRequiredService<LiveSources>());
builder.Services.AddSingleton<LivePlayback>();
builder.Services.AddSingleton<ILiveProducer,LiveProducer>();
builder.Services.AddHostedService(services=>services.GetRequiredService<LivePlayback>());
builder.Services.AddHostedService<LiveSourceScheduler>();
builder.Services.AddHostedService<DvrRecorder>();
builder.Services.AddSingleton<DvrPublisher>();
builder.Services.AddSingleton<IProductWorkHandler>(services=>services.GetRequiredService<AdvancedOperations>());
builder.Services.AddHostedService<OperationSampler>();
builder.Services.AddSingleton<IProductWorkHandler>(services=>services.GetRequiredService<ReceiverService>());
builder.Services.AddSingleton<IProductWorkHandler>(services=>services.GetRequiredService<SegmentAnalysis>());
builder.Services.AddSingleton<IProductWorkHandler>(services=>services.GetRequiredService<SpeechGeneration>());
builder.Services.AddSingleton<IProductWorkHandler>(services=>services.GetRequiredService<CatalogFeedService>());
builder.Services.AddHostedService<CatalogFeedScheduler>();
builder.Services.AddHostedService<MusicListWorker>();
builder.Services.AddSingleton<MusicPcmCache>();
builder.Services.AddSingleton<MusicSonicAnalyzer>();
builder.Services.AddSingleton<MusicTagService>();
builder.Services.AddSingleton<MusicTaskWorker>();
builder.Services.AddHostedService(services => services.GetRequiredService<MusicTaskWorker>());
builder.Services.AddHostedService<MusicIndexWorker>();
builder.Services.AddHostedService<AutomationWorker>();
builder.Services.AddHostedService<TrackingWorker>();
builder.Services.AddHostedService<AutoMatchWorker>();
builder.Services.AddHostedService<LibraryMonitor>();
builder.Services.AddHostedService<AcquisitionWorker>();
builder.Services.AddSingleton<DownloadFiles>();
builder.Services.AddSingleton<TorrentEngine>();
builder.Services.AddSingleton<TorrentMetadataResolver>();
builder.Services.AddSingleton<UsenetService>();
builder.Services.AddSingleton<Par2Tool>();
builder.Services.AddSingleton<PackageExtractor>();
builder.Services.AddSingleton<NzbEngine>();
builder.Services.AddSingleton<ITorrentSessions, TransferSessions>();
builder.Services.AddSingleton<DownloadImporter>();
builder.Services.AddSingleton<DownloadWorker>();
builder.Services.AddHostedService(services => services.GetRequiredService<DownloadWorker>());
foreach (var provider in new[] { "tvmaze", "tmdb", "artwork", "indexer", "musicbrainz", "cover-art", "subtitle-api", "subtitle-file" })
{
    builder.Services.AddHttpClient(provider, client =>
    {
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HomeMediaServer/" + BuildInfo.Version);
        if (provider == "tvmaze") client.BaseAddress = new Uri("https://api.tvmaze.com/");
        if (provider == "tmdb") client.BaseAddress = new Uri("https://api.themoviedb.org/3/");
        if (provider == "musicbrainz")
        {
            client.BaseAddress = new Uri("https://musicbrainz.org/ws/2/");
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("HomeMediaServer/" + BuildInfo.Version + " (personal music library)");
        }
    }).RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = provider != "indexer",
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
    });
}
builder.Services.AddSingleton<MediaTools>();
builder.Services.AddHttpClient("notifications", client => { client.Timeout = TimeSpan.FromSeconds(20); client.DefaultRequestHeaders.UserAgent.ParseAdd("HomeMediaServer/" + BuildInfo.Version); })
    .RemoveAllLoggers().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, MaxResponseHeadersLength = 16 });
builder.Services.AddTransient<PushResponseHeadersOnlyHandler>();
builder.Services.AddHttpClient("web-push", client => { client.Timeout = TimeSpan.FromSeconds(20); client.DefaultRequestHeaders.UserAgent.ParseAdd("HomeMediaServer/" + BuildInfo.Version); })
    .RemoveAllLoggers().AddHttpMessageHandler<PushResponseHeadersOnlyHandler>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, MaxResponseHeadersLength = 16 });
builder.Services.AddSingleton<IMediaProbe>(services => services.GetRequiredService<MediaTools>());
builder.Services.AddSingleton<MediaAnalysisService>();
builder.Services.AddSingleton<EpisodeBoundaryService>();
builder.Services.AddHostedService(services => services.GetRequiredService<EpisodeBoundaryService>());
builder.Services.AddSingleton<EncodingSupport>();
builder.Services.AddSingleton<SubtitleService>();
builder.Services.AddSingleton<PlaybackManager>();
builder.Services.AddHostedService(services => services.GetRequiredService<PlaybackManager>());
builder.Services.AddSingleton<PlaybackSessions>();
builder.Services.AddHostedService(services => services.GetRequiredService<PlaybackSessions>());
builder.Services.AddHostedService<ReceiverMonitor>();
builder.Services.AddSingleton<OfflineService>();
builder.Services.AddHostedService(services => services.GetRequiredService<OfflineService>());
builder.Services.AddSingleton<WatchRoomService>();
builder.Services.AddHostedService(services => services.GetRequiredService<WatchRoomService>());
builder.Services.AddSingleton<LibraryScanner>();
builder.Services.AddSingleton<ScanWorker>();
builder.Services.AddHostedService(services => services.GetRequiredService<ScanWorker>());
builder.Services.AddSingleton<IPasswordHasher<Owner>, PasswordHasher<Owner>>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentAccount>();
builder.Services.AddSingleton<IdentityService>();
builder.Services.AddSingleton<PlaybackDeviceIdentity>();
builder.Services.AddSingleton<HouseholdProfiles>();
builder.Services.AddHostedService<RequestWorker>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton<RestartCoordinator>();
builder.Services.AddSingleton<NetworkConfiguration>();
builder.Services.AddSingleton<UpdateService>();
builder.Services.AddSingleton<ContainerRequestGate>();
builder.Services.AddSingleton<ContainerUpdateCoordinator>();
builder.Services.AddHostedService<ContainerUpdateControl>();
builder.Services.AddOptions<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>().Configure<HomePaths, IDataProtectionProvider, IConfiguration>(NetworkConfiguration.ConfigureTls);
builder.Services.AddHostedService<SystemWorker>();
builder.Services.AddDataProtection().SetApplicationName("HomeServer");
builder.Services.AddOptions<KeyManagementOptions>().Configure<HomePaths, ILoggerFactory>((options, homePaths, loggers) =>
    options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(Path.Combine(homePaths.State, "keys")), loggers));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = NetworkConfiguration.CookieName("home.session", networkPreferences.PathBase);
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = networkPreferences.PathBase.Length == 0 ? "/" : networkPreferences.PathBase;
    options.Cookie.SecurePolicy = networkPreferences.RequireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    options.Events.OnValidatePrincipal = async context =>
    {
        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var session = context.Principal?.FindFirstValue("home.sid");
        var user = id is null || session is null ? null : context.HttpContext.RequestServices.GetRequiredService<MediaStore>().ValidateLoginSession(session, id);
        if (user is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
        else context.HttpContext.Items[typeof(Account)] = user;
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("admin", policy => policy.RequireRole("admin"));
    options.AddPolicy("acquire", policy => policy.RequireAssertion(context => context.User.IsInRole("admin") || context.User.HasClaim("home.acquire", "yes")));
    options.AddPolicy("discover", policy => policy.RequireAssertion(context => context.User.IsInRole("admin") || context.User.HasClaim("home.discover", "yes")));
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = NetworkConfiguration.CookieName("home.csrf", networkPreferences.PathBase);
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = networkPreferences.PathBase.Length == 0 ? "/" : networkPreferences.PathBase;
    options.Cookie.SecurePolicy = networkPreferences.RequireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("link-poll",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=60,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
    options.AddPolicy("watch-live", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue("home.sid") ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 1200, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("watch-command", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("native-input", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromSeconds(1), QueueLimit = 0 }));
    options.AddFixedWindowLimiter("credentials", limiter =>
    {
        limiter.PermitLimit = 12;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

var app = builder.Build();
networkPreferences = NetworkConfiguration.Validate(app.Configuration.GetSection("Home:Network").Get<NetworkPreferences>() ?? new());
var paths = app.Services.GetRequiredService<HomePaths>();
_ = app.Services.GetRequiredService<AppDataLease>();
app.Services.GetRequiredService<MediaStore>().Initialize();
app.Services.GetRequiredService<MediaStore>().ReconcileNetworkSecurity(networkPreferences);
var pidFile = Path.Combine(paths.Data, "home.pid");
if (File.Exists(pidFile)) AppFiles.RejectLink(pidFile);
File.WriteAllText(pidFile, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
app.Lifetime.ApplicationStopped.Register(() =>
{
    try { if (File.Exists(pidFile) && File.ReadAllText(pidFile) == Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)) File.Delete(pidFile); }
    catch (IOException) { }
});
NetworkConfiguration.UseNetwork(app, networkPreferences);

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    try { await next(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception ex) when (!context.Response.HasStarted)
    {
        var status = ex switch { DomainException domain => domain.StatusCode, BadHttpRequestException bad => bad.StatusCode, _ => 500 };
        var message = ex is DomainException ? ex.Message : status == 400 ? "İstek okunamadı." : "İşlem tamamlanamadı. Yeniden deneyebilirsin.";
        if (status == 500) app.Logger.LogError(ex, "İstek işlenemedi.");
        context.Response.StatusCode = status;
            var budget = ex as DownloadBudgetException;
            if (budget?.RetryAt is { } retry) context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling((retry - DateTimeOffset.UtcNow).TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await context.Response.WriteAsJsonAsync(new { message, code = budget?.Code, retryAt = budget?.RetryAt });
    }
});
var containerUpdates = app.Services.GetRequiredService<ContainerUpdateCoordinator>();
var containerRequests = app.Services.GetRequiredService<ContainerRequestGate>();
app.Use((context, next) => ContainerUpdateMiddleware.InvokeAsync(context, next, containerRequests, containerUpdates.Supported));
app.UseAuthentication();
app.UseWebSockets();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { message = "Oturum doğrulaması yenilenmeli. Sayfayı yenileyip tekrar dene." });
            return;
        }
    }
    await next(context);
});

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", version = BuildInfo.Version }));
app.MapGet("/api/session", (HttpContext context, MediaStore store, IAntiforgery antiforgery) =>
{
    context.Response.Headers.CacheControl = "no-cache, no-store";
    context.Response.Headers.Pragma = "no-cache";
    var owner = store.GetOwner();
    var authenticated = context.User.Identity?.IsAuthenticated == true;
    var user = context.Items[typeof(Account)] as Account;
    return Results.Ok(new
    {
        configured = owner is not null,
        authenticated,
        preview = app.Configuration.GetValue<bool>("Home:Preview"),
        displayName = user?.DisplayName, userId = user?.Id, role = user?.Role,
        canDiscover = user is not null && (user.IsAdmin || user.CanDiscover), canAcquire = user is not null && (user.IsAdmin || user.CanAcquire),
        canRequest = user?.CanRequest ?? false, sessionId = context.User.FindFirstValue("home.sid"),
        csrfToken = antiforgery.GetAndStoreTokens(context).RequestToken
    });
});
app.MapPost("/api/setup", async (Credentials input, HttpContext context, MediaStore store, IdentityService identity) =>
{
    ValidateCredentials(input);
    var owner = new Owner(input.DisplayName.Trim(), "");
    store.CreateOwner(owner with { PasswordHash = identity.Hash(owner.DisplayName, input.Password) });
    await identity.SignInAsync(context, store.GetAccountSecret("1")!);
    return Results.Ok(new { message = "Home hazır." });
}).RequireRateLimiting("credentials");
app.MapPost("/api/auth/login", async (Credentials input, HttpContext context, IdentityService identity) =>
{
    await identity.LoginAsync(context, input);
    return Results.Ok(new { message = "Tekrar hoş geldin." });
}).RequireRateLimiting("credentials");
app.MapPost("/api/invitations/accept", async (AcceptInvitation input, HttpContext context, MediaStore store, IdentityService identity) =>
{
    if (string.IsNullOrEmpty(input.Token)) throw new DomainException("Davet bağlantısı gerekli.");
    var user = store.AcceptInvitation(input.Token, input.DisplayName, identity.Hash(input.DisplayName, input.Password));
    await identity.SignInAsync(context, store.GetAccountSecret(user.Id)!);
    return Results.Ok(new { message = "Haneye katıldın." });
}).RequireRateLimiting("credentials");

var api = app.MapGroup("/api").RequireAuthorization();
app.MapGames(api);
app.MapNativeGames(api);
app.MapDeviceLinks();
app.MapReceivers(api);
app.MapLiveTv(api);
app.MapGet("/api/profiles", (HouseholdProfiles profiles, HttpContext context) => Results.Ok(new { enabled = profiles.Device(context) is not null, profiles = profiles.Profiles(context) }));
app.MapPost("/api/profiles/switch", async (ProfileSwitchInput input, HouseholdProfiles profiles, HttpContext context) => { await profiles.SwitchAsync(context, input); return Results.NoContent(); }).RequireRateLimiting("credentials");
app.MapDelete("/api/profiles/device", (HouseholdProfiles profiles, HttpContext context) => { profiles.Forget(context); return Results.NoContent(); });
api.MapMetadata();
api.MapGroup("").RequireAuthorization("acquire").MapDownloads();
api.MapCatalog();
api.MapCatalogIdentity();
api.MapEpisodeCatalog();
api.MapPersonalCatalog();
api.MapCatalogFeeds();
api.MapLibraryAdministration();
api.MapGroup("").RequireAuthorization("acquire").MapSources();
api.MapAdapters();
api.MapSourceCatalog();
api.MapPlayback();
api.MapSegments();
api.MapPlaybackQuotas();
api.MapIdentity();
api.MapNotificationDelivery();
api.MapWebPush();
api.MapOffline();
api.MapWatchRooms();
api.MapDownloadBudgets();
api.MapAutomation();
api.MapOperations();
api.MapAdvancedOperations();
api.MapMusic();
api.MapShelves();
api.MapPodcasts();
api.MapMusicCatalog();
api.MapMusicAutomation();
api.MapMusicAudio();
api.MapSubtitleProviders();
api.MapSpeech();
api.MapUsenet();
api.MapPost("/auth/logout", async (HttpContext context, CurrentAccount actor, MediaStore store, PlaybackSessions playback) =>
{
    if (actor.SessionId is { } session) store.RevokeLoginSession(session, actor.User.Id);
    await playback.ReconcileAsync();
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
});
api.MapGet("/system", (MediaTools tools, CurrentAccount actor) => Results.Ok(new
{
    version = BuildInfo.Version, framework = RuntimeInformation.FrameworkDescription,
    os = actor.User.IsAdmin ? RuntimeInformation.OSDescription : "", dataPath = actor.User.IsAdmin ? paths.Data : "",
    ffmpegAvailable = tools.FfmpegAvailable, ffprobeAvailable = tools.FfprobeAvailable,
    playbackMode = tools.FfmpegAvailable ? "direct+remux+software-hls" : "direct", metadataSource = "filename+tvmaze+tmdb",
    downloadEngine = "MonoTorrent 3.0.2",
    downloadNetwork = app.Configuration.GetValue<bool>("Home:Downloads:LoopbackOnly") ? "loopback" : "standard",
    demoPath = (string?)null
}));
api.MapGet("/stats", (MediaStore store, CurrentAccount actor) => Results.Ok(store.GetStats(actor.User)));
api.MapGet("/libraries", (MediaStore store, CurrentAccount actor) => Results.Ok(store.GetLibraries(viewer: actor.User).Select(l => actor.User.IsAdmin ? l : l with { Path = "" })));
api.MapPost("/libraries", (AddLibrary input, MediaStore store, EventHub events) =>
{
    var library = store.AddLibrary(input);
    var job = store.QueueScan(library.Id);
    events.Publish("libraries.changed");
    return Results.Created($"/api/libraries/{library.Id}", new { library, job });
}).RequireAuthorization("admin");
api.MapPost("/libraries/{id}/scan", (string id, MediaStore store, EventHub events) =>
{
    var job = store.QueueScan(id);
    events.Publish("jobs.changed");
    return Results.Accepted($"/api/jobs/{job.Id}", job);
}).RequireAuthorization("admin");
api.MapGet("/catalog", (string? q, string? kind, string? library, int? offset, int? limit, MediaStore store, CurrentAccount actor) =>
    Results.Ok(store.GetCatalog(q, kind, library, offset ?? 0, limit ?? 60, actor.User)));
api.MapGet("/continue", (MediaStore store, CurrentAccount actor) => Results.Ok(store.GetContinueWatching(actor.User)));
api.MapGet("/media/{id}", (string id, CurrentAccount actor) => Results.Ok(actor.RequireMedia(id)));
api.MapMethods("/media/{id}/stream", ["GET", "HEAD"], (string id, string? playbackId, MediaStore store, CurrentAccount actor, PlaybackSessions playback) =>
{
    actor.RequireMedia(id); return playback.Direct(id, playbackId, store.GetPlaybackActor(actor.User.Id, actor.SessionId));
});
api.MapGet("/media/{id}/thumbnail", (string id, HttpContext context, CurrentAccount actor, MediaStore store) =>
{
    var item = actor.RequireMedia(id);
    if (LibraryEndpoints.LocalArtwork(id, "poster", store) is { } local) return local;
    if (item is null || !item.HasThumbnail) return Results.NotFound();
    var path = Path.Combine(paths.Thumbnails, item.Id + ".jpg");
    if (!File.Exists(path)) return Results.NotFound();
    var info = new FileInfo(path);
    context.Response.Headers.CacheControl = "private, max-age=3600";
    return Results.File(path, "image/jpeg", lastModified: new DateTimeOffset(info.LastWriteTimeUtc),
        entityTag: new EntityTagHeaderValue($"\"{info.LastWriteTimeUtc.Ticks:x}\""));
});
api.MapPut("/media/{id}/progress", (string id, PlaybackProgress input, MediaStore store, CurrentAccount actor) =>
{
    store.SaveProgress(id, input, actor.User);
    return Results.NoContent();
});
api.MapGet("/jobs", (MediaStore store) => Results.Ok(store.GetJobs())).RequireAuthorization("admin");
api.MapPost("/jobs/{id}/cancel", (string id, MediaStore store, ScanWorker worker) =>
{
    if (store.GetJob(id) is null) throw new DomainException("İşlem bulunamadı.", 404);
    worker.Cancel(id);
    return Results.Accepted($"/api/jobs/{id}", store.GetJob(id));
}).RequireAuthorization("admin");
api.MapGet("/events", async (HttpContext context, EventHub events) =>
{
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers["X-Accel-Buffering"] = "no";
    var subscription = events.Subscribe();
    try
    {
        await context.Response.WriteAsync("event: ready\ndata: {}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        while (!context.RequestAborted.IsCancellationRequested)
        {
            using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var message = await subscription.Reader.ReadAsync(heartbeat.Token);
                await context.Response.WriteAsync($"event: change\ndata: {JsonSerializer.Serialize(message)}\n\n", context.RequestAborted);
            }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            { await context.Response.WriteAsync(": heartbeat\n\n", context.RequestAborted); }
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
    finally { events.Unsubscribe(subscription.Id); }
});

var webRoot = builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(webRoot);
app.Use(async (context, next) =>
{
    if (context.Request.Method is "GET" or "HEAD" && context.Request.Path is { } path && (path == "/" || path == "/index.html" || path == "/manifest.webmanifest"))
    {
        var file = Path.Combine(webRoot, path == "/manifest.webmanifest" ? "manifest.webmanifest" : "index.html");
        if (File.Exists(file))
        {
            var text = await File.ReadAllTextAsync(file, context.RequestAborted);
            var manifest = path == "/manifest.webmanifest";
            context.Response.Headers.CacheControl = "no-cache";
            await Results.Content(manifest ? text.Replace("\"/", "\"" + networkPreferences.PathBase + "/", StringComparison.Ordinal) : NetworkConfiguration.Html(text, networkPreferences.PathBase), manifest ? "application/manifest+json" : "text/html").ExecuteAsync(context);
            return;
        }
    }
    await next(context);
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(webRoot),
    OnPrepareResponse = context =>
    {
        if (context.File.Name == "sw.js") context.Context.Response.Headers.CacheControl = "no-cache";
        else if (context.Context.Request.Path.StartsWithSegments("/assets")) context.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    }
});
app.MapFallback("/api/{**path}", () => Results.NotFound(new { message = "API adresi bulunamadı." }));
app.MapFallback(() => File.Exists(Path.Combine(webRoot, "index.html"))
    ? Results.Content(NetworkConfiguration.Html(File.ReadAllText(Path.Combine(webRoot, "index.html")), networkPreferences.PathBase), "text/html")
    : Results.Content("Home API çalışıyor. Geliştirme arayüzü: http://localhost:5173", "text/plain"));
var restart = app.Services.GetRequiredService<RestartCoordinator>();
app.Run();
restart.LaunchReplacement();

static void ValidateCredentials(Credentials input)
{
    if (string.IsNullOrWhiteSpace(input.DisplayName) || input.DisplayName.Trim().Length > 60)
        throw new DomainException("Adın 1–60 karakter olmalı.");
    if (string.IsNullOrEmpty(input.Password) || input.Password.Length < 10 || input.Password.Length > 256)
        throw new DomainException("En az 10 karakterli bir parola seç.");
}

public partial class Program;

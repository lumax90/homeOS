using System.Security.Cryptography;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HomeServer.Tests;

public sealed class MediaStillsTests
{
    private static MediaStreamInfo Video(bool hdr=false,bool dv=false,int profile=8) => new(0,"video","hevc","und","",true,false,false,128,72,null,null,null,null,
        "Main 10","yuv420p10le",hdr?"smpte2084":"bt709",hdr?"bt2020":"bt709",dv,
        DolbyVisionConfig:dv?new(profile,6,profile==8?1:0,true,profile==7,true):null);
    [Fact]
    public void StillsHaveSixBoundedRealSourcePositionsAndDoNotUseEndCredits()
    {
        var positions=MediaStills.Positions(7200);Assert.Equal(6,positions.Length);Assert.All(positions,p=>Assert.InRange(p,0,5040));
        Assert.Empty(MediaStills.Positions(double.NaN));Assert.Empty(MediaStills.Positions(double.PositiveInfinity));
        Assert.Empty(MediaStills.Positions(0));Assert.Empty(MediaStills.Positions(90000));
    }
    [Fact]
    public void UnverifiedDolbyVisionCannotBeMistakenForOrdinaryHdrStills()
    {
        Assert.False(MediaStills.SupportedVideo(Video(true,true,5)));Assert.False(MediaStills.SupportedVideo(Video(true,true,7)));
        Assert.False(MediaStills.SupportedVideo(Video(true,true) with{DolbyVisionConfig=null}));
        Assert.True(MediaStills.SupportedVideo(Video(true,true)));Assert.True(MediaStills.SupportedVideo(Video()));
    }
    [Fact]
    public void SingleFrameArgumentsRestrictProtocolsAndReuseVerifiedHdrTonemapping()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Isolated scene owner","preserved"));
        var item=new MediaItem("", "", "", "", "video", "", "", 0,0,null,null,null,null,false,"","",false,0,false,false,"",null);
        var media=new AnalyzedMedia(item,Path.Combine(home.Source,"ORIGINAL.mkv"),"fixture",new(6,128,72,"hevc","",Streams:[Video(true)]));
        var args=MediaStills.Arguments(media,0,Path.Combine(home.Root,"only-cache.tmp.jpg"));
        Assert.Contains("file,pipe",args);Assert.Contains("1",args);Assert.DoesNotContain("-y",args);
        Assert.Contains(EncodingSupport.ToneMapFilter+",scale=960:-2",args);
        Assert.Throws<DomainException>(()=>MediaStills.Arguments(media,6,"not-a-target"));
    }
    private static (MediaTools Tools,MediaStills Stills) Services(TestWorkspace home)
    {
        var ffmpeg=Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG");var ffprobe=Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE");
        Assert.True(File.Exists(ffmpeg)&&File.Exists(ffprobe),"Configure isolated FFmpeg/ffprobe for native still-photo verification.");
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Home:FfmpegPath"]=ffmpeg,["Home:FfprobePath"]=ffprobe}).Build();
        var tools=new MediaTools(config,home.Paths,NullLogger<MediaTools>.Instance);
        return(tools,new(home.Paths,home.Store,new(home.Store,tools),tools,new(tools,home.Store)));
    }
    private static async Task<(string Id,string Source)> Fixture(TestWorkspace home,MediaTools tools,bool hdr,string transfer="smpte2084")
    {
        var path=Path.Combine(home.Source,"ORIGINAL-ISOLATED-scene.mkv");
        var args=new List<string>{"-hide_banner","-loglevel","error","-nostdin","-f","lavfi","-i",hdr?"color=size=128x72:rate=2":"testsrc2=size=128x72:rate=12","-t","6","-threads","1",
            "-c:v",hdr?"libx265":"libx264","-preset","ultrafast","-pix_fmt",hdr?"yuv420p10le":"yuv420p"};
        if(hdr)args.AddRange(["-x265-params","pools=1:frame-threads=1:log-level=error:colorprim=bt2020:transfer="+transfer+":colormatrix=bt2020nc"]);
        args.Add(path);await MediaProcess.RunAsync(tools.FfmpegExecutable,args,TimeSpan.FromSeconds(30),CancellationToken.None);
        var library=home.AddLibrary();await home.Scan(library.Id);
        return(Assert.Single(home.Store.GetCatalog(null,null,null,0,10).Items).Id,path);
    }
    [MediaFact]
    public async Task ActualLocalFramesAreCachedWithoutChangingMediaPostersOrPermissionBoundaries()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Isolated scene owner","preserved"));var viewer=home.Store.GetAccount("1")!;
        var (tools,stills)=Services(home);var(id,source)=await Fixture(home,tools,false);var bytes=SHA256.HashData(await File.ReadAllBytesAsync(source));
        var poster=Path.Combine(home.Paths.Thumbnails,id+".jpg");await File.WriteAllTextAsync(poster,"ORIGINAL manual poster");
        var gallery=await stills.DescribeAsync(id,viewer,CancellationToken.None);Assert.Equal(6,gallery.Images.Count);
        var frame=await stills.GetAsync(id,0,gallery.Fingerprint,viewer,CancellationToken.None);
        var image=await File.ReadAllBytesAsync(frame);Assert.Equal(0xff,image[0]);Assert.Equal(0xd8,image[1]);Assert.DoesNotContain(home.Source,frame);
        Assert.Equal(frame,await stills.GetAsync(id,0,gallery.Fingerprint,viewer,CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(()=>stills.GetAsync(id,0,gallery.Fingerprint,viewer with{Id="999"},CancellationToken.None));
        await Assert.ThrowsAsync<DomainException>(()=>stills.GetAsync(id,99,gallery.Fingerprint,viewer,CancellationToken.None));
        File.SetLastWriteTimeUtc(source,DateTime.UtcNow.AddSeconds(2));
        await Assert.ThrowsAsync<DomainException>(()=>stills.GetAsync(id,0,gallery.Fingerprint,viewer,CancellationToken.None));
        Assert.Equal(bytes,SHA256.HashData(await File.ReadAllBytesAsync(source)));Assert.Equal("ORIGINAL manual poster",await File.ReadAllTextAsync(poster));
    }
    [MediaFact]
    public async Task HdrSourceCreatesAnSdrJpegAndAnUnclaimedCacheIsNeverOverwritten()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Isolated HDR owner","preserved"));var viewer=home.Store.GetAccount("1")!;
        var (tools,stills)=Services(home);var(id,source)=await Fixture(home,tools,true);
        var gallery=await stills.DescribeAsync(id,viewer,CancellationToken.None);Assert.Null(gallery.Reason);Assert.Equal(6,gallery.Images.Count);
        var root=Path.Combine(home.Paths.Thumbnails,"scene-stills-v1");Directory.CreateDirectory(root);var note=Path.Combine(root,"ORIGINAL-NOTES.txt");await File.WriteAllTextAsync(note,"preserved");
        await Assert.ThrowsAsync<DomainException>(()=>stills.GetAsync(id,0,gallery.Fingerprint,viewer,CancellationToken.None));
        Assert.Equal("preserved",await File.ReadAllTextAsync(note));
        await File.WriteAllTextAsync(Path.Combine(root,".home-scene-stills"),"Home.SceneStills.v1");
        var frame=await stills.GetAsync(id,0,gallery.Fingerprint,viewer,CancellationToken.None);
        var text=await MediaProcess.RunAsync(Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE")!,["-v","error","-show_streams","-of","json",frame],TimeSpan.FromSeconds(15),CancellationToken.None);
        using var probe=JsonDocument.Parse(text);var stream=probe.RootElement.GetProperty("streams")[0];Assert.Equal(960,stream.GetProperty("width").GetInt32());
        Assert.Equal("mjpeg",stream.GetProperty("codec_name").GetString());
        Assert.False(stream.TryGetProperty("color_transfer",out var transfer)&&transfer.GetString()=="smpte2084");
        Assert.True(File.Exists(source));Assert.Equal("preserved",await File.ReadAllTextAsync(note));
    }
    [MediaFact]
    public async Task HlgSourceProducesActualSdrJpegWithoutChangingTheSource()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Isolated HLG owner","preserved"));var actor=home.Store.GetAccount("1")!;
        var(tools,stills)=Services(home);var(id,path)=await Fixture(home,tools,true,"arib-std-b67");var original=SHA256.HashData(await File.ReadAllBytesAsync(path));
        var source=await tools.ProbeAsync(path,CancellationToken.None);Assert.Equal("arib-std-b67",source!.Streams!.Single(stream=>stream.Kind=="video").ColorTransfer);
        var gallery=await stills.DescribeAsync(id,actor,CancellationToken.None);var image=await stills.GetAsync(id,0,gallery.Fingerprint,actor,CancellationToken.None);
        var text=await MediaProcess.RunAsync(Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE")!,["-v","error","-show_streams","-of","json",image],TimeSpan.FromSeconds(15),CancellationToken.None);
        using var probe=JsonDocument.Parse(text);var output=probe.RootElement.GetProperty("streams")[0];Assert.Equal("mjpeg",output.GetProperty("codec_name").GetString());Assert.False(output.TryGetProperty("color_transfer",out var transfer)&&transfer.GetString()=="arib-std-b67");
        Assert.Equal(original,SHA256.HashData(await File.ReadAllBytesAsync(path)));
    }
    [MediaFact]
    public async Task VerifiedOriginalP81FixtureProducesActualSdrJpegAndPreservesDvSource()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Isolated P81 owner","preserved"));var actor=home.Store.GetAccount("1")!;var(tools,stills)=Services(home);
        var path=await DolbyVisionFixture.GenerateAsync(home,tools.FfmpegExecutable,"aac");var original=SHA256.HashData(await File.ReadAllBytesAsync(path));var library=home.AddLibrary();await home.Scan(library.Id);
        var item=Assert.Single(home.Store.GetCatalog(null,null,null,0,60).Items);var analysis=new MediaAnalysisService(home.Store,tools);var source=await analysis.InspectAsync(item.Id,CancellationToken.None);
        Assert.Equal(8,source.Probe.Streams!.Single(stream=>stream.Kind=="video").DolbyVisionConfig!.Profile);
        var gallery=await stills.DescribeAsync(item.Id,actor,CancellationToken.None);Assert.Null(gallery.Reason);var image=await stills.GetAsync(item.Id,0,gallery.Fingerprint,actor,CancellationToken.None);
        var text=await MediaProcess.RunAsync(Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE")!,["-v","error","-show_streams","-of","json",image],TimeSpan.FromSeconds(15),CancellationToken.None);
        using var probe=JsonDocument.Parse(text);var output=probe.RootElement.GetProperty("streams")[0];Assert.Equal("mjpeg",output.GetProperty("codec_name").GetString());Assert.DoesNotContain("Dolby Vision",text);Assert.False(output.TryGetProperty("color_transfer",out var transfer)&&transfer.GetString()=="smpte2084");
        Assert.Equal(original,SHA256.HashData(await File.ReadAllBytesAsync(path)));
    }
    private static SemaphoreSlim Writer(MediaStills stills)=>(SemaphoreSlim)typeof(MediaStills).GetField("_work",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(stills)!;
    [MediaFact]
    public async Task CancelledOrPermissionRevokedWaitersCannotPublishOrLeakTheWriter()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Isolated owner","preserved"));var owner=home.Store.GetAccount("1")!;
        var(tools,stills)=Services(home);var(id,_)=await Fixture(home,tools,false);var viewer=home.Store.SaveAccount(null,new("Isolated viewer"),"isolated");var gallery=await stills.DescribeAsync(id,viewer,CancellationToken.None);
        var writer=Writer(stills);await writer.WaitAsync();
        try
        {
            using var cancelled=new CancellationTokenSource();var waiting=stills.GetAsync(id,0,gallery.Fingerprint,viewer,cancelled.Token);cancelled.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>waiting);
            var denied=stills.GetAsync(id,0,gallery.Fingerprint,viewer,CancellationToken.None);home.Store.SaveAccount(viewer.Id,new(viewer.DisplayName,Enabled:false));writer.Release();
            Assert.Equal(404,(await Assert.ThrowsAsync<DomainException>(()=>denied)).StatusCode);
        }
        finally{if(writer.CurrentCount==0)writer.Release();}
        Assert.False(Directory.Exists(Path.Combine(home.Paths.Thumbnails,"scene-stills-v1")));
        var image=await stills.GetAsync(id,0,gallery.Fingerprint,owner,CancellationToken.None);Assert.True(File.Exists(image));Assert.Equal(1,writer.CurrentCount);
    }
    [MediaFact]
    public async Task ConcurrentRequestsShareOneJpegAndPruningKeepsUnknownUserFiles()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Isolated owner","preserved"));var actor=home.Store.GetAccount("1")!;var(tools,stills)=Services(home);var(id,_)=await Fixture(home,tools,false);var gallery=await stills.DescribeAsync(id,actor,CancellationToken.None);
        var root=Path.Combine(home.Paths.Thumbnails,"scene-stills-v1");Directory.CreateDirectory(root);await File.WriteAllTextAsync(Path.Combine(root,".home-scene-stills"),"Home.SceneStills.v1");
        var unknown=Path.Combine(root,"user-notes.jpg");await File.WriteAllTextAsync(unknown,"ORIGINAL USER CONTENT");
        var old=Path.Combine(root,new string('a',64)+"-5.jpg");using(var file=File.Create(old))file.SetLength(256L*1024*1024);File.SetLastWriteTimeUtc(old,DateTime.UtcNow.AddDays(-1));
        var abandoned=Path.Combine(root,Guid.NewGuid().ToString("N")+".tmp.jpg");await File.WriteAllTextAsync(abandoned,"abandoned isolated writer");
        var files=await Task.WhenAll(Enumerable.Range(0,6).Select(_=>stills.GetAsync(id,0,gallery.Fingerprint,actor,CancellationToken.None)));
        Assert.Single(files.Distinct());Assert.False(File.Exists(old));Assert.False(File.Exists(abandoned));Assert.Equal("ORIGINAL USER CONTENT",await File.ReadAllTextAsync(unknown));Assert.Empty(Directory.GetFiles(root,"*.tmp.jpg"));
    }
    [MediaFact]
    public async Task AuthenticatedStillRoutesRefuseARevokedLoginEvenWithAnExistingServerCache()
    {
        using var home=new TestWorkspace();await using var app=new StillFactory(home);using var client=app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/media/unknown/stills")).StatusCode);
        var session=await client.GetFromJsonAsync<JsonElement>("/api/session");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",session.GetProperty("csrfToken").GetString());
        (await client.PostAsJsonAsync("/api/setup",new Credentials("Isolated owner","isolated-password-2026"))).EnsureSuccessStatusCode();
        session=await client.GetFromJsonAsync<JsonElement>("/api/session");var actor=home.Store.GetAccount(session.GetProperty("userId").GetString()!)!;
        var tools=app.Services.GetRequiredService<MediaTools>();var(id,_)=await Fixture(home,tools,false);
        var gallery=await client.GetFromJsonAsync<MediaStillGallery>($"/api/media/{id}/stills");Assert.NotNull(gallery);
        var url=gallery.Images[0].Url;var photo=await client.GetAsync(url);photo.EnsureSuccessStatusCode();Assert.Contains("no-store",photo.Headers.CacheControl!.ToString());Assert.Equal("image/jpeg",photo.Content.Headers.ContentType!.MediaType);
        home.Store.RevokeLoginSession(session.GetProperty("sessionId").GetString()!,actor.Id);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(url)).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync($"/api/media/{id}/stills")).StatusCode);
    }
    private sealed class StillFactory(TestWorkspace home):WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)=>builder.UseEnvironment("Testing").ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>
        { ["Home:DataPath"]=home.Paths.Data,["Home:FfmpegPath"]=Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG"),["Home:FfprobePath"]=Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE"),["Home:DemoPath"]=""})).ConfigureServices(services=>
        {foreach(var descriptor in services.Where(service=>service.ImplementationType==typeof(ProductWorker)).ToArray())services.Remove(descriptor);});
    }
}

using System.Security.Cryptography;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class MediaRuntimeCompatibilityTests
{
    [Theory]
    [InlineData("mpeg","video","video/mpeg")]
    [InlineData("mpg","video","video/mpeg")]
    [InlineData("vob","video","video/mpeg")]
    [InlineData("m2ts","video","video/mp2t")]
    [InlineData("mts","video","video/mp2t")]
    [InlineData("wmv","video","video/x-ms-wmv")]
    [InlineData("asf","video","video/x-ms-asf")]
    [InlineData("3gp","video","video/3gpp")]
    [InlineData("3g2","video","video/3gpp2")]
    [InlineData("aac","audio","audio/aac")]
    [InlineData("wma","audio","audio/x-ms-wma")]
    public async Task SourceScanningAndReadOnlyRepeatableCatalogCoverTheNewContainers(string extension,string kind,string mime)
    {
        using var home=new TestWorkspace();var path=home.Write("ORIGINAL isolated format contract."+extension);var before=StateFolders.Hash(path);
        var library=home.AddLibrary();await home.Scan(library.Id);var first=Assert.Single(home.Store.GetCatalog(null,null,null,0,60).Items);
        Assert.Equal(kind,first.Kind);Assert.Equal(mime,first.MimeType);await home.Scan(library.Id);
        Assert.Equal(first.Id,Assert.Single(home.Store.GetCatalog(null,null,null,0,60).Items).Id);Assert.Equal(before,StateFolders.Hash(path));
    }

    [MediaTheory]
    [InlineData("mpeg","mpeg2video","mp2")]
    [InlineData("wmv","wmv2","wmav2")]
    [InlineData("m2ts","mpeg2video","mp2")]
    [InlineData("3gp","libx264","aac")]
    [InlineData("aac",null,"aac")]
    [InlineData("wma",null,"wmav2")]
    public async Task GeneratedLegacyAndAudioSourcesProbeAndProduceARealCompatibleStream(string extension,string? videoEncoder,string audioEncoder)
    {
        using var home=new TestWorkspace();var path=Path.Combine(home.Source,"ORIGINAL synthetic runtime fixture."+extension);
        var ff=Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG")!;
        var arguments=new List<string>{"-hide_banner","-loglevel","error","-nostdin"};
        if(videoEncoder is not null)arguments.AddRange(["-f","lavfi","-i","testsrc2=size=176x144:rate=24"]);
        arguments.AddRange(["-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-t","8","-threads","1"]);
        if(videoEncoder is not null)arguments.AddRange(["-c:v",videoEncoder,"-pix_fmt","yuv420p","-g","24"]);
        arguments.AddRange(["-c:a",audioEncoder,path]);
        await MediaProcess.RunAsync(ff,arguments,TimeSpan.FromSeconds(30),CancellationToken.None);var before=SHA256.HashData(await File.ReadAllBytesAsync(path));
        var library=home.AddLibrary();await home.Scan(library.Id);var item=Assert.Single(home.Store.GetCatalog(null,null,null,0,60).Items);
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Home:FfmpegPath"]=ff,["Home:FfprobePath"]=Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE")}).Build();
        var tools=new MediaTools(configuration,home.Paths,NullLogger<MediaTools>.Instance);var analysis=new MediaAnalysisService(home.Store,tools);var source=await analysis.InspectAsync(item.Id,CancellationToken.None);
        Assert.NotEmpty(source.Probe.Streams!);
        var streams=source.Probe.Streams!;
        var client=new PlaybackClient([], ["h264"],["aac"],true,2,new(source.Fingerprint,streams.FirstOrDefault(s=>s.Kind=="video")?.Index,streams.First(s=>s.Kind=="audio").Index,[new("mp4","hls.js",false,false,false,true)]));
        using var manager=new PlaybackManager(home.Paths,home.Store,analysis,tools,configuration,NullLogger<PlaybackManager>.Instance);
        await manager.StartAsync(CancellationToken.None);
        try{
            var session=await manager.CreateAsync("isolated-runtime-owner",new(item.Id,client),CancellationToken.None);
            var sample=Path.Combine(home.Root,"output.mp4");
            await using(var output=File.Create(sample))foreach(var part in new[]{"init.mp4","segment000000.m4s"}){
                var artifact=manager.OpenArtifact(session.Id!,part,"isolated-runtime-owner");await using(artifact.Stream)await artifact.Stream.CopyToAsync(output);
            }
            var probe=(await tools.ProbeAsync(sample,CancellationToken.None))!;Assert.Equal("aac",probe.AudioCodec);
            if(videoEncoder is not null)Assert.Equal("h264",probe.VideoCodec);
            await MediaProcess.RunAsync(ff,["-v","error","-nostdin","-i",sample,"-f","null","-"],TimeSpan.FromSeconds(15),CancellationToken.None);
            await manager.CloseAsync(session.Id!,"isolated-runtime-owner");Assert.Equal(before,SHA256.HashData(await File.ReadAllBytesAsync(path)));
        }finally{await manager.StopAsync(CancellationToken.None);}
    }
}

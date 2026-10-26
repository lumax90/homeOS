using System.Security.Cryptography;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class DolbyVisionBaseLayerTests
{
    private static AnalyzedMedia Source(DolbyVisionInfo? info=null)
    {
        var video=new MediaStreamInfo(0,"video","hevc","und","",true,false,false,3840,2160,null,null,null,40000000,"Main 10","yuv420p10le","smpte2084","bt2020",true,
            CodecString:"dvh1.08.06",DolbyVisionConfig:info??new(8,6,1,true,false,true),BaseCodecString:"hvc1.2.4.L153.B0");
        var audio=new MediaStreamInfo(1,"audio","truehd","en","",true,false,false,null,null,8,"7.1",48000,4000000,null,null,null,null);
        var media=new MediaItem("fixture","library","Isolated fixture","Original fixture","video","fixture.mp4","video/mp4",36000000000,7200,3840,2160,"hevc","truehd",true,"","",false,27,false,false);
        return new(media,media.RelativePath,"isolated-fingerprint",new(7200,3840,2160,"hevc","truehd",Streams:[video,audio]));
    }
    private static PlaybackClient Client(AnalyzedMedia source,bool original=false)=>new(["mp4"],["hevc","h264"],["aac"],true,2,new(source.Fingerprint,0,1,
        [new("mp4","direct",original,false,false,false),new("mp4","hls.js",false,false,false,true,BaseVideoCopy:true)]));
    [Fact]
    public void VerifiedP81BasePlusUnsupportedTrueHdKeeps4kHdrVideoAndOnlyConvertsAudio()
    {
        var source=Source();var plan=PlaybackPlanner.Decide(source,new(source.Media.Id,Client(source)),true,100000,dolbyVisionStripAvailable:true);
        Assert.Equal("audio-transcode",plan.Mode);Assert.True(plan.CopyVideo);Assert.False(plan.CopyAudio);Assert.True(plan.StripDolbyVision);Assert.False(plan.ToneMap);
        Assert.Equal(2160,plan.Height);Assert.Equal("hevc",plan.VideoCodec);Assert.Equal("aac",plan.AudioCodec);Assert.Contains(plan.Reasons,r=>r.Contains("HDR10"));
        Assert.Contains("hevc_metadata=remove_dovi=1",PlaybackManager.EncodingArguments("input",0,plan));
        Assert.Equal("direct",PlaybackPlanner.Decide(source,new(source.Media.Id,Client(source,true)),true,100000,dolbyVisionStripAvailable:true).Mode);
        Assert.Throws<DomainException>(()=>PlaybackPlanner.Decide(source,new(source.Media.Id,Client(source)),true,100000));
    }
    [Theory]
    [InlineData(5,0,true,false,true)]
    [InlineData(7,1,true,true,true)]
    [InlineData(8,4,true,false,true)]
    [InlineData(8,1,null,false,true)]
    [InlineData(8,1,true,null,true)]
    [InlineData(8,1,true,false,null)]
    public void UnverifiedOrUnknownLayersCannotUseAnOrdinaryHdr10BaseClaim(int profile,int compatibility,bool? bl,bool? el,bool? rpu)
    {
        var source=Source(new(profile,6,compatibility,bl,el,rpu));Assert.False(DolbyVisionBaseLayer.CanUseHdr10(source.Probe.Streams![0]));
        Assert.Throws<DomainException>(()=>PlaybackPlanner.Decide(source,new(source.Media.Id,Client(source)),true,100000,toneMappingAvailable:true,dolbyVisionStripAvailable:true));
    }
    [Fact]
    public void BaseCopyDoesNotBypassQualityLimitsSubtitleBurnOrChangedTrackBinding()
    {
        var source=Source();var client=Client(source);
        Assert.Throws<DomainException>(()=>PlaybackPlanner.Decide(source,new(source.Media.Id,client,MaxHeight:1080),true,100000,dolbyVisionStripAvailable:true));
        var sub=new SubtitleTrack("fixture-sub","en","Isolated subtitle","pgs","embedded",false,false,false,Burnable:true,Image:true);
        Assert.Throws<DomainException>(()=>PlaybackPlanner.Decide(source,new(source.Media.Id,client,SubtitleId:sub.Id),true,100000,selectedSubtitle:sub,dolbyVisionStripAvailable:true));
        Assert.Equal(409,Assert.Throws<DomainException>(()=>PlaybackPlanner.Decide(source with{Fingerprint="changed"},new(source.Media.Id,client),true,100000,dolbyVisionStripAvailable:true)).StatusCode);
    }
    [MediaTheory]
    [InlineData("truehd",false)]
    [InlineData("aac",true)]
    public async Task RealGeneratedP81WithRpuKeepsIdenticalHdrBasePixelsAndRemovesDvMetadataFromTheOutput(string audioCodec,bool preserveAudio)
    {
        using var home=new TestWorkspace();var ff=Environment.GetEnvironmentVariable("HOME_TEST_FFMPEG")!;
        var file=await DolbyVisionFixture.GenerateAsync(home,ff,audioCodec);var bytes=SHA256.HashData(await File.ReadAllBytesAsync(file));
        var library=home.AddLibrary();await home.Scan(library.Id);var media=Assert.Single(home.Store.GetCatalog(null,null,null,0,60).Items);
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Home:FfmpegPath"]=ff,["Home:FfprobePath"]=Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE")}).Build();
        var tools=new MediaTools(config,home.Paths,NullLogger<MediaTools>.Instance);var analysis=new MediaAnalysisService(home.Store,tools);var source=await analysis.InspectAsync(media.Id,CancellationToken.None);
        var video=source.Probe.Streams!.Single(s=>s.Kind=="video");Assert.Equal(new DolbyVisionInfo(8,1,1,true,false,true),video.DolbyVisionConfig);
        var encoding=new EncodingSupport(tools,home.Store);Assert.True(await encoding.DolbyVisionStripAsync(CancellationToken.None));
        var client=new PlaybackClient([], ["hevc","h264"],["aac"],true,2,new(source.Fingerprint,video.Index,source.Probe.Streams!.Single(s=>s.Kind=="audio").Index,
            [new("mp4","hls.js",false,false,false,true,BaseOriginal:preserveAudio,BaseVideoCopy:true)]));
        using var manager=new PlaybackManager(home.Paths,home.Store,analysis,tools,config,NullLogger<PlaybackManager>.Instance,encoding);
        await manager.StartAsync(CancellationToken.None);
        try
        {
            var session=await manager.CreateAsync("isolated-dv-owner",new(media.Id,client),CancellationToken.None);
            Assert.Equal(preserveAudio?"remux":"audio-transcode",session.Mode);Assert.Equal("copy",session.Encoder);Assert.Equal("hevc",session.VideoCodec);
            var sample=Path.Combine(home.Root,"base-output.mp4");
            await using(var output=File.Create(sample))foreach(var part in new[]{"init.mp4","segment000000.m4s"}){var artifact=manager.OpenArtifact(session.Id!,part,"isolated-dv-owner");await using(artifact.Stream)await artifact.Stream.CopyToAsync(output);}
            var result=(await tools.ProbeAsync(sample,CancellationToken.None))!;var outputVideo=result.Streams!.Single(s=>s.Kind=="video");
            Assert.False(outputVideo.DolbyVision);Assert.Null(outputVideo.DolbyVisionConfig);Assert.Equal("smpte2084",outputVideo.ColorTransfer);Assert.Equal("bt2020",outputVideo.ColorPrimaries);
            Assert.Equal(video.PixelFormat,outputVideo.PixelFormat);Assert.Equal(video.Width,outputVideo.Width);Assert.Equal(video.Height,outputVideo.Height);Assert.Equal("aac",result.AudioCodec);
            Assert.Equal(preserveAudio?2:6,source.Probe.Streams!.Single(s=>s.Kind=="audio").Channels);Assert.Equal(2,result.Streams!.Single(s=>s.Kind=="audio").Channels);
            async Task<string> Frames(string input)=>await MediaProcess.RunAsync(Environment.GetEnvironmentVariable("HOME_TEST_FFPROBE")!,
                ["-v","error","-select_streams","v:0","-read_intervals","0%+0.1","-show_entries","frame=side_data_list:frame_side_data=side_data_type","-of","json",input],TimeSpan.FromSeconds(15),CancellationToken.None);
            Assert.Contains("Dolby Vision RPU Data",await Frames(file));Assert.DoesNotContain("Dolby Vision",await Frames(sample));
            async Task<string> Pixels(string input)=>await MediaProcess.RunAsync(ff,["-v","error","-nostdin","-i",input,"-map","0:v:0","-an","-f","hash","-hash","sha256","-"],TimeSpan.FromSeconds(20),CancellationToken.None);
            Assert.Equal(await Pixels(file),await Pixels(sample));
            Assert.Throws<DomainException>(()=>manager.OpenArtifact(session.Id!,"init.mp4","other-owner"));
            await manager.CloseAsync(session.Id!,"isolated-dv-owner");Assert.Equal(bytes,SHA256.HashData(await File.ReadAllBytesAsync(file)));Assert.Empty(Directory.EnumerateDirectories(Path.Combine(home.Paths.Data,"playback")));
        }
        finally{await manager.StopAsync(CancellationToken.None);}
    }
}

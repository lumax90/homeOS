using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Tests;

public sealed class DolbyVisionAnalysisTests
{
    [Fact]
    public void ProbeRetainsTheActualDvProfileAndBaseLayerFlagsWithoutPromotingPlaybackSupport()
    {
        // Isolated ffprobe contract data, not an acquired Dolby Vision media sample.
        var result=MediaTools.ParseProbe("""
            {"format":{"duration":"4"},"streams":[{"index":0,"codec_type":"video","codec_name":"hevc","profile":"Main 10","level":153,"pix_fmt":"yuv420p10le","color_transfer":"smpte2084","color_primaries":"bt2020","side_data_list":[{"side_data_type":"DOVI configuration record","dv_profile":8,"dv_level":6,"bl_present_flag":1,"el_present_flag":0,"rpu_present_flag":1,"dv_bl_signal_compatibility_id":1}]}]}
            """);
        var video=Assert.Single(result.Streams!);Assert.True(video.DolbyVision);Assert.Equal("dvh1.08.06",video.CodecString);Assert.Equal("hvc1.2.4.L153.B0",video.BaseCodecString);
        Assert.Equal(new DolbyVisionInfo(8,6,1,true,false,true),video.DolbyVisionConfig);Assert.Equal(MediaTools.CurrentAnalysisVersion,result.AnalysisVersion);
    }
    [Fact]
    public void UnknownDvLayerFieldsStayUnknownInsteadOfInventingAnHdr10Fallback()
    {
        var result=MediaTools.ParseProbe("""
            {"format":{"duration":"4"},"streams":[{"index":0,"codec_type":"video","codec_name":"hevc","side_data_list":[{"side_data_type":"DOVI configuration record","dv_profile":5,"dv_level":6,"dv_bl_signal_compatibility_id":200,"el_present_flag":2}]}]}
            """);
        var info=Assert.Single(result.Streams!).DolbyVisionConfig!;Assert.Equal(5,info.Profile);Assert.Null(info.BaseLayerCompatibilityId);Assert.Null(info.EnhancementLayerPresent);Assert.Null(info.BaseLayerPresent);
    }
    [Theory]
    [InlineData("hevc","dvhe",8,"dvhe.08.06")]
    [InlineData("h264","dvav",9,"dvav.09.06")]
    [InlineData("h264","dva1",9,"dva1.09.06")]
    [InlineData("av1","av01",10,"dav1.10.06")]
    public void OriginalDvIdentifiersRetainTheCodecFamilyAndObservedSampleEntry(string codec,string tag,int profile,string expected)
    {
        using var json=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new{codec_name=codec,codec_tag_string=tag,side_data_list=new[]{new{dv_profile=profile,dv_level=6}}}));
        Assert.Equal(expected,CodecIdentifiers.FromProbe(json.RootElement));
    }
    [Fact]
    public async Task OldCachedTrackAnalysisRefreshesReadOnlyAndKeepsTheMediaIdentityAndProgress()
    {
        using var home=new TestWorkspace();var source=home.Write("ORIGINAL Dolby analysis contract.mp4");var library=home.AddLibrary();await home.Scan(library.Id);
        var media=Assert.Single(home.Store.GetCatalog(null,null,null,0,60).Items);home.Store.SaveProgress(media.Id,new(27,100));
        var fingerprint=MediaAnalysisService.Fingerprint(new FileInfo(source));home.Store.SaveAnalysis(media.Id,fingerprint,new(100,null,null,null,null,AnalysisVersion:MediaTools.CurrentAnalysisVersion-1));var before=StateFolders.Hash(source);
        var probe=new Probe();var analysis=new MediaAnalysisService(home.Store,probe);
        var first=await analysis.InspectAsync(media.Id,CancellationToken.None);var second=await analysis.InspectAsync(media.Id,CancellationToken.None);
        Assert.Equal(1,probe.Calls);Assert.Equal(first,second);Assert.Equal(MediaTools.CurrentAnalysisVersion,first.Probe.AnalysisVersion);
        Assert.Equal(media.Id,first.Media.Id);Assert.Equal(27,home.Store.GetMedia(media.Id)!.PositionSeconds);Assert.Equal(before,StateFolders.Hash(source));
    }
    private sealed class Probe:IMediaProbe
    {
        public bool FfprobeAvailable=>true;public int Calls;
        public Task<ProbeResult?> ProbeAsync(string path,CancellationToken token)
        {Calls++;return Task.FromResult<ProbeResult?>(new(100,null,null,null,null,AnalysisVersion:MediaTools.CurrentAnalysisVersion));}
    }
}

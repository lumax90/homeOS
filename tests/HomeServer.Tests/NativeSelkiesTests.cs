using System.Net;
using System.Net.Http.Json;
using HomeServer.Core;
using HomeServer.Gaming;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HomeServer.Tests;

public sealed class NativeSelkiesTests
{
    [Fact]
    public void ChangingThePublisherRequiresTheExactExistingFrontendAndCoreSaveContract()
    {
        var frontend=new NativeRuntimeFile("frontend",1,new string('a',64));var core=new NativeRuntimeFile("core",1,new string('b',64));
        var profile=new NativeRuntimeProfile(2,true,"0.10.5","26b7884bc25a5933960f3cdcd98bac1ae14d42e2",frontend,core,core,core,[],true,"linux-x64",true,core,true);
        var manifest=new NativeSelkiesManifest(1,"2.1.0",frontend.Sha256,new("retroarch",1,new string('c',64)),"python","python","web",[new("mgba",core.Sha256)],[]);
        var version=profile.Version;
        NativeSelkiesRuntime.RequireCompatibility(manifest,profile,"mgba",core.Sha256);
        Assert.Equal(version,profile.Version);
        Assert.Throws<DomainException>(()=>NativeSelkiesRuntime.RequireCompatibility(manifest,profile with{RetroArch=frontend with{Sha256=new string('d',64)}},"mgba",core.Sha256));
        Assert.Throws<DomainException>(()=>NativeSelkiesRuntime.RequireCompatibility(manifest,profile,"mgba",new string('e',64)));
        Assert.Throws<DomainException>(()=>NativeSelkiesRuntime.RequireCompatibility(manifest,profile,"citra",core.Sha256));
        Assert.Throws<DomainException>(()=>NativeSelkiesRuntime.RequireCompatibility(manifest,profile with{Platform="win-x64",Format=1},"mgba",core.Sha256));
    }
    [Theory]
    [InlineData("api/tokens")]
    [InlineData("api/switch")]
    [InlineData("api/upload")]
    [InlineData("api/files/manifest.json")]
    [InlineData("api/screenshot")]
    [InlineData("api/webrtc/signaling//")]
    [InlineData("api/webrtc/signaling/../tokens")]
    [InlineData("api/webrtc/signaling/%2f")]
    [InlineData("../tokens.json")]
    [InlineData("src/../../tokens.json")]
    [InlineData("src/%2e%2e/tokens.json")]
    [InlineData("src\\..\\tokens.json")]
    [InlineData("https://example.invalid/script.js")]
    public void ViewerProxyCannotBecomeAGenericDesktopOrFileManagementGateway(string path)=>Assert.False(NativeSelkiesProxy.Allowed(path));
    [Theory]
    [InlineData("api/webrtc/signaling")]
    [InlineData("api/webrtc/signaling/")]
    public void ReleasedBrowserAndRawProtocolSignallingUseTheSameExactAllowedRoute(string path)
    {
        Assert.True(NativeSelkiesProxy.Allowed(path));
        Assert.Equal("api/webrtc/signaling",NativeSelkiesProxy.CanonicalAsset(path));
        Assert.Equal("api/tokens/",NativeSelkiesProxy.CanonicalAsset("api/tokens/"));
        Assert.False(NativeSelkiesProxy.Allowed("api/tokens/"));
    }
    [Fact]
    public async Task AnAbsentAlternatePackageDoesNotCreateStateOrStartAnything()
    {
        using var home=new TestWorkspace();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Home:Games:SelkiesRuntimePath",Path.Combine(home.Root,"missing-package")}}).Build();
        var runtime=new NativeSelkiesRuntime(config);
        for(var i=0;i<3;i++){var status=runtime.Status();Assert.False(status.Available);Assert.True(status.Experimental);}
        Assert.False(Directory.Exists(Path.Combine(home.Root,"missing-package")));
        await using var app=new Factory(home);using var client=app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/games/native/backends")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/game-native/fixture/selkies/")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/game-native/fixture/selkies/api/webrtc/signaling/")).StatusCode);
    }
    [Fact]
    public void LegacyRequestsKeepStandardAndUnknownBackendsCannotSelectExecutablePaths()
    {
        Assert.Equal("standard",NativeSelkiesRuntime.Backend(null));Assert.Equal("standard",new NativeGameStart("fixture","ticket").StreamingBackend);
        Assert.Equal("selkies",NativeSelkiesRuntime.Backend("selkies"));
        foreach(var value in new[]{"auto","../other","https://example.invalid","Selkies"})Assert.Throws<DomainException>(()=>NativeSelkiesRuntime.Backend(value));
    }
    [Fact]
    public void AlternatePresentationUsesTheSameManagedInputAndSaveLocationsWithARealAudioOutput()
    {
        using var home=new TestWorkspace();var standard=RetroArchHostConfiguration.CreatePrivateHost(home.Root,62420,62406,null);
        var alternative=RetroArchHostConfiguration.CreateSelkies(home.Root,62420,62406,null,640,480);
        foreach(var name in new[]{"savefile_directory","savestate_directory","system_directory","network_remote_base_port","network_cmd_port","input_driver","input_joypad_driver"})
            Assert.Equal(standard.Split('\n').Single(line=>line.TrimStart().StartsWith(name+" =",StringComparison.Ordinal)),alternative.Split('\n').Single(line=>line.TrimStart().StartsWith(name+" =",StringComparison.Ordinal)));
        Assert.Contains("audio_driver = \"pulse\"",alternative);Assert.Contains("audio_enable = \"true\"",alternative);Assert.Contains("video_gpu_record = \"false\"",alternative);
        Assert.Contains("video_driver = \"sdl2\"",alternative);Assert.DoesNotContain("video_driver = \"glcore\"",alternative);
        Assert.Contains("suspend_screensaver_enable = \"false\"",alternative);
    }
    private sealed class Factory(TestWorkspace home):WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {builder.UseEnvironment("Testing");builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{{"Home:DataPath",home.Paths.Data},{"Home:Downloads:LoopbackOnly","true"}}));}
    }
}

using HomeServer.Gaming;
using Microsoft.Extensions.Configuration;

namespace HomeServer.Tests;

public sealed class GamingSupportedSystemsTests
{
    [Fact]
    public void MissingPackagesAreVisibleAndNeverClaimNintendoReadyOrTheEntireCatalogSupported()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("Isolated owner","isolated"));var actor=home.Store.GetAccount("1")!;
        var catalog=new RetroArchCoreCatalog(new GameStoreTests.EnvironmentStub(AppContext.BaseDirectory));var config=new ConfigurationBuilder().Build();
        var handheld=new NativeHandheldRuntime(config,catalog);var registry=new NativeCoreRegistry(home.Paths,catalog,new(home.Paths),psp:new(config,catalog),handheld:handheld);
        var support=new GamingSupportedSystems(home.Paths,registry).Read(actor);
        Assert.All(support.Items,system=>Assert.False(system.Ready));
        Assert.Equal("experimental",support.Items.Single(item=>item.Id=="3ds").State);
        Assert.Equal("experimental",support.Items.Single(item=>item.Id=="nds").State);
        Assert.Equal("unsupported",support.Items.Single(item=>item.Id=="ps3").State);
        Assert.Equal("unsupported",support.Items.Single(item=>item.Id=="steam").State);
        Assert.True(support.Items.Length<30);Assert.True(catalog.Data.Entries.Length>support.Items.Length);
        Assert.Empty(home.Store.GetGameRoms(actor));Assert.Empty(home.Store.GetProductJobs(actor));
    }
    [Fact]
    public void ExistingNintendoSystemIdentityAndSaveRootsAreRetained()
    {
        Assert.Equal("desmume",NativeSystems.Require("nds").Core);Assert.Equal("citra",NativeSystems.Require("3ds").Core);
        Assert.Contains("desmume_pointer_type = \"touch\"",NativeCoreConfiguration.Options("desmume"));
        Assert.Contains("desmume_screens_layout = \"top/bottom\"",NativeCoreConfiguration.Options("desmume"));
        Assert.Contains("citra_touch_touchscreen = \"enabled\"",NativeCoreConfiguration.Options("citra"));
        Assert.Contains("LibRetro Default",NativeCoreConfiguration.RequiredOptions("citra"));
        Assert.True(NativeSaveSnapshots.SafeName("Citra/sdmc/Nintendo 3DS/isolated/metadata.bin"));
        Assert.True(NativeSaveSnapshots.SafeName("game.dsv"));
    }
    [Fact]
    public void SelkiesDoesNotReplaceAHardwareCoreContextWithTheSoftwareSdlDriver()
    {
        using var home=new TestWorkspace();var ds=RetroArchHostConfiguration.CreateSelkies(home.Root,62420,62406,null,256,384);
        Assert.Contains("video_driver = \"sdl2\"",ds);
        var citra=RetroArchHostConfiguration.CreateSelkies(home.Root,62420,62406,"glcore",400,480);
        Assert.Contains("video_driver = \"glcore\"",citra);Assert.DoesNotContain("video_driver = \"sdl2\"",citra);
        Assert.Contains("audio_driver = \"pulse\"",citra);Assert.Contains("network_remote_base_port = \"62420\"",citra);
    }
}

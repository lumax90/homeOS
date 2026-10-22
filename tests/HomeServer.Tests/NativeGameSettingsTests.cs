using System.Text.Json;
using HomeServer.Core;
using HomeServer.Gaming;

namespace HomeServer.Tests;

public sealed class NativeGameSettingsTests
{
    private const string Core="pcsx_rearmed",Key="pcsx_rearmed_neon_enhancement_enable";
    private static readonly string Hash=new('a',64);
    private static GameRom Rom(string id="rom-original-a",string user="1")=>new(id,user,"ps1","Original.chd","Original",new string('b',64),512,"");
    private static void Observe(NativeGameSettings settings,string user="1",string core=Core)=>settings.Observe(user,core,Hash,[new(0,Key,"Enhanced Resolution","Original fixture option","disabled",["disabled","enabled"]),new(1,"pcsx_rearmed_region","Region","","auto",["auto","PAL","NTSC"]),new(2,"fixture_save_path","Save path","","managed",["managed","outside"])],[new(0,1,[new(1,"Original pad"),new(257,"Original three-button pad")])]);
    [Fact]
    public void GameOverrideIsPrivateAndDoesNotChangeAnotherGameCoreOrProfile()
    {
        using var home=new TestWorkspace();var settings=new NativeGameSettings(home.Paths);Observe(settings);Observe(settings,"2");Observe(settings,"1","mgba");
        var saved=settings.Save(Rom(),Core,"Original core",Hash,new("game",0,new(){{Key,"enabled"}},new(Fit:"integer",Quality:"quality"),[new(0,257)]));
        Assert.Equal("enabled",saved.Options.Single(o=>o.Key==Key).Value);Assert.Equal("game",saved.Options.Single(o=>o.Key==Key).Source);Assert.Equal("disabled",settings.View(Rom("rom-original-b"),Core,"",Hash).Options.Single(o=>o.Key==Key).Value);
        Assert.Equal("disabled",settings.View(Rom(user:"2"),Core,"",Hash).Options.Single(o=>o.Key==Key).Value);Assert.Equal("disabled",settings.View(Rom(),"mgba","",Hash).Options.Single(o=>o.Key==Key).Value);
        Assert.Null(settings.Quality("1",Core,Hash,"rom-original-b"));Assert.Equal("quality",settings.Quality("1",Core,Hash,Rom().Id));Assert.Equal("",settings.ControllerConfiguration("1",Core,Hash,"rom-original-b").Trim());Assert.Contains("257",settings.ControllerConfiguration("1",Core,Hash,Rom().Id));
    }
    [Fact]
    public void InheritanceAndResetFollowUpdatedEmulatorDefaultsWithoutFreezingUnchangedOptions()
    {
        using var home=new TestWorkspace();var settings=new NativeGameSettings(home.Paths);Observe(settings);
        settings.Save(Rom(),Core,"",Hash,new("game",0,new(){{Key,"enabled"}}));settings.Save(Rom(),Core,"",Hash,new("core",0,new(){{"pcsx_rearmed_region","PAL"}}));
        var view=settings.View(Rom(),Core,"",Hash);Assert.Equal("PAL",view.Options.Single(o=>o.Key=="pcsx_rearmed_region").Value);Assert.Equal("core",view.Options.Single(o=>o.Key=="pcsx_rearmed_region").Source);
        view=settings.Save(Rom(),Core,"",Hash,new("game",1,new(){{Key,null}}));Assert.Equal("disabled",view.Options.Single(o=>o.Key==Key).Value);Assert.NotEqual("game",view.Options.Single(o=>o.Key==Key).Source);
        settings.Save(Rom(),Core,"",Hash,new("game",2,new(){{Key,"enabled"}},new(Fit:"fill"),[new(0,257)]));view=settings.Save(Rom(),Core,"",Hash,new("game",3,Reset:true));Assert.Empty(view.GameControllers);Assert.Null(view.GameDisplay.Fit);Assert.Equal("PAL",view.Options.Single(o=>o.Key=="pcsx_rearmed_region").Value);Assert.Equal("disabled",view.Options.Single(o=>o.Key==Key).Value);
    }
    [Fact]
    public void LegacyCorePreferencesArePreservedAndNewBinarySettingsDoNotOverwriteOldVersions()
    {
        using var home=new TestWorkspace();var legacy=new NativeCoreOptions(home.Paths);legacy.Save("1",Core,Hash,[new(0,Key,"Original option","","enabled",["disabled","enabled"])]);
        var path=Path.Combine(home.Paths.State,"games","core-options","1",Core,"values.json");var before=File.ReadAllBytes(path);var settings=new NativeGameSettings(home.Paths);Observe(settings);
        settings.Save(Rom(),Core,"",Hash,new("game",0,new(){{Key,"disabled"}}));Assert.Equal(before,File.ReadAllBytes(path));Assert.Equal("enabled",settings.View(Rom("rom-b"),Core,"",Hash).Options.Single(o=>o.Key==Key).Value);
        var newer=new string('c',64);Assert.False(settings.View(Rom(),Core,"",newer).SchemaReady);Assert.Equal(0,settings.View(Rom(),Core,"",newer).Revision);Assert.Contains("disabled",settings.Configuration("1",Core,Hash,Rom().Id));Assert.DoesNotContain(Key,settings.Configuration("1",Core,newer,Rom().Id));Assert.Equal(before,File.ReadAllBytes(path));
    }
    [Fact]
    public void StaleInvalidManagedAndConflictingControllerChangesCannotPartiallyPublish()
    {
        using var home=new TestWorkspace();var settings=new NativeGameSettings(home.Paths);Observe(settings);
        Assert.Throws<DomainException>(()=>settings.Save(Rom(),Core,"",Hash,new("game",0,new(){{Key,"enabled"}},new(Fit:"unbounded"))));Assert.Equal(0,settings.View(Rom(),Core,"",Hash).Revision);
        Assert.Throws<DomainException>(()=>settings.Save(Rom(),Core,"",Hash,new("game",0,new(){{"fixture_save_path","outside"}})));
        Assert.Throws<DomainException>(()=>settings.Save(Rom(),Core,"",Hash,new("game",0,new(){{Key,"unknown"}})));
        Assert.Throws<DomainException>(()=>settings.Save(Rom(),Core,"",Hash,new("game",0,Controllers:[new(0,999)])));
        Assert.Throws<DomainException>(()=>settings.Save(Rom(),Core,"",Hash,new("game",0,Controllers:[new(0,1),new(0,257)])));
        settings.Save(Rom(),Core,"",Hash,new("game",0,new(){{Key,"enabled"}}));Assert.Equal(409,Assert.Throws<DomainException>(()=>settings.Save(Rom(),Core,"",Hash,new("game",0,new(){{Key,"disabled"}}))).StatusCode);Assert.Equal("enabled",settings.View(Rom(),Core,"",Hash).Options.Single(o=>o.Key==Key).Value);
        Assert.Throws<DomainException>(()=>settings.Configuration("../foreign",Core,Hash,Rom().Id));Assert.Throws<DomainException>(()=>settings.Save(Rom(),Core,"",Hash,new("everything",1)));Assert.Throws<DomainException>(()=>settings.Save(Rom(),Core,"",Hash,new("core",0,Reset:true)));
    }
    [Fact]
    public void GeneratedConfigurationPlacesUserChoicesAfterDefaultsAndSecurityPathsLast()
    {
        using var home=new TestWorkspace();var settings=new NativeGameSettings(home.Paths);settings.Observe("1","citra",Hash,[new(0,"citra_resolution_factor","Resolution","","1x",["1x","2x"])],[]);settings.Save(Rom(),"citra","",Hash,new("game",0,new(){{"citra_resolution_factor","2x"}}));
        var config=NativeCoreConfiguration.Options("citra")+settings.Configuration("1","citra",Hash,Rom().Id)+NativeCoreConfiguration.RequiredOptions("citra");Assert.True(config.LastIndexOf("2x",StringComparison.Ordinal)>config.LastIndexOf("1x",StringComparison.Ordinal));Assert.Contains("LibRetro Default",config);Assert.True(NativeCoreOptions.NeedsRestart(new(0,Key,"Enhanced Resolution","","enabled",["disabled","enabled"])));
    }
}

using HomeServer.Gaming;

namespace HomeServer.Tests;

public sealed class NativeBasicSelectionTests
{
    [Fact]
    public void ProductFocusContainsExactlySevenSystemsAndFiveMatureCores()
    {
        Assert.Equal(new[]{"gb","gbc","gba","nes","snes","megadrive","ps1"},NativeBasicSelection.Systems);
        Assert.Equal(new[]{"mgba","fceumm","snes9x","genesis_plus_gx","pcsx_rearmed"},NativeBasicSelection.Cores);
        Assert.Equal(new[]{"gb","gbc","gba"},NativeBasicSelection.CoreSystems("mgba"));
        Assert.Equal(new[]{"nes"},NativeBasicSelection.CoreSystems("fceumm"));
        Assert.Equal(new[]{"snes"},NativeBasicSelection.CoreSystems("snes9x"));
        Assert.Equal(new[]{"megadrive"},NativeBasicSelection.CoreSystems("genesis_plus_gx"));
        Assert.Equal(new[]{"ps1"},NativeBasicSelection.CoreSystems("pcsx_rearmed"));
        Assert.Empty(NativeBasicSelection.CoreSystems("citra"));Assert.Empty(NativeBasicSelection.CoreSystems("dolphin"));Assert.False(NativeBasicSelection.Includes("ps3"));
    }
    [Fact]
    public void DefaultOrderingDoesNotRemoveAdvancedSystemsOrChangeExistingRouting()
    {
        var ordered=NativeBasicSelection.Order(NativeSystems.All).ToArray();
        Assert.Equal(NativeBasicSelection.Systems,ordered.Take(7).Select(system=>system.Id));
        Assert.Equal(NativeSystems.All.Length,ordered.Length);
        Assert.Equal(NativeSystems.All.Select(system=>system.Id).Order(),ordered.Select(system=>system.Id).Order());
        Assert.Contains(ordered,system=>system.Id=="3ds");Assert.Equal("citra",NativeSystems.Require("3ds").Core);
        Assert.False(NativeBasicSelection.Includes("3ds"));Assert.Equal("mgba",NativeSystems.Require("gba").Core);
    }
}

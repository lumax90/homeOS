namespace HomeServer.Gaming;

/// <summary>Product focus, not a claim that a catalogue entry passed real game/device acceptance.</summary>
public static class NativeBasicSelection
{
    public static readonly string[] Systems=["gb","gbc","gba","nes","snes","megadrive","ps1"];
    public static readonly string[] Cores=["mgba","fceumm","snes9x","genesis_plus_gx","pcsx_rearmed"];
    public static bool Includes(string system)=>Systems.Contains(system,StringComparer.Ordinal);
    public static string[] CoreSystems(string id)=>Systems.Where(system=>NativeSystems.Require(system).Core==id).ToArray();
    public static IEnumerable<NativeSystem> Order(IEnumerable<NativeSystem> systems)=>systems.OrderBy(system=>Includes(system.Id)?Array.IndexOf(Systems,system.Id):Systems.Length).ThenBy(system=>system.Name,StringComparer.Ordinal);
}

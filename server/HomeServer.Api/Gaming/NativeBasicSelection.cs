namespace HomeServer.Gaming;

/// <summary>Product focus, not a claim that a catalogue entry passed real game/device acceptance.</summary>
public static class NativeBasicSelection
{
    public static readonly string[] Systems=["gb","gbc","gba","nes","snes","megadrive","ps1"];
    public static readonly string[] Cores=["mgba","fceumm","snes9x","genesis_plus_gx","pcsx_rearmed"];
    public static bool Includes(string system)=>system is "psp" or "nds" or "3ds"||Systems.Contains(system,StringComparer.Ordinal);
    public static string[] CoreSystems(string id)=>id switch{"ppsspp"=>["psp"],"desmume"=>["nds"],"citra"=>["3ds"],_=>Systems.Where(system=>NativeSystems.Require(system).Core==id).ToArray()};
    public static IEnumerable<NativeSystem> Order(IEnumerable<NativeSystem> systems)=>systems.OrderBy(system=>system.Id switch{"psp"=>Systems.Length,"nds"=>Systems.Length+1,"3ds"=>Systems.Length+2,_=>Includes(system.Id)?Array.IndexOf(Systems,system.Id):Systems.Length+3}).ThenBy(system=>system.Name,StringComparer.Ordinal);
}

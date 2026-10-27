using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Gaming;

public sealed record GamingSystemSupport(string Id,string Name,string State,bool Installed,bool Ready,string? Core,string[] Backends,string? Reason=null);
public sealed record GamingSupportView(string ServerPlatform,GamingSystemSupport[] Items,string Note);

/// <summary>Product/runtime capability, never the entire upstream catalog presented as working support.</summary>
public sealed class GamingSupportedSystems(HomePaths paths,NativeCoreRegistry cores,NativeSelkiesRuntime? selkies=null)
{
    private static readonly (string Id,string Name)[] Unqualified=[("n64","Nintendo 64"),("ps2","PlayStation 2"),("ps3","PlayStation 3"),("vita","PlayStation Vita"),("dreamcast","Dreamcast"),("gamecube","GameCube"),("wii","Wii"),("switch","Nintendo Switch"),("steam","Steam")];
    public GamingSupportView Read(Account actor)
    {
        NativeRuntimeProfile? host=null;try{host=NativeRuntimeProfile.Read(paths);}catch(DomainException){}
        var systems=NativeBasicSelection.Systems.Concat(["psp","nds","3ds"]).Distinct().Select(id=>
        {
            var definition=NativeSystems.Require(id);var installed=id is "gb" or "gbc" or "gba"?host is not null:false;string? reason=null;string? coreHash=null;
            try{var current=cores.Installed(definition.Core);installed=definition.Core=="mgba"?host is not null:current is not null;coreHash=definition.Core=="mgba"?host?.Core.Sha256:current?.Binary.Sha256;reason=cores.BlockReason(new("",actor.Id,id,"game."+definition.Extensions[0],"","",16,""),host);}
            catch(Exception error)when(error is DomainException or IOException or System.Text.Json.JsonException){reason="Bu sistemin sunucu paketi doğrulanamadı.";}
            var ready=installed&&reason is null;
            var state=id=="3ds"?"experimental":id=="nds"&&!ready?"experimental":id=="psp"&&!installed?"experimental":"supported";
            var backends=id!="psp"&&host is not null&&coreHash is not null&&selkies?.Supports(host,definition.Core,coreHash)==true?new[]{"standard","selkies"}:new[]{"standard"};
            return new GamingSystemSupport(id,definition.Name,state,installed,ready,definition.Core,backends,reason);
        }).Concat(Unqualified.Select(item=>new GamingSystemSupport(item.Id,item.Name,"unsupported",false,false,null,[],"Bu sunucu profilinde hazır ve doğrulanmış Home desteği yok."))).ToArray();
        return new(host?.Platform??System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,systems,"Destek listesi Home entegrasyonunu gösterir; her oyunun ve cihazın performansı ayrıca denenmelidir.");
    }
}

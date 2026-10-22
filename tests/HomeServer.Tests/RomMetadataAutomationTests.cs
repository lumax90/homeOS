using System.Text.Json;
using HomeServer.Core;
using HomeServer.Gaming;
using HomeServer.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeServer.Tests;

public sealed class RomMetadataAutomationTests
{
    private static (Account Actor,string Login) Owner(TestWorkspace home){home.Store.CreateOwner(new("Owner","isolated-secret"));var actor=home.Store.GetAccount("1")!;return(actor,home.Store.CreateLoginSession("1",home.Store.GetAccountSecret("1")!.Version,"automation-fixture").Id);}
    private sealed class NoNetwork:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>throw new InvalidOperationException("No network is allowed in the queue-only fixture.");}
    [Fact]
    public void HundredsOfExistingRomsUseBoundedRefillAndDisablingCancelsOnlyAutomaticWork()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);for(var i=0;i<205;i++)home.Store.RegisterGameRom("Original "+i+".chd",i.ToString("x64"),512,actor,login,"ps1");
        using var art=new RomArtwork(home.Paths,home.Store,new EventHub(),new NoNetwork());using var client=new HasheousMetadataClient(new NoNetwork());var preferences=new GameLibraryPreferences(home.Paths,home.Store,new EventHub());var metadata=new RomMetadata(home.Paths,home.Store,new EventHub(),art,client,preferences);using var automation=new RomMetadataAutomation(home.Store,metadata,preferences,NullLogger<RomMetadataAutomation>.Instance);
        Assert.Equal(8,automation.Fill());Assert.Equal(4,automation.Fill());Assert.Equal(0,automation.Fill());Assert.Equal(12,home.Store.PendingProductJobCount("rom-metadata"));
        preferences.Save(actor,login,new(0,false,true,"auto"));Assert.Equal(0,home.Store.PendingProductJobCount("rom-metadata"));Assert.Equal(0,automation.Fill());Assert.Equal(205,home.Store.GetGameRoms(actor).Count);Assert.Empty(home.Store.GetActiveGameSessions());
        preferences.Save(actor,login,new(1,true,true,"eu"));Assert.Equal(8,automation.Fill());Assert.Equal(8,home.Store.PendingProductJobCount("rom-metadata"));
        using var restarted=new RomMetadataAutomation(home.Store,metadata,preferences,NullLogger<RomMetadataAutomation>.Instance);Assert.Equal(4,restarted.Fill());Assert.Equal(12,home.Store.PendingProductJobCount("rom-metadata"));
    }
    [Fact]
    public void DurableReceiptsSuppressRetryStormsAndRescanGenerationExplicitlyRevisitsTheCollection()
    {
        using var home=new TestWorkspace();var(actor,login)=Owner(home);var rom=home.Store.RegisterGameRom("Original.chd",new string('a',64),512,actor,login,"ps1");using var art=new RomArtwork(home.Paths,home.Store,new EventHub(),new NoNetwork());using var client=new HasheousMetadataClient(new NoNetwork());var prefs=new GameLibraryPreferences(home.Paths,home.Store,new EventHub());var metadata=new RomMetadata(home.Paths,home.Store,new EventHub(),art,client,prefs);
        var root=AppFiles.DirectoryIn(home.Paths.State,"games","metadata",actor.Id,rom.Id);var retryAt=DateTimeOffset.UtcNow.AddHours(1);File.WriteAllText(Path.Combine(root,"automation.json"),JsonSerializer.Serialize(new RomMetadataAttempt(1,rom.Sha256,RomMetadata.PipelineVersion,0,"failed",3,retryAt.ToString("O"))));
        Assert.False(metadata.ShouldEnrich(rom,actor,prefs.Read(actor.Id),DateTimeOffset.UtcNow));Assert.True(metadata.ShouldEnrich(rom,actor,prefs.Read(actor.Id),retryAt.AddSeconds(1)));prefs.Refresh(actor,login,0);Assert.True(metadata.ShouldEnrich(rom,actor,prefs.Read(actor.Id),DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(()=>prefs.Save(actor,login,new(0,false,true,"auto")));Assert.Throws<DomainException>(()=>prefs.Save(actor,login,new(1,true,true,"invalid")));Assert.Equal(1,prefs.Read(actor.Id).Revision);
    }
}

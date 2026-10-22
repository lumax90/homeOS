using System.Text;
using System.Buffers.Binary;
using HomeServer.Core;
using HomeServer.Gaming;

namespace HomeServer.Tests;

// Original metadata/header fixtures only. No user ROM, provider data, emulator or browser execution.
public sealed class NativeRomDetectionTests
{
    private static NativeRomProbeFile File(string name,byte[]? prefix=null,long bytes=65536)=>new(name,bytes,prefix is null?null:Convert.ToBase64String(prefix));
    [Theory]
    [InlineData("test.gba","gba")]
    [InlineData("test.GBC","gbc")]
    [InlineData("test.nes","nes")]
    [InlineData("test.sfc","snes")]
    [InlineData("test.gen","megadrive")]
    public void UnambiguousFormatsAreSuggestedWithoutImportingAnything(string name,string system)
    {var result=NativeRomDetection.Detect(new([File(name)],name));Assert.Equal(system,result.SystemId);Assert.Empty(result.MissingFiles);}
    [Theory]
    [InlineData("SEGA MEGA DRIVE ","megadrive")]
    [InlineData("SEGA 32X        ","sega32x")]
    public void SharedMdExtensionUsesTheConsoleHeaderRatherThanAssumingMegaDrive(string header,string system)
    {var prefix=new byte[512];Encoding.ASCII.GetBytes(header).CopyTo(prefix,0x100);var result=NativeRomDetection.Detect(new([File("original.md",prefix)],"original.md"));Assert.Equal(system,result.SystemId);Assert.Equal("header",result.Reason);}
    [Fact]
    public void CueAndBinAreNotAConsoleIdentityAndCaseOnlyReferencesArePresent()
    {
        var cue=Encoding.UTF8.GetBytes("FILE \"original track.bin\" BINARY\r\n TRACK 01 MODE2/2352\r\n INDEX 01 00:00:00\r\n");
        var result=NativeRomDetection.Detect(new([File("Original.cue",cue,cue.Length),File("Original Track.BIN")],"Original.cue"));
        Assert.Null(result.SystemId);Assert.Contains("ps1",result.Candidates);Assert.Contains("saturn",result.Candidates);Assert.Empty(result.MissingFiles);Assert.Equal("ambiguous",result.Reason);
    }
    [Theory]
    [InlineData("BOOT = cdrom:\\SLUS_000.00;1\r\n","ps1")]
    [InlineData("BOOT2 = cdrom0:\\SLUS_000.00;1\r\n","ps2")]
    public void StructuredBootConfigurationSeparatesPlayStationGenerations(string text,string system)
    {
        var cue=Encoding.UTF8.GetBytes("FILE \"track.bin\" BINARY\n TRACK 01 MODE2/2352\n INDEX 01 00:00:00\n");var result=NativeRomDetection.Detect(new([File("entry.cue",cue,cue.Length),File("track.bin",Encoding.ASCII.GetBytes(text))],"entry.cue"));Assert.Equal(system,result.SystemId);Assert.Equal("header",result.Reason);
    }
    [Fact]
    public void SharedPlayStationVolumeLabelAndDiscFilenameCannotSilentlyChoosePs1()
    {var result=NativeRomDetection.Detect(new([File("PlayStation.iso",Encoding.ASCII.GetBytes("PLAYSTATION"))],"PlayStation.iso"));Assert.Null(result.SystemId);Assert.Contains("ps1",result.Candidates);Assert.Contains("ps2",result.Candidates);}
    [Fact]
    public void MissingTrackIsIdentifiedBeforeTheLargePayloadUpload()
    {var cue=Encoding.UTF8.GetBytes("FILE \"required.bin\" BINARY\n");var result=NativeRomDetection.Detect(new([File("entry.cue",cue,cue.Length)],"entry.cue"));Assert.Equal(new[]{"required.bin"},result.MissingFiles);}
    [Fact]
    public void UnicodeBomCueUsesTheSameTextDecodingAsImportValidation()
    {var text="FILE \"original.bin\" BINARY\r\n";var cue=Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();var result=NativeRomDetection.Detect(new([File("entry.cue",cue,cue.Length),File("original.bin")],"entry.cue"));Assert.Empty(result.MissingFiles);}
    [Theory]
    [InlineData("FILE \"../outside.bin\" BINARY\n")]
    [InlineData("FILE \"C:/outside.bin\" BINARY\n")]
    [InlineData("CDTEXTFILE \"outside.txt\"\nFILE \"track.bin\" BINARY\n")]
    public void UnsafeCueReferencesStayRejectedDuringDetection(string text)
    {var cue=Encoding.UTF8.GetBytes(text);Assert.Throws<DomainException>(()=>NativeRomDetection.Detect(new([File("entry.cue",cue,cue.Length)],"entry.cue")));}
    [Fact]
    public void ProbeCountSizeInvalidBase64AndCaseCollisionsAreBounded()
    {
        Assert.Throws<DomainException>(()=>NativeRomDetection.Detect(new(Enumerable.Range(0,129).Select(i=>File(i+".gba")).ToArray(),"0.gba")));
        Assert.Throws<DomainException>(()=>NativeRomDetection.Detect(new([new("test.gba",65536,"not base64")],"test.gba")));
        Assert.Throws<DomainException>(()=>NativeRomDetection.Detect(new([File("test.gba",new byte[65537],65537)],"test.gba")));
        Assert.Throws<DomainException>(()=>NativeRomDetection.Detect(new([File("same.gba"),File("SAME.GBA")],"same.gba")));
        Assert.Throws<DomainException>(()=>NativeRomDetection.Detect(new([File("../unsafe.gba")],"../unsafe.gba")));
        Assert.Throws<DomainException>(()=>NativeRomDetection.Detect(new([new(null!,10)],"")));
        Assert.Throws<DomainException>(()=>NativeRomDetection.Detect(new(Enumerable.Range(0,9).Select(i=>File(i+".gba",new byte[65536])).ToArray(),"0.gba")));
    }
    [Theory]
    [InlineData(2048,0,false)]
    [InlineData(2352,24,false)]
    [InlineData(2352,16,true)]
    public void DiscBootFileOutsidePrefixIsDetectedThroughBoundedDirectoryAndBootReads(int stride,int payload,bool ps2)
    {
        // Original structural ISO fixture, no acquired disc or BIOS/game data.
        var bytes=new byte[stride*110];var pvd=16*stride+payload;bytes[pvd]=1;"CD001"u8.CopyTo(bytes.AsSpan(pvd+1));bytes[pvd+6]=1;BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(pvd+128),2048);bytes[pvd+156]=34;BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(pvd+158),70);BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(pvd+166),2048);
        var directory=70*stride+payload;bytes[directory]=48;BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(directory+2),90);var boot=Encoding.ASCII.GetBytes(ps2?"BOOT2 = cdrom0:\\SLUS_000.00;1\r\n":"BOOT = cdrom:\\SLUS_000.00;1\r\n");BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(directory+10),(uint)boot.Length);bytes[directory+32]=12;"SYSTEM.CNF;1"u8.CopyTo(bytes.AsSpan(directory+33));boot.CopyTo(bytes,90*stride+payload);
        var original=new NativeRomProbeFile("Original.bin",bytes.Length,Convert.ToBase64String(bytes[..65536]));var cue=Encoding.UTF8.GetBytes("FILE \"Original.bin\" BINARY\n TRACK 01 MODE2/2352\n INDEX 01 00:00:00\n");var request=new NativeRomProbeRequest([File("Original.cue",cue,cue.Length),original],"Original.cue");var result=NativeRomDetection.Detect(request);Assert.Null(result.SystemId);Assert.Equal("reading-disc",result.Reason);
        for(var i=0;i<2;i++){Assert.Single(result.Reads!);var read=result.Reads![0];Assert.InRange(read.Length,1,65536);Assert.Equal("Original.bin",read.Name);original=original with{Samples=[..original.Samples??[],new(read.Offset,Convert.ToBase64String(bytes.AsSpan((int)read.Offset,read.Length)))]};request=request with{Files=[request.Files[0],original]};result=NativeRomDetection.Detect(request);}
        Assert.Equal(ps2?"ps2":"ps1",result.SystemId);Assert.Empty(result.MissingFiles);
    }
    [Fact]
    public void MalformedAdditionalSamplesCannotRequestOutsideTheSelectedFile()
    {
        Assert.Throws<DomainException>(()=>NativeRomDetection.Detect(new([new("original.iso",65536,null,[new(-1,"AA==")])],"original.iso")));
        Assert.Throws<DomainException>(()=>NativeRomDetection.Detect(new([new("original.iso",65536,null,[new(65535,"AAAA")])],"original.iso")));
    }
}

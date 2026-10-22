using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;
using Microsoft.Extensions.Configuration;

namespace HomeServer.Tests;

public sealed class PackageTests
{
    [Fact]
    public async Task SignedPayloadIsVerifiedAndChangedBytesCannotBeActivated()
    {
        using var home = new TestWorkspace(); using var key = RSA.Create(2048);
        var archive = Path.Combine(home.Root, "release.zip"); await Build(archive, key);
        var directory = Path.Combine(home.Root, "release"); Directory.CreateDirectory(directory);
        var manifest = await PackageTrust.ExtractAsync(archive, directory, key.ExportSubjectPublicKeyInfoPem(), CancellationToken.None);
        Assert.Equal("9.0.0", manifest.Version);
        await PackageTrust.VerifyDirectoryAsync(directory, key.ExportSubjectPublicKeyInfoPem(), CancellationToken.None);
        await File.AppendAllTextAsync(Path.Combine(directory, manifest.EntryPoint), "tampered");
        await Assert.ThrowsAsync<DomainException>(() => PackageTrust.VerifyDirectoryAsync(directory, key.ExportSubjectPublicKeyInfoPem(), CancellationToken.None));
    }
    [Fact]
    public async Task AnUploadedKeyCannotAuthenticateItsOwnPackage()
    {
        using var home = new TestWorkspace(); using var trusted = RSA.Create(2048); using var attacker = RSA.Create(2048);
        var archive = Path.Combine(home.Root, "untrusted.zip"); await Build(archive, attacker);
        var directory = Path.Combine(home.Root, "release"); Directory.CreateDirectory(directory);
        await Assert.ThrowsAsync<DomainException>(() => PackageTrust.ExtractAsync(archive, directory, trusted.ExportSubjectPublicKeyInfoPem(), CancellationToken.None));
        Assert.Empty(Directory.GetFiles(directory));
    }
    [Theory]
    [InlineData("../escape.exe")]
    [InlineData("C:/escape.exe")]
    [InlineData("nested/../../escape.exe")]
    public async Task SignedArchivesStillCannotEscapeTheStagingBoundary(string name)
    {
        using var home = new TestWorkspace(); using var key = RSA.Create(2048);
        var archive = Path.Combine(home.Root, "unsafe.zip"); await Build(archive, key, extraPath: name);
        var directory = Path.Combine(home.Root, "release"); Directory.CreateDirectory(directory);
        await Assert.ThrowsAsync<DomainException>(() => PackageTrust.ExtractAsync(archive, directory, key.ExportSubjectPublicKeyInfoPem(), CancellationToken.None));
        Assert.Empty(Directory.GetFiles(directory));
    }
    [Fact]
    public async Task PreparingAndCancellingNeverChangesTheCurrentDatabaseOrReleasePointer()
    {
        using var home = new TestWorkspace(); using var key = RSA.Create(2048);
        home.Store.CreateOwner(new Owner("Owner", "original-hash"));
        var keyPath = Path.Combine(home.Root, "trusted.pem"); await File.WriteAllTextAsync(keyPath, key.ExportSubjectPublicKeyInfoPem());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Home:Updates:TrustedPublicKeyPath"] = keyPath }).Build();
        var updates = new UpdateService(home.Paths, config, new BackupService(home.Paths, home.Store), new RestartCoordinator());
        var archive = Path.Combine(home.Root, "release.zip"); await Build(archive, key);
        await using (var source = File.OpenRead(archive)) await updates.PrepareAsync(source, CancellationToken.None);
        Assert.NotNull(updates.Pending); Assert.False(File.Exists(Path.Combine(home.Paths.Data, "updates", "active.json")));
        Assert.Equal("Owner", home.Store.GetOwner()!.DisplayName);
        await updates.CancelAsync(CancellationToken.None); Assert.Null(updates.Pending);
        Assert.Empty(Directory.GetDirectories(Path.Combine(home.Paths.Data, "updates", "releases")));
    }
    private static async Task Build(string path, RSA key, string? extraPath = null)
    {
        var runtime = RuntimeInformation.RuntimeIdentifier; var entry = OperatingSystem.IsWindows() ? "HomeServer.Api.exe" : "HomeServer.Api";
        var payload = "isolated package test bytes; never executed"u8.ToArray();
        var files = new List<PackageFile> { new(entry, payload.Length, Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), true) };
        if (extraPath is not null) files.Add(new(extraPath, payload.Length, files[0].Sha256));
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new PackageManifest(1, "9.0.0", runtime, MediaStore.LatestSchema, entry, files.ToArray()));
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        await using (var stream = archive.CreateEntry(PackageTrust.ManifestName).Open()) await stream.WriteAsync(manifest);
        await using (var stream = archive.CreateEntry(PackageTrust.SignatureName).Open()) await stream.WriteAsync(key.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        foreach (var file in files) { await using var stream = archive.CreateEntry(file.Path).Open(); await stream.WriteAsync(payload); }
    }
}

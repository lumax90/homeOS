using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Operations;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: Home.Packager <publish-directory> <rid> <output.zip> <private-key.pem>");
    return 1;
}
var source = Path.GetFullPath(args[0]); var runtime = args[1]; var output = Path.GetFullPath(args[2]); var keyPath = Path.GetFullPath(args[3]);
if (runtime is not ("win-x64" or "linux-x64" or "linux-arm64")) throw new ArgumentException("Unsupported package runtime.");
var entry = runtime == "win-x64" ? "HomeServer.Api.exe" : "HomeServer.Api";
if (!File.Exists(Path.Combine(source, entry))) throw new InvalidOperationException("A self-contained Home publish directory is required.");
var version = AssemblyName.GetAssemblyName(Path.Combine(source, "HomeServer.Api.dll")).Version!.ToString(3);
using var rsa = RSA.Create(3072);
if (File.Exists(keyPath)) { AppFiles.RejectLink(keyPath); rsa.ImportFromPem(await File.ReadAllTextAsync(keyPath)); }
else
{
    Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
    await using (var key = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    await using (var writer = new StreamWriter(key)) await writer.WriteAsync(rsa.ExportPkcs8PrivateKeyPem());
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
}
var publicPath = Path.Combine(source, PackageTrust.PublicKeyName); var publicKey = rsa.ExportSubjectPublicKeyInfoPem();
if (File.Exists(publicPath) && (await File.ReadAllTextAsync(publicPath)).Trim() != publicKey.Trim()) throw new InvalidOperationException("Publish directory uses a different signing key.");
await File.WriteAllTextAsync(publicPath, publicKey);
var files = new List<PackageFile>();
foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
{
    var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
    if (relative is PackageTrust.ManifestName or PackageTrust.SignatureName) continue;
    _ = MediaPaths.ResolveFile(source, relative.Replace('/', Path.DirectorySeparatorChar));
    files.Add(new PackageFile(relative, new FileInfo(file).Length, await PackageTrust.HashAsync(file, CancellationToken.None), relative == entry || relative.EndsWith(".sh", StringComparison.Ordinal)));
}
var manifest = new PackageManifest(1, version, runtime, MediaStore.LatestSchema, entry, files.ToArray());
var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest); var signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
_ = PackageTrust.Authenticate(bytes, signature, publicKey);
await File.WriteAllBytesAsync(Path.Combine(source, PackageTrust.ManifestName), bytes); await File.WriteAllBytesAsync(Path.Combine(source, PackageTrust.SignatureName), signature);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using (var archive = ZipFile.Open(output, ZipArchiveMode.Create))
{
    foreach (var file in files) archive.CreateEntryFromFile(Path.Combine(source, file.Path.Replace('/', Path.DirectorySeparatorChar)), file.Path, CompressionLevel.Optimal);
    archive.CreateEntryFromFile(Path.Combine(source, PackageTrust.ManifestName), PackageTrust.ManifestName);
    archive.CreateEntryFromFile(Path.Combine(source, PackageTrust.SignatureName), PackageTrust.SignatureName);
}
await File.WriteAllTextAsync(output + ".sha256", await PackageTrust.HashAsync(output, CancellationToken.None) + "  " + Path.GetFileName(output) + "\n");
Console.WriteLine($"Home {version} · {runtime} · {files.Count} verified files · {new FileInfo(output).Length} bytes");
Console.WriteLine("Public key fingerprint: " + Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant());
return 0;

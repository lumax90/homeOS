using System.Security.Cryptography;
using HomeServer.Core;
using HomeServer.Downloads;

namespace HomeServer.Infrastructure;

public static class VerifiedFilePublication
{
    public static string FileName(string value)
    {
        _ = DownloadFiles.ValidateRelative(value);
        if (value != Path.GetFileName(value) || value.Contains('/') || value.Contains('\\')) throw new DomainException("Yayın dosyası tek bir dosya adı olmalı.", 409);
        return value;
    }
    public static async Task<string> HashAsync(string path, CancellationToken token)
    {
        AppFiles.RejectLink(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
    }
    public static async Task StageAsync(string source, string root, string fileName, string operation, long length, string hash, CancellationToken token)
    {
        _ = FileName(fileName); _ = AppFiles.Id(operation); AppFiles.RejectLink(root);
        var target = Path.Combine(root, fileName);
        if (File.Exists(target)) { Require(target, length, hash); return; }
        var temporary = Path.Combine(root, ".home-publish-" + operation + ".partial");
        if (File.Exists(temporary))
        {
            AppFiles.RejectLink(temporary);
            if (new FileInfo(temporary).Length == length && await HashAsync(temporary, token) == hash) return;
            File.Delete(temporary);
        }
        AppFiles.RejectLink(source);
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous))
        await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
        { await input.CopyToAsync(output, token); await output.FlushAsync(token); output.Flush(true); }
        if (new FileInfo(temporary).Length != length || await HashAsync(temporary, token) != hash) throw new DomainException("Yayın kopyasının boyut/hash doğrulaması başarısız.", 422);
    }
    public static string Promote(string root, string fileName, string operation, long length, string hash)
    {
        _ = FileName(fileName); _ = AppFiles.Id(operation); AppFiles.RejectLink(root);
        var target = Path.Combine(root, fileName); var temporary = Path.Combine(root, ".home-publish-" + operation + ".partial");
        if (File.Exists(target)) { Require(target, length, hash); return target; }
        Require(temporary, length, hash);
        File.Move(temporary, target, false); // Both names are in the target directory, including cross-volume imports.
        return target;
    }
    public static void Require(string path, long length, string hash)
    {
        if (!File.Exists(path)) throw new DomainException("Yayın dosyası bulunamadı; alınan parçalar korundu.", 409);
        AppFiles.RejectLink(path);
        if (new FileInfo(path).Length != length || StateFolders.Hash(path) != hash)
            throw new DomainException("Yayın hedefi farklı veri içeriyor; çakışan dosya korundu.", 409);
    }
}

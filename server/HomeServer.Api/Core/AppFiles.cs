namespace HomeServer.Core;

public static class AppFiles
{
    public static string DirectoryIn(string root, params string[] segments)
    {
        Directory.CreateDirectory(root);
        RejectLink(root);
        var current = Path.GetFullPath(root);
        foreach (var segment in segments)
        {
            if (segment.Length is < 1 or > 100 || segment.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
                throw new DomainException("Geçersiz uygulama dizini.");
            current = Path.Combine(current, segment);
            Directory.CreateDirectory(current);
            RejectLink(current);
        }
        return current;
    }

    public static string Id(string id) => Guid.TryParseExact(id, "N", out _) ? id : throw new DomainException("Geçersiz işlem kimliği.", 404);
    public static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new DomainException("Uygulama alanında dosya sistemi bağlantısı kullanılamaz.", 403);
    }
    public static void DeleteOwnedDirectory(string path)
    {
        RejectLink(path);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            RejectLink(entry);
            if (Directory.Exists(entry)) DeleteOwnedDirectory(entry);
            else File.Delete(entry);
        }
        Directory.Delete(path);
    }
}

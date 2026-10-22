using HomeServer.Core;

namespace HomeServer.Scanning;

public static class LibraryTraversal
{
    public static bool Excluded(string relative, LibraryOptions options)
    {
        var path = relative.Replace('\\', '/');
        return (options.ExcludedPaths ?? []).Any(value =>
        {
            var excluded = value.Replace('\\', '/').TrimEnd('/');
            return path.Equals(excluded, StringComparison.OrdinalIgnoreCase) || path.StartsWith(excluded + "/", StringComparison.OrdinalIgnoreCase);
        });
    }
    public static IEnumerable<string> Files(string root, LibraryOptions options, CancellationToken token, string[]? ignoredRoots = null)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                if (Ignored(entry, ignoredRoots ?? [])) continue;
                if (Excluded(Path.GetRelativePath(root, entry), options)) continue;
                var attributes = File.GetAttributes(entry);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry); else yield return entry;
            }
        }
    }
    public static bool Ignored(string path, string[] roots) => roots.Any(root => MediaPaths.Key(path) == MediaPaths.Key(root)
        || MediaPaths.Key(path).StartsWith(MediaPaths.Key(Path.TrimEndingDirectorySeparator(root)) + Path.DirectorySeparatorChar, StringComparison.Ordinal));
}

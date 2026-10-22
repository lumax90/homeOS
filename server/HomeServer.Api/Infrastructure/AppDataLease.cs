namespace HomeServer.Infrastructure;

public sealed class AppDataLease : IDisposable
{
    private readonly FileStream _lease;
    public AppDataLease(string directory)
    {
        Directory.CreateDirectory(directory);
        try
        {
            _lease = new FileStream(Path.Combine(directory, "home.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("Bu veri klasörü başka bir Home sunucusu tarafından kullanılıyor.", ex);
        }
    }
    public void Dispose() => _lease.Dispose();
}

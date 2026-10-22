namespace HomeServer.Core;

public sealed record UsenetServerInput(string Name, string Host, int Port = 563, string Security = "tls", string Username = "",
    string? Password = null, int Connections = 4, int Priority = 1, bool Enabled = true);
public sealed record UsenetServer(string Id, string Name, string Host, int Port, string Security, string Username, bool Configured,
    int Connections, int Priority, bool Enabled, int Revision, string? LastTest, string? LastError, string? RetryAt, long DownloadedBytes);
public sealed record UsenetServerSecret(UsenetServer Server, string ProtectedPassword);
public sealed record NzbSegment(int Number, long Bytes, string MessageId);
public sealed record NzbFile(int Index, string Name, string[] Groups, NzbSegment[] Segments);
public sealed record NzbManifest(string Name, string Fingerprint, string? Password, NzbFile[] Files)
{
    public long EstimatedBytes => Files.Sum(file => file.Segments.Sum(segment => segment.Bytes));
}
public sealed record YencPart(string Name, long FileSize, long Offset, int Number, byte[] Bytes, uint Crc, uint? FileCrc);
public sealed record PartReceipt(string Name, long FileSize, long Offset, int Number, int Length, uint Crc, uint? FileCrc, string Sha256);
public sealed record ArchivePassword(string Password);

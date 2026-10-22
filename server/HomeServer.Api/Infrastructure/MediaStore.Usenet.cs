using HomeServer.Core;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public IReadOnlyList<UsenetServerSecret> GetUsenetServers()
    {
        using var db = Open();
        return Query(db, "SELECT id,name,host,port,security,username,secret,connections,priority,enabled,revision,last_test,last_error,retry_at,downloaded_bytes FROM usenet_servers ORDER BY priority,name",
            r => new UsenetServerSecret(new UsenetServer(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetString(4), r.GetString(5),
                r.GetString(6).Length > 0, r.GetInt32(7), r.GetInt32(8), r.GetBoolean(9), r.GetInt32(10), Text(r, 11), Text(r, 12), Text(r, 13), r.GetInt64(14)), r.GetString(6)));
    }
    public UsenetServer SaveUsenetServer(string? id, UsenetServerInput input, string protectedPassword)
    {
        lock (_writeGate)
        {
            id ??= Guid.NewGuid().ToString("N"); using var db = Open();
            Execute(db, """
                INSERT INTO usenet_servers(id,name,host,port,security,username,secret,connections,priority,enabled)
                VALUES($id,$name,$host,$port,$security,$user,$secret,$connections,$priority,$enabled)
                ON CONFLICT(id) DO UPDATE SET name=$name,host=$host,port=$port,security=$security,username=$user,secret=$secret,
                  connections=$connections,priority=$priority,enabled=$enabled,revision=revision+1,last_error=NULL,retry_at=NULL
                """, ("$id", id), ("$name", input.Name.Trim()), ("$host", input.Host.Trim()), ("$port", input.Port), ("$security", input.Security), ("$user", input.Username),
                ("$secret", protectedPassword), ("$connections", input.Connections), ("$priority", input.Priority), ("$enabled", input.Enabled));
            return GetUsenetServers().Single(server => server.Server.Id == id).Server;
        }
    }
    public void SetUsenetHealth(string id, int revision, string? error, int retrySeconds = 0, long bytes = 0)
    {
        lock (_writeGate)
        {
            using var db = Open(); Execute(db, "UPDATE usenet_servers SET last_test=$now,last_error=$error,retry_at=$retry,downloaded_bytes=downloaded_bytes+$bytes WHERE id=$id AND revision=$revision",
                ("$id", id), ("$revision", revision), ("$error", error), ("$now", Now()), ("$retry", retrySeconds > 0 ? DateTimeOffset.UtcNow.AddSeconds(retrySeconds).ToString("O") : null), ("$bytes", bytes));
        }
    }
    public void SetDownloadPassword(string id, string protectedPassword)
    {
        lock (_writeGate)
        {
            if (GetDownload(id)?.SourceKind != "nzb") throw new DomainException("NZB indirmesi bulunamadı.", 404);
            using var db = Open(); Execute(db, "INSERT INTO download_passwords(download_id,secret) VALUES($id,$secret) ON CONFLICT(download_id) DO UPDATE SET secret=$secret", ("$id", id), ("$secret", protectedPassword));
        }
    }
    public string? GetDownloadPassword(string id)
    {
        using var db = Open(); return Scalar(db, "SELECT secret FROM download_passwords WHERE download_id=$id", ("$id", id)) as string;
    }
}

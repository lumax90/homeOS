using HomeServer.Core;
using HomeServer.Usenet;

namespace HomeServer.Downloads;

public sealed class TransferSessions(TorrentEngine torrents, NzbEngine nzb) : ITorrentSessions
{
    public Task<ITorrentSession> OpenAsync(DownloadSource source, CancellationToken token) => source.Record.SourceKind == "nzb"
        ? nzb.OpenAsync(source, token) : torrents.OpenAsync(source, token);
}

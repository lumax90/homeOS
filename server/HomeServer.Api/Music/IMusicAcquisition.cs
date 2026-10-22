using HomeServer.Core;

namespace HomeServer.Music;

public interface IMusicAcquisition
{
    Task<MusicSearchResult> SearchMusicAsync(MusicSearchInput input, MusicBrainzProvider metadata, CancellationToken token);
    Task<DownloadRecord> GrabMusicAsync(string id, CancellationToken token, MusicAutomationLease? automation = null);
}

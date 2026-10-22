using HomeServer.Core;

namespace HomeServer.Sources;

public interface IReleaseAcquisition
{
    Task<ReleaseSearchResult> SearchAsync(ReleaseSearchInput input, CancellationToken token, QualityProfile? profileOverride = null);
    Task<DownloadRecord> GrabAsync(string id, bool allowRejected, CancellationToken token, AutomationLease? automation = null);
}

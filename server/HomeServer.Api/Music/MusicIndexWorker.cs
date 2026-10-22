using HomeServer.Infrastructure;
using HomeServer.Playback;

namespace HomeServer.Music;

public sealed class MusicIndexWorker(MediaStore store, MediaTools tools, MediaAnalysisService analysis, EventHub events) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var id = tools.FfprobeAvailable ? store.NextMusicIndex() : null;
            if (id is null) { try { await Task.Delay(5000, stoppingToken); } catch (OperationCanceledException) { break; } continue; }
            try
            {
                var result = await analysis.InspectAsync(id, stoppingToken);
                if (result.Probe.Music is not null) store.SaveMusicTags(id, result.Probe.Music); else store.FailMusicIndex(id);
                events.Publish("music.changed");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { store.FailMusicIndex(id); }
        }
    }
}

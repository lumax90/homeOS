namespace HomeServer.Core;

public sealed record SubtitleCue(double Start, double End, string[] Lines, string Settings = "");
public sealed record VoiceActivity(float[] Probabilities, double StepSeconds, double DurationSeconds);
public sealed record SubtitleWarpPoint(double SourceSeconds,double TargetSeconds,double Score);
public sealed record SubtitleAlignment(double OffsetSeconds, double TimeScale, double Score, double AlternativeScore, double VoiceSeconds, int Cues, bool CanApply, string Reason,SubtitleWarpPoint[]? Points=null);
public sealed record SubtitleSyncInput(string TrackId, int? AudioStream = null, int MaximumOffsetSeconds = 30, bool CorrectFrameRate = false, bool AutoApply = false,bool Segmented=false);
public sealed record SubtitleSyncJob(string Id, string MediaId, string TrackId, string Fingerprint, string SubtitleFingerprint, int MatchRevision,
    int AudioStream, int Revision, string Status, int Progress, string Message, string CreatedAt, string UpdatedAt, SubtitleSyncInput Input, SubtitleAlignment? Result, string? SubtitleId);
public sealed record SubtitleSyncCommand(int Revision);
public sealed record VoiceCapabilities(bool Available, string Model, string Runtime, string? Error = null);

namespace HomeServer.Core;
public sealed record OperationMetric(string At,double CpuPercent,long WorkingSetBytes,int Threads,long? DiskFreeBytes,long? DiskTotalBytes,int Downloads,int Streams,int Jobs,long DatabaseBytes);
public sealed record MaintenancePreferences(bool Enabled=false,int IntervalHours=24,int CacheDays=7);
public sealed record MaintenanceInput(string Action);
public sealed record ConfigurationBundle(int FormatVersion,Dictionary<string,string> Settings,QualityProfile[] Profiles);
public sealed record ConfigurationApply(string CommandId,string ExpectedCurrentHash,ConfigurationBundle Bundle);

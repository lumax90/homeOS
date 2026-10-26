using System.Text.Json;
using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Operations;

public sealed partial class ContainerUpdateCoordinator
{
    private string AvailabilityPath => Path.Combine(paths.Data, "updates", "container", "availability.json");
    public static bool ValidDigest(string? value) => value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal)
        && value[7..].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static void ValidateCandidate(ContainerUpdateCandidate value)
    {
        if (!Version.TryParse(value.Version, out var version) || version.Build < 0 || version.Revision >= 0
            || !ValidDigest(value.Digest) || value.Revision is not { Length: 40 } || !value.Revision.All(char.IsAsciiHexDigit)
            || value.TotalBytes is < 1 or > 20L * 1024 * 1024 * 1024
            || value.EstimatedDownloadBytes is < 0 || value.EstimatedDownloadBytes > value.TotalBytes)
            throw new DomainException("Invalid container release metadata.", 409);
    }
    private bool HelperConnected(ContainerUpdateAvailability value) => DateTimeOffset.TryParse(value.HelperSeenAt, out var seen)
        && seen > clock.GetUtcNow().AddMinutes(-2) && seen <= clock.GetUtcNow().AddMinutes(1);
    public ContainerUpdateAvailability Availability() => ReadAvailability();
    private ContainerUpdateAvailability ReadAvailability()
    {
        if (!File.Exists(AvailabilityPath)) return new();
        AppFiles.RejectLink(AvailabilityPath);
        if (new FileInfo(AvailabilityPath).Length > 16 * 1024) throw new DomainException("Update availability exceeds its bound.", 409);
        var value = JsonSerializer.Deserialize<ContainerUpdateAvailability>(File.ReadAllText(AvailabilityPath), Json)
            ?? throw new DomainException("Update availability requires review.", 409);
        if (value.Format != 1 || value.Phase is not ("idle" or "available" or "approved" or "downloading" or "waiting" or "ready" or "applying" or "updated" or "failed"))
            throw new DomainException("Update availability requires review.", 409);
        if (value.Candidate is { } candidate) ValidateCandidate(candidate);
        if (value.ApprovedDigest is { } approved && (!ValidDigest(approved) || approved != value.Candidate?.Digest))
            throw new DomainException("Update approval requires review.", 409);
        if (value.DismissedDigest is not null && !ValidDigest(value.DismissedDigest))
            throw new DomainException("Update dismissal requires review.", 409);
        return value;
    }
    private void WriteAvailability(ContainerUpdateAvailability value)
    {
        _ = AppFiles.DirectoryIn(paths.Data, "updates", "container");
        if (File.Exists(AvailabilityPath)) AppFiles.RejectLink(AvailabilityPath);
        PackageTrust.AtomicJson(AvailabilityPath, value);
    }
    public async Task ReportCandidateAsync(ContainerUpdateCandidate? candidate, string? error, CancellationToken token)
    {
        if (candidate is not null) ValidateCandidate(candidate);
        if (candidate is not null && Version.TryParse(BuildInfo.Version, out var current) && Version.Parse(candidate.Version) < current)
            throw new DomainException("Container downgrades require explicit backup recovery.", 409);
        await _gate.WaitAsync(token);
        try
        {
            var value = ReadAvailability();
            // A moving stable channel must never change the owner's approved target.
            if (value.ApprovedDigest is not null || value.Phase == "applying") return;
            WriteAvailability(value with { Candidate = error is null ? candidate : value.Candidate,
                CheckedAt = clock.GetUtcNow().ToString("O"), Error = error,
                Phase = error is not null ? value.Phase : candidate is null ? "idle" : "available" });
        }
        finally { _gate.Release(); }
    }
    public async Task ApproveAsync(string digest, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var value = ReadAvailability();
            if (!Enabled || !HelperConnected(value)) throw new DomainException("The update helper is not connected or update checks are paused.", 409);
            if (!ValidDigest(digest) || value.Candidate?.Digest != digest) throw new DomainException("This release changed. Check updates again before approving.", 409);
            if (_lease is not null || value.Phase == "applying") throw new DomainException("An update is already being applied.", 409);
            if (value.ApprovedDigest == digest) return;
            WriteAvailability(value with { ApprovedDigest = digest, DismissedDigest = null, Phase = "approved", Error = null });
        }
        finally { _gate.Release(); }
    }
    public async Task CancelApprovalAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var value = ReadAvailability();
            if (_lease is not null || value.Phase == "applying") throw new DomainException("Installation has started; do not interrupt it.", 409);
            WriteAvailability(value with { ApprovedDigest = null, DismissedDigest = value.Candidate?.Digest,
                Phase = value.Candidate is null ? "idle" : "available", Error = null });
        }
        finally { _gate.Release(); }
    }
    public async Task<object> AgentAsync(ContainerUpdateAgentCommand command, CancellationToken token)
    {
        if (command.Protocol != 1) throw new DomainException("Unsupported update helper protocol.", 409);
        await _gate.WaitAsync(token);
        try
        {
            var value = ReadAvailability() with { HelperSeenAt = clock.GetUtcNow().ToString("O") };
            if (command.Action == "status")
            {
                if (command.Digest is not null)
                {
                    if (!ValidDigest(command.Digest)) throw new DomainException("Invalid installed image digest.", 409);
                    value = value with { InstalledDigest = command.Digest };
                }
                if (AutomaticInstall && value.Candidate is not null && value.ApprovedDigest is null
                    && value.DismissedDigest != value.Candidate.Digest && value.Phase == "available")
                    value = value with { ApprovedDigest = value.Candidate.Digest, Phase = "approved" };
            }
            else
            {
                if (!Enabled || command.Digest is null || command.Digest != value.ApprovedDigest)
                    throw new DomainException("No matching release approval is active.", 409);
                if (command.Action == "downloading" && value.Phase is "approved" or "downloading" or "waiting" or "ready")
                    value = value with { Phase = "downloading", Error = null };
                else if (command.Action == "ready" && value.Phase is "downloading" or "waiting" or "ready")
                    value = value with { Phase = Activity().Idle ? "ready" : "waiting" };
                else if (command.Action == "failed" && value.Phase != "applying")
                    value = value with { Phase = "failed", ApprovedDigest = null, Error = "The update did not complete. Existing data and application backups are retained." };
                else throw new DomainException("Invalid update helper transition.", 409);
            }
            WriteAvailability(value);
            return new { protocol = 1, enabled = Enabled, automaticInstall = AutomaticInstall, availability = value };
        }
        finally { _gate.Release(); }
    }
}

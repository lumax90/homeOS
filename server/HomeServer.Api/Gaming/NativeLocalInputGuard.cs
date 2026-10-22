using System.Runtime.InteropServices;

namespace HomeServer.Gaming;

public sealed record NativeInputFirewallRule(bool Enabled, int Direction, int Action, int Protocol, int Profiles,
    string Application, string Service, string LocalPorts, string LocalAddresses, string RemoteAddresses);
public sealed record NativeInputFirewallSnapshot(bool[] EnabledProfiles, int[] DefaultInboundActions, bool HasExcludedInterfaces, NativeInputFirewallRule[] Rules);

/// <summary>Read-only Windows firewall precondition for RetroArch's otherwise unauthenticated UDP listener.</summary>
public static class NativeLocalInputGuard
{
    public static bool IsRecentSafeResult(bool safe, long checkedTicks, long nowTicks)
        => safe && nowTicks >= checkedTicks && nowTicks - checkedTicks < TimeSpan.FromSeconds(5).Ticks;
    public static bool IsPrivate(NativeInputFirewallSnapshot snapshot, string executable, int port)
    {
        if (port is < 1024 or > 65535 || snapshot.EnabledProfiles.Length != 3 || snapshot.EnabledProfiles.Any(enabled => !enabled)
            || snapshot.DefaultInboundActions.Length != 3 || snapshot.DefaultInboundActions.Any(action => action != 0) || snapshot.HasExcludedInterfaces) return false;
        var file = Path.GetFullPath(executable);
        if (snapshot.Rules.Any(rule => rule.Enabled && rule.Direction == 1 && rule.Action == 0 && rule.Protocol == 17 && (rule.Profiles & 7) == 7
            && string.IsNullOrEmpty(rule.Service) && !string.IsNullOrEmpty(rule.Application) && rule.Application != "*" && MatchesApplication(rule.Application, file)
            && MatchesPort(rule.LocalPorts, port) && (rule.LocalAddresses is "*" or "") && CoversExternalIpv4(rule.RemoteAddresses))) return true;
        return !snapshot.Rules.Any(rule => rule.Enabled && rule.Direction == 1 && rule.Action == 1 && (rule.Profiles & 7) != 0
            && rule.Protocol is 17 or 256 && string.IsNullOrEmpty(rule.Service) && MatchesApplication(rule.Application, file)
            && MatchesPort(rule.LocalPorts, port) && !OnlyLoopback(rule.LocalAddresses) && !OnlyLoopback(rule.RemoteAddresses));
    }
    public static bool Inspect(string executable, int port)
        => InspectPorts(executable, [port]);
    public static bool InspectPorts(string executable, int[] ports)
    {
        if (!OperatingSystem.IsWindows() || ports.Length is < 1 or > 16) return false;
        object? instance = null;
        try
        {
            if (!ServiceRunning("MpsSvc") || !ServiceRunning("BFE")) return false;
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2"); if (type is null) return false;
            instance = Activator.CreateInstance(type); if (instance is null) return false; dynamic policy = instance;
            var enabled = new List<bool>(); var inbound = new List<int>(); var excluded = false;
            foreach (var profile in new[] { 1, 2, 4 })
            {
                enabled.Add((bool)policy.FirewallEnabled[profile]); inbound.Add((int)policy.DefaultInboundAction[profile]);
                var interfaces = policy.ExcludedInterfaces[profile]; if (interfaces is Array values && values.Length > 0) excluded = true;
            }
            var rules = new List<NativeInputFirewallRule>();
            foreach (dynamic rule in policy.Rules)
            {
                try
                {
                    if (!(bool)rule.Enabled || (int)rule.Direction != 1 || ((int)rule.Action is not (0 or 1)) || ((int)rule.Protocol is not (17 or 256))) continue;
                    rules.Add(new(true, 1, (int)rule.Action, (int)rule.Protocol, (int)rule.Profiles, (string?)rule.ApplicationName ?? "", (string?)rule.ServiceName ?? "",
                        (int)rule.Protocol == 256 ? "*" : (string?)rule.LocalPorts ?? "", (string?)rule.LocalAddresses ?? "", (string?)rule.RemoteAddresses ?? ""));
                }
                finally { if (Marshal.IsComObject(rule)) Marshal.ReleaseComObject(rule); }
            }
            var snapshot = new NativeInputFirewallSnapshot(enabled.ToArray(), inbound.ToArray(), excluded, rules.ToArray());
            return ports.All(port => IsPrivate(snapshot, executable, port));
        }
        catch (Exception error) when (error is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { return false; }
        finally { if (instance is not null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance); }
    }
    private static bool MatchesApplication(string application, string executable)
    {
        if (string.IsNullOrEmpty(application) || application == "*") return true;
        try { return string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(application)), executable, StringComparison.OrdinalIgnoreCase); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException) { return true; }
    }
    private static bool ServiceRunning(string name)
    {
        var manager = OpenSCManager(null, null, 1); if (manager == IntPtr.Zero) return false;
        try
        {
            var service = OpenService(manager, name, 4); if (service == IntPtr.Zero) return false;
            try { return QueryServiceStatus(service, out var status) && status.State == 4; }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus { public uint Type, State, Accepted, Win32Exit, ServiceExit, CheckPoint, WaitHint; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr OpenService(IntPtr manager, string name, uint access);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(IntPtr handle);
    private static bool OnlyLoopback(string addresses)
    {
        if (string.IsNullOrWhiteSpace(addresses)) return false;
        return addresses.Split(',').All(address => address.Trim() is "127.0.0.1" or "127.0.0.1/255.255.255.255" or "::1");
    }
    private static bool CoversExternalIpv4(string addresses)
    {
        var values = addresses.Split(',').Select(value => value.Trim()).ToArray();
        return values.Contains("0.0.0.0-126.255.255.255", StringComparer.Ordinal) && values.Contains("128.0.0.0-255.255.255.255", StringComparer.Ordinal);
    }
    private static bool MatchesPort(string ports, int port)
    {
        if (string.IsNullOrWhiteSpace(ports) || ports == "*") return true;
        foreach (var value in ports.Split(','))
        {
            var part = value.Trim();
            if (int.TryParse(part, out var single)) { if (single == port) return true; continue; }
            var range = part.Split('-');
            if (range.Length != 2 || !int.TryParse(range[0], out var lower) || !int.TryParse(range[1], out var upper)) return true;
            if (lower <= port && port <= upper) return true;
        }
        return false;
    }
}

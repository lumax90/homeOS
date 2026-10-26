using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using HomeServer.Core;

namespace HomeServer.Operations;

// Reads bounded registry metadata only: no image-layer or internet-media payloads.
public sealed class ContainerReleaseSource(HttpClient client)
{
    public const string Repository = "lumax90/homeos";
    private const string Accept = "application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json";
    private async Task<byte[]> ReadAsync(string path, string? bearer, string? digest, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://ghcr.io/" + path);
        request.Headers.TryAddWithoutValidation("Accept", Accept);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 1024 * 1024) throw new DomainException("Registry metadata exceeds its bound.", 409);
        await using var stream = await response.Content.ReadAsStreamAsync(token); using var output = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > 1024 * 1024) throw new DomainException("Registry metadata exceeds its bound.", 409);
            output.Write(buffer, 0, count);
        }
        var bytes = output.ToArray();
        digest ??= response.Headers.TryGetValues("Docker-Content-Digest", out var values) ? values.SingleOrDefault() : null;
        if (digest is not null && (!ContainerUpdateCoordinator.ValidDigest(digest) || digest != "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes))))
            throw new DomainException("Registry metadata digest does not match.", 409);
        return bytes;
    }
    private async Task<(JsonDocument Manifest, string Digest)> ManifestAsync(string reference, string bearer, CancellationToken token, int depth = 0)
    {
        if (depth > 1) throw new DomainException("Nested release platform indexes are not supported.", 409);
        var bytes = await ReadAsync($"v2/{Repository}/manifests/{reference}", bearer,
            ContainerUpdateCoordinator.ValidDigest(reference) ? reference : null, token);
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        var document = JsonDocument.Parse(bytes);
        if (document.RootElement.TryGetProperty("manifests", out var platforms))
        {
            var selected = platforms.EnumerateArray().Where(item => item.TryGetProperty("platform", out var platform)
                && platform.GetProperty("os").GetString() == "linux" && platform.GetProperty("architecture").GetString() == "amd64").ToArray();
            if (selected.Length != 1) { document.Dispose(); throw new DomainException("No unique supported container platform.", 409); }
            var child = selected[0].GetProperty("digest").GetString(); document.Dispose();
            if (!ContainerUpdateCoordinator.ValidDigest(child)) throw new DomainException("Invalid platform digest.", 409);
            return await ManifestAsync(child!, bearer, token, depth + 1);
        }
        return (document, digest);
    }
    public async Task<ContainerUpdateCandidate?> CheckAsync(string currentVersion, string? revision, string? installedDigest, CancellationToken token)
    {
        using var authorization = JsonDocument.Parse(await ReadAsync($"token?service=ghcr.io&scope=repository:{Repository}:pull", null, null, token));
        var bearer = authorization.RootElement.GetProperty("token").GetString() ?? throw new DomainException("Registry authentication is unavailable.", 409);
        var (manifest, digest) = await ManifestAsync("stable", bearer, token);
        using (manifest)
        {
            var configDigest = manifest.RootElement.GetProperty("config").GetProperty("digest").GetString();
            if (!ContainerUpdateCoordinator.ValidDigest(configDigest)) throw new DomainException("Invalid image configuration digest.", 409);
            using var config = JsonDocument.Parse(await ReadAsync($"v2/{Repository}/blobs/{configDigest}", bearer, configDigest, token));
            var root = config.RootElement;
            if (root.GetProperty("os").GetString() != "linux" || root.GetProperty("architecture").GetString() != "amd64")
                throw new DomainException("Unsupported image platform.", 409);
            var labels = root.GetProperty("config").GetProperty("Labels");
            if (labels.GetProperty("org.opencontainers.image.source").GetString() != "https://github.com/lumax90/homeOS")
                throw new DomainException("Unexpected release source.", 409);
            var version = labels.GetProperty("org.opencontainers.image.version").GetString() ?? "";
            var source = labels.GetProperty("org.opencontainers.image.revision").GetString() ?? "";
            var layers = manifest.RootElement.GetProperty("layers").EnumerateArray().ToArray();
            if (layers.Length is < 1 or > 128 || layers.Any(x => !ContainerUpdateCoordinator.ValidDigest(x.GetProperty("digest").GetString()) || x.GetProperty("size").GetInt64() < 0))
                throw new DomainException("Invalid release layers.", 409);
            var total = layers.Sum(x => x.GetProperty("size").GetInt64());
            var candidate = new ContainerUpdateCandidate(version, source, digest, total, null);
            ContainerUpdateCoordinator.ValidateCandidate(candidate);
            if (!Version.TryParse(currentVersion, out var current) || Version.Parse(version) < current)
                throw new DomainException("Release downgrade refused.", 409);
            if (version == currentVersion && source == revision) return null;
            if (ContainerUpdateCoordinator.ValidDigest(installedDigest))
            {
                try
                {
                    var (old, _) = await ManifestAsync(installedDigest!, bearer, token);
                    using (old)
                    {
                        var cached = old.RootElement.GetProperty("layers").EnumerateArray().Select(x => x.GetProperty("digest").GetString()).ToHashSet();
                        candidate = candidate with { EstimatedDownloadBytes = layers.Where(x => !cached.Contains(x.GetProperty("digest").GetString())).Sum(x => x.GetProperty("size").GetInt64()) };
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or DomainException or KeyNotFoundException)
                { /* An unavailable old manifest only removes the optional download estimate. */ }
            }
            return candidate;
        }
    }
}

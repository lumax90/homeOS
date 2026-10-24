using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HomeServer.Core;

namespace HomeServer.Playback;

/// <summary>FFmpeg owns muxing. This window owns published segment lifetimes and bounded producer backpressure.</summary>
public sealed partial class PlaybackWindow(string directory, long budget, double start, double requestedStart, TimeProvider? time = null, double aheadSeconds = 90)
{
    private sealed record Segment(int Index, string Name, double Start, double Duration)
    {
        public double End => Start + Duration;
        public DateTimeOffset? RemovedAt;
        public bool Deleted;
    }
    private readonly object _gate = new();
    private readonly TimeProvider _clock = time ?? TimeProvider.System;
    private readonly SortedDictionary<int, Segment> _segments = [];
    private readonly double _ahead = Math.Clamp(aheadSeconds, 12, 120);
    private readonly double _requested = Math.Max(0, requestedStart - start);
    private double _position = Math.Max(0, requestedStart - start), _end, _produced, _target = 4, _maximumPlaylistDuration;
    private double? _speed;
    private int _last = -1, _first, _internalFirst;
    private bool _complete, _mp4, _observedProgress;
    private long _bytes;
    private long _playlistStamp = -1;
    private long _playlistLength = -1;
    public long Budget => budget;
    public bool Ready { get { lock (_gate) { Refresh(); return _end >= _requested + 8 || _complete && _segments.Count > 0; } } }

    public void Touch(double position)
    {
        lock (_gate) { _position = Math.Max(0, position - start); if (_position > _requested + 1) _observedProgress = true; }
    }
    public void Progress(double? seconds, double? speed)
    {
        lock (_gate)
        {
            if (seconds is >= 0 && double.IsFinite(seconds.Value)) _produced = Math.Max(_produced, seconds.Value);
            if (speed is > 0 && double.IsFinite(speed.Value)) _speed = speed;
        }
    }
    public bool Control(bool paused)
    {
        lock (_gate)
        {
            Refresh(); Rotate(); DeleteExpired();
            _bytes = Directory.EnumerateFiles(directory).Sum(path => { try { return new FileInfo(path).Length; } catch (FileNotFoundException) { return 0; } });
            if (_bytes > budget) throw new DomainException("Akışın geçici parçaları ayrılan alanı aşıyor. Oynatma cache sınırını artır veya daha düşük kalite seç.", 409);
            if (_complete || _end < _requested + 8) return false;
            var ahead = Math.Max(_produced, _end) - _position;
            return paused ? ahead > Math.Max(4, _ahead - _target) || _bytes > budget * .6 : ahead >= _ahead || _bytes > budget * .75;
        }
    }
    public Stream Playlist()
    {
        lock (_gate)
        {
            Refresh(); Rotate();
            var segments = _segments.Values.Where(segment => segment.Index >= _first).ToArray();
            if (segments.Length == 0) throw new DomainException("Oynatma parçaları hazırlanıyor.", 503);
            _maximumPlaylistDuration = Math.Max(_maximumPlaylistDuration, segments.Sum(segment => segment.Duration));
            var result = new StringBuilder($"#EXTM3U\n#EXT-X-VERSION:{(_mp4 ? 7 : 3)}\n#EXT-X-TARGETDURATION:{Math.Ceiling(_target)}\n#EXT-X-MEDIA-SEQUENCE:{segments[0].Index}\n#EXT-X-INDEPENDENT-SEGMENTS\n");
            if (_mp4) result.Append("#EXT-X-MAP:URI=\"init.mp4\"\n");
            foreach (var segment in segments) result.Append("#EXTINF:").Append(segment.Duration.ToString("0.######", CultureInfo.InvariantCulture)).Append(",\n").Append(segment.Name).Append('\n');
            if (_complete) result.Append("#EXT-X-ENDLIST\n");
            return new MemoryStream(Encoding.UTF8.GetBytes(result.ToString()), writable: false);
        }
    }
    public void RequireArtifact(string name)
    {
        lock (_gate)
        {
            Refresh();
            if (name == "init.mp4" && _mp4) return;
            if (!SegmentName().IsMatch(name)) throw new DomainException("Oynatma parçası bulunamadı.", 404);
            if (_segments.Values.Any(segment => segment.Name == name && !segment.Deleted)) return;
            var index = int.Parse(name.AsSpan(7, 6), CultureInfo.InvariantCulture);
            throw new DomainException(index <= _last ? "Bu bölüm geçici tamponun dışında. Kaldığın yerden yeniden oynat." : "Oynatma parçası henüz hazır değil.", index <= _last ? 410 : 404);
        }
    }
    public PlaybackTelemetry Telemetry(bool throttled)
    {
        lock (_gate)
        {
            var first = _segments.Values.FirstOrDefault(segment => segment.Index >= _first);
            return new(_speed, start + Math.Max(_produced, _end), start + (first?.Start ?? 0), start + _end, _bytes, throttled);
        }
    }
    private void Refresh()
    {
        var path = Path.Combine(directory, "index.m3u8");
        var info = new FileInfo(path); if (!info.Exists) return;
        if (info.Length > 1024 * 1024) throw new DomainException("Oynatma listesi izin verilen sınırı aşıyor.", 422);
        if (info.LastWriteTimeUtc.Ticks == _playlistStamp && info.Length == _playlistLength) return;
        string text;
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(file); text = reader.ReadToEnd();
        }
        catch (IOException) { return; } // FFmpeg atomically publishes its next list.
        var lines = text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        if (lines.FirstOrDefault() != "#EXTM3U") throw new DomainException("Oynatma listesi geçersiz.", 422);
        var sequenceLine = lines.FirstOrDefault(line => line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.Ordinal));
        var index = 0;
        if (sequenceLine is not null && (!int.TryParse(sequenceLine.AsSpan(22), CultureInfo.InvariantCulture, out index) || index < 0))
            throw new DomainException("Oynatma listesi geçersiz.", 422);
        _internalFirst = index;
        var position = index == 0 ? 0 : _segments.TryGetValue(index, out var previous) ? previous.Start : index == _last + 1 ? _end : throw new DomainException("Oynatma zaman çizelgesi kesildi.", 422);
        double? duration = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            {
                if (!double.TryParse(line[8..].Split(',')[0], CultureInfo.InvariantCulture, out var parsed)) throw new DomainException("Oynatma listesi geçersiz.", 422);
                duration = parsed;
            }
            else if (!line.StartsWith('#'))
            {
                if (!SegmentName().IsMatch(line) || duration is not > 0 || duration > 600 || !double.IsFinite(duration.Value)
                    || int.Parse(line.AsSpan(7, 6), CultureInfo.InvariantCulture) != index) throw new DomainException("Oynatma parçası zaman çizelgesi geçersiz.", 422);
                if (index > _last)
                {
                    if (index != _last + 1) throw new DomainException("Oynatma parçaları sıralı değil.", 422);
                    _segments.Add(index, new(index, line, position, duration.Value)); _last = index; _end = position + duration.Value;
                }
                _target = Math.Max(_target, duration.Value); position += duration.Value; duration = null; index++;
            }
        }
        _mp4 = lines.Contains("#EXT-X-MAP:URI=\"init.mp4\"", StringComparer.Ordinal);
        _complete = lines.Contains("#EXT-X-ENDLIST", StringComparer.Ordinal);
        _playlistStamp = info.LastWriteTimeUtc.Ticks; _playlistLength = info.Length;
    }
    private void Rotate()
    {
        // The first playlist starts at timestamp zero, including after a keyframe-aligned seek.
        // A HEAD/prefetch must not change that origin before the player establishes its timeline.
        if (!_observedProgress) return;
        // Retain a back buffer, and never trim away the playhead or the final three target durations.
        var oldest = Math.Min(_position - Math.Min(30, _ahead / 3), _end - _target * 3);
        foreach (var segment in _segments.Values)
        {
            if (segment.End > oldest) break;
            if (segment.Index >= _first) { segment.RemovedAt = _clock.GetUtcNow(); _first = segment.Index + 1; }
        }
    }
    private void DeleteExpired()
    {
        // A segment removed from a playlist remains downloadable for the old playlist's duration plus itself (RFC 8216 §6.2.2).
        foreach (var segment in _segments.Values.Where(segment => segment.RemovedAt is not null && !segment.Deleted).ToArray())
        {
            if (_clock.GetUtcNow() - segment.RemovedAt < TimeSpan.FromSeconds(_maximumPlaylistDuration + segment.Duration)) continue;
            var path = Path.Combine(directory, segment.Name); if (File.Exists(path)) { AppFiles.RejectLink(path); File.Delete(path); }
            segment.Deleted = true;
        }
        foreach (var index in _segments.Where(pair => pair.Key < _internalFirst && pair.Value.Deleted).Select(pair => pair.Key).ToArray()) _segments.Remove(index);
    }
    [GeneratedRegex(@"^segment[0-9]{6}\.(?:ts|m4s)$", RegexOptions.NonBacktracking)] private static partial Regex SegmentName();
}

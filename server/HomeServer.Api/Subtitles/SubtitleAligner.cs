using System.Numerics;
using HomeServer.Core;

namespace HomeServer.Subtitles;

public static class SubtitleAligner
{
    private const double Step = .1;
    public static SubtitleAlignment Align(VoiceActivity voice, IReadOnlyList<SubtitleCue> cues, int maximumOffsetSeconds, bool correctFrameRate, CancellationToken token)
    {
        if (maximumOffsetSeconds is < 1 or > 120 || voice.DurationSeconds is <= 0 or > 14400 || !double.IsFinite(voice.DurationSeconds) || voice.StepSeconds <= 0 || !double.IsFinite(voice.StepSeconds)) throw new DomainException("Senkron aralığı geçersiz.");
        var count = (int)Math.Ceiling(voice.DurationSeconds / Step); var margin = maximumOffsetSeconds * 10;
        var spoken = new ulong[(count + 63) / 64];
        for (var i = 0; i < voice.Probabilities.Length; i++)
        {
            if (voice.Probabilities[i] < .5f) continue;
            var cell = (int)(i * voice.StepSeconds / Step); if (cell < count) spoken[cell / 64] |= 1UL << (cell % 64);
        }
        var voiceCount = spoken.Sum(word => BitOperations.PopCount(word)); var voiceSeconds = voiceCount * Step;
        if (voiceSeconds < 2.5 || cues.Count < 3) return new(0, 1, 0, 0, voiceSeconds, cues.Count, false, "En az üç zamanlı cue ve yeterli konuşma etkinliği gerekli.");
        var scales = correctFrameRate ? new[] { 1d, 25d / 24, 24d / 25, 25d / (24000d / 1001), (24000d / 1001) / 25, 1.001, 1 / 1.001 } : [1d];
        var trials = new List<(double Score, int Offset, double Scale)>();
        foreach (var scale in scales)
        {
            var bits = new ulong[(count + margin * 2 + 127) / 64];
            var until = 0;
            foreach (var cue in cues.OrderBy(cue => cue.Start))
            {
                token.ThrowIfCancellationRequested();
                var begin = Math.Clamp((int)Math.Floor(cue.Start * scale / Step) + margin, 0, count + 2 * margin);
                var end = Math.Clamp((int)Math.Ceiling(cue.End * scale / Step) + margin, 0, count + 2 * margin);
                for (var i = Math.Max(begin, until); i < end; i++) bits[i / 64] |= 1UL << (i % 64);
                until = Math.Max(until, end);
            }
            for (var shift = -margin; shift <= margin; shift++)
            {
                token.ThrowIfCancellationRequested(); var intersection = 0; var captions = 0;
                for (var word = 0; word < spoken.Length; word++)
                {
                    var index = word * 64 + margin - shift; var sourceWord = index / 64; var offset = index % 64;
                    var value = bits[sourceWord] >> offset; if (offset != 0) value |= bits[sourceWord + 1] << (64 - offset);
                    if (word == spoken.Length - 1 && count % 64 != 0) value &= (1UL << (count % 64)) - 1;
                    captions += BitOperations.PopCount(value); intersection += BitOperations.PopCount(value & spoken[word]);
                }
                var score = captions + voiceCount == 0 ? 0 : 2d * intersection / (captions + voiceCount);
                trials.Add((score, shift, scale));
            }
        }
        var best = trials.OrderByDescending(trial => trial.Score).ThenBy(trial => Math.Abs(trial.Scale - 1)).ThenBy(trial => Math.Abs(trial.Offset)).First();
        var alternative = trials.Where(trial => Math.Abs(trial.Offset - best.Offset) >= 10 && Math.Abs(trial.Scale - best.Scale) < .00001).Select(trial => trial.Score).DefaultIfEmpty(0).Max();
        var eligible = best.Score >= .50 && best.Score - alternative >= .025 && Math.Abs(best.Offset) < margin;
        var reason = eligible ? "Konuşma ve altyazı zamanları belirgin biçimde eşleşiyor." : Math.Abs(best.Offset) >= margin ? "En iyi eşleşme arama sınırında; daha geniş aralık seç." : "Eşleşme yeterince belirgin değil; otomatik uygulama yapılmadı.";
        return new(Math.Round(best.Offset * Step, 3), best.Scale, Math.Round(best.Score, 4), Math.Round(alternative, 4), Math.Round(voiceSeconds, 2), cues.Count, eligible, reason);
    }
}

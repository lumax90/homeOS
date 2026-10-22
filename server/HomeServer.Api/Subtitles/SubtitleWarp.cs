using System.Text;
using HomeServer.Core;

namespace HomeServer.Subtitles;

public static class SubtitleWarp
{
    public static SubtitleAlignment Align(VoiceActivity voice,SubtitleCue[] cues,int maximumOffset,bool correctRate,CancellationToken token)
    {
        var global=SubtitleAligner.Align(voice,cues,maximumOffset,correctRate,token);var points=new List<SubtitleWarpPoint>();
        for(var start=0d;start<voice.DurationSeconds;start+=300)
        {
            token.ThrowIfCancellationRequested();var begin=Math.Max(0,start-maximumOffset);var end=Math.Min(voice.DurationSeconds,start+300+maximumOffset);
            var selected=cues.Where(cue=>cue.Start*global.TimeScale>=start&&cue.End*global.TimeScale<Math.Min(voice.DurationSeconds,start+300)).ToArray();if(selected.Length<6)continue;
            var first=Math.Clamp((int)(begin/voice.StepSeconds),0,voice.Probabilities.Length);var last=Math.Clamp((int)Math.Ceiling(end/voice.StepSeconds),first,voice.Probabilities.Length);
            var part=new VoiceActivity(voice.Probabilities[first..last],voice.StepSeconds,(last-first)*voice.StepSeconds);
            var local=SubtitleAligner.Align(part,selected.Select(cue=>cue with{Start=cue.Start*global.TimeScale-begin,End=cue.End*global.TimeScale-begin}).ToArray(),maximumOffset,false,token);
            if(!local.CanApply)continue;var source=selected.Average(cue=>(cue.Start+cue.End)/2);points.Add(new(source,source*global.TimeScale+local.OffsetSeconds,local.Score));
        }
        if(points.Count<2)return global with{CanApply=false,Reason="Bölgesel zaman eşleme için en az iki güvenilir konuşma penceresi gerekli."};
        for(var index=1;index<points.Count;index++){var scale=(points[index].TargetSeconds-points[index-1].TargetSeconds)/(points[index].SourceSeconds-points[index-1].SourceSeconds);if(scale is <.5 or >2)return global with{CanApply=false,Reason="Sürüm kesikleri tutarlı bir zaman çizelgesi vermiyor; elle bölüm aralıklarını incele.",Points=points.ToArray()};}
        return global with{Score=points.Average(point=>point.Score),CanApply=true,Reason=$"{points.Count} konuşma penceresinden monoton, bölgesel zaman eşlemesi önerildi.",Points=points.ToArray()};
    }
    public static string Transform(SubtitleCue[] cues,SubtitleAlignment alignment)
    {
        if(alignment.Points is not{Length:>=2}points)return SubtitleTiming.Transform(cues,alignment.OffsetSeconds,alignment.TimeScale);
        if(points.Length>100||points.Any(point=>!double.IsFinite(point.SourceSeconds)||!double.IsFinite(point.TargetSeconds)||point.SourceSeconds<0||point.TargetSeconds<0))throw new DomainException("Bölgesel zaman noktaları geçersiz.");
        double Map(double value)
        {
            if(value<=points[0].SourceSeconds)return points[0].TargetSeconds+(value-points[0].SourceSeconds)*alignment.TimeScale;
            for(var index=1;index<points.Length;index++)if(value<=points[index].SourceSeconds){var left=points[index-1];var right=points[index];if(right.SourceSeconds<=left.SourceSeconds||right.TargetSeconds<=left.TargetSeconds)throw new DomainException("Zaman çizelgesi sıralı değil.");return left.TargetSeconds+(value-left.SourceSeconds)/(right.SourceSeconds-left.SourceSeconds)*(right.TargetSeconds-left.TargetSeconds);}
            return points[^1].TargetSeconds+(value-points[^1].SourceSeconds)*alignment.TimeScale;
        }
        return SubtitleTiming.Transform(cues.Select(cue=>cue with{Start=Math.Max(0,Map(cue.Start)),End=Math.Max(0,Map(cue.End))}).Where(cue=>cue.End>cue.Start),0,1);
    }
}

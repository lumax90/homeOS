using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HomeServer.Core;

namespace HomeServer.Operations;
public sealed record OperationLog(long Id,string At,string Level,string Category,string Message,string? Error);
public sealed partial class OperationJournal(string root):ILoggerProvider
{
 private readonly object _gate=new();private long _sequence=DateTimeOffset.UtcNow.Ticks;public long Dropped{get;private set;}
 public static string Redact(string value)
 {value=Urls().Replace(value,match=>Uri.TryCreate(match.Value,UriKind.Absolute,out var uri)?$"{uri.Scheme}://{uri.Host}/[redacted]":"[url]");value=Tokens().Replace(value,"$1=[redacted]");return value[..Math.Min(value.Length,16000)];}
 [GeneratedRegex(@"https?://[^\s\""'<>]+",RegexOptions.IgnoreCase|RegexOptions.NonBacktracking)]private static partial Regex Urls();
 [GeneratedRegex(@"\b(password|passwd|token|api[_-]?key|secret|authorization|cookie)\b[\s\""']*[:=][\s\""']*[^\r\n,;\s\""}]+",RegexOptions.IgnoreCase|RegexOptions.NonBacktracking)]private static partial Regex Tokens();
 public ILogger CreateLogger(string category)=>new JournalLogger(this,category);
 public void Dispose(){}
 private void Write(string category,LogLevel level,string message,Exception? error)
 {
  if(level<LogLevel.Information)return;lock(_gate)try
  {
   Directory.CreateDirectory(root);var path=Path.Combine(root,"current.jsonl");if(File.Exists(path)){AppFiles.RejectLink(path);if(new FileInfo(path).Length>=5*1024*1024)File.Move(path,Path.Combine(root,$"events-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{_sequence}.jsonl"));}
   var entry=new OperationLog(Interlocked.Increment(ref _sequence),DateTimeOffset.UtcNow.ToString("O"),level.ToString(),category,Redact(message),error is null?null:Redact(error.GetType().Name+": "+error.Message));
   File.AppendAllText(path,JsonSerializer.Serialize(entry)+"\n",new UTF8Encoding(false));foreach(var file in Directory.EnumerateFiles(root,"events-*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).Skip(9)){AppFiles.RejectLink(file);File.Delete(file);}
  }catch(IOException){Dropped++;}catch(UnauthorizedAccessException){Dropped++;}
 }
 public OperationLog[] Read(long after,string? level,string? query,int limit)
 {
  if(query?.Length>120)throw new DomainException("Günlük araması en fazla 120 karakter olabilir.");limit=Math.Clamp(limit,1,200);lock(_gate)
  {
   if(!Directory.Exists(root))return[];var result=new List<OperationLog>();foreach(var file in Directory.EnumerateFiles(root,"*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc))
   {
    AppFiles.RejectLink(file);foreach(var line in File.ReadAllLines(file).Reverse())try{var entry=JsonSerializer.Deserialize<OperationLog>(line);if(entry is not null&&entry.Id>after&&(string.IsNullOrEmpty(level)||entry.Level==level)&&(string.IsNullOrEmpty(query)||entry.Message.Contains(query,StringComparison.OrdinalIgnoreCase)||entry.Category.Contains(query,StringComparison.OrdinalIgnoreCase)))result.Add(entry);if(result.Count>=limit)break;}catch(JsonException){}if(result.Count>=limit)break;
   }return result.OrderBy(entry=>entry.Id).ToArray();
  }
 }
 private sealed class JournalLogger(OperationJournal owner,string category):ILogger
 {
  public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;public bool IsEnabled(LogLevel level)=>level>=LogLevel.Information;
  public void Log<TState>(LogLevel level,EventId eventId,TState state,Exception? exception,Func<TState,Exception?,string> formatter){if(IsEnabled(level))owner.Write(category,level,formatter(state,exception),exception);}
 }
}

using HomeServer.Core;

namespace HomeServer.Infrastructure;

public interface IProductWorkHandler
{
    string[] Kinds { get; }
    Task RunAsync(ProductJob job,CancellationToken token);
}
public sealed class ProductWorker(MediaStore store,IEnumerable<IProductWorkHandler> handlers,EventHub events,ILogger<ProductWorker> logger):BackgroundService
{
    private readonly Dictionary<string,IProductWorkHandler> _handlers=handlers.SelectMany(handler=>handler.Kinds.Select(kind=>(kind,handler))).ToDictionary(value=>value.kind,value=>value.handler);
    public override Task StartAsync(CancellationToken token){store.RecoverProductJobs(_handlers.Keys.ToArray());return base.StartAsync(token);}
    protected override Task ExecuteAsync(CancellationToken token)=>Task.WhenAll(_handlers.Values.Distinct().Select(handler=>RunLaneAsync(handler,token)));
    private async Task RunLaneAsync(IProductWorkHandler handler,CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            var job=store.ClaimProductJob(handler.Kinds); if(job is null){try{await Task.Delay(1500,token);}catch(OperationCanceledException){break;}continue;}
            using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromHours(4));
            using var watchStop=CancellationTokenSource.CreateLinkedTokenSource(token);var watch=Watch();
            try{store.RequireProductJob(job);await _handlers[job.Kind].RunAsync(job,budget.Token);}
            catch(OperationCanceledException)when(token.IsCancellationRequested){}
            catch(Exception ex){store.FinishProductJob(job,null,"failed",ex is DomainException?ex.Message:ex is OperationCanceledException?"İş iptal edildi veya süre sınırına ulaştı.":"İş tamamlanamadı; işlem günlüğünü incele.");logger.LogWarning("{Kind} işi {Id}: {ErrorType}",job.Kind,job.Id,ex.GetType().Name);}
            finally{watchStop.Cancel();try{await watch;}catch(OperationCanceledException){}events.Publish("product.changed");}
            async Task Watch(){while(!watchStop.IsCancellationRequested){await Task.Delay(1000,watchStop.Token);try{store.RequireProductJob(job);}catch{budget.Cancel();return;}}}
        }
    }
}

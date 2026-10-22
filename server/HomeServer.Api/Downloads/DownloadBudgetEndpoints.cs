using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Downloads;

public static class DownloadBudgetEndpoints
{
    public static void MapDownloadBudgets(this RouteGroupBuilder api)
    {
        api.MapGet("/account/download-usage", (CurrentAccount account, MediaStore store, TimeProvider clock) => Results.Ok(store.GetDownloadBudgetUsage(account.User.Id, clock.GetUtcNow())));
        api.MapGet("/account/download-history", (int? offset, int? limit, CurrentAccount account, MediaStore store) => Results.Ok(store.GetDownloadBudgetHistory(account.User.Id, offset ?? 0, limit ?? 30)));
        var admin = api.MapGroup("").RequireAuthorization("admin");
        admin.MapGet("/users/{id}/download-usage", (string id, MediaStore store, TimeProvider clock) => Results.Ok(store.GetDownloadBudgetUsage(id, clock.GetUtcNow())));
        admin.MapGet("/users/{id}/download-history", (string id, int? offset, int? limit, MediaStore store) => { _ = store.GetAccount(id) ?? throw new DomainException("Hesap bulunamadı.", 404); return Results.Ok(store.GetDownloadBudgetHistory(id, offset ?? 0, limit ?? 30)); });
        admin.MapPut("/users/{id}/download-budget", (string id, DownloadBudgetPolicy input, CurrentAccount account, HttpContext context, MediaStore store, DownloadWorker worker, EventHub events) =>
        {
            _ = DownloadAccess.Actor(account, context, store); var result = store.SaveDownloadBudget(id, input); worker.InterruptUser(id);
            events.Publish("download-budgets.changed"); return Results.Ok(result);
        });
        admin.MapPut("/downloads/{id}/owner", (string id, AssignDownloadOwner input, CurrentAccount account, HttpContext context, MediaStore store, EventHub events) =>
        {
            _ = DownloadAccess.Actor(account, context, store); var result = store.AssignDownloadAccount(id, input, account.User);
            events.Publish("downloads.changed"); events.Publish("download-budgets.changed"); return Results.Ok(result);
        });
    }
}

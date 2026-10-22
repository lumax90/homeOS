using HomeServer.Core;
using HomeServer.Infrastructure;

namespace HomeServer.Identity;
public static class DeviceLinkEndpoints
{
 public static void MapDeviceLinks(this WebApplication app)
 {
  app.MapPost("/api/device-links",(DeviceLinkStart input,MediaStore store)=>Results.Ok(store.StartDeviceLink(input.DeviceName))).RequireRateLimiting("credentials");
  app.MapPost("/api/device-links/poll",async(DeviceLinkPoll input,MediaStore store,IdentityService identity,HttpContext context)=>{var user=store.ConsumeDeviceLink(input);if(user is null)return Results.Ok(new{approved=false});await identity.SignInAsync(context,user);return Results.Ok(new{approved=true});}).RequireRateLimiting("link-poll");
  app.MapGet("/api/device-links/{code}",(string code,CurrentAccount actor,MediaStore store)=>Results.Ok(store.DeviceLinkInfo(code,actor.User))).RequireAuthorization().RequireRateLimiting("credentials");
  app.MapPost("/api/device-links/approve",(DeviceLinkApproval input,CurrentAccount actor,MediaStore store)=>{store.ApproveDeviceLink(input.Code,actor.User.Id,actor.SessionId);return Results.NoContent();}).RequireAuthorization().RequireRateLimiting("credentials");
 }
}

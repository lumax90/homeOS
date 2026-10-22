using HomeServer.Core;
using HomeServer.Identity;
using HomeServer.Infrastructure;

namespace HomeServer.Shelves;
public sealed class ShelfThumbnails(MediaStore store,MediaTools tools,HomePaths paths)
{
 private readonly SemaphoreSlim _gate=new(2,2);
 public async Task<string> GetAsync(string id,Account actor,CancellationToken token)
 {
  var source=store.ShelfSource(id,actor);if(source.Asset.Kind!="photo")throw new DomainException("Fotoğraf bulunamadı.",404);var root=AppFiles.DirectoryIn(paths.Data,"shelf-thumbnails");var key=LiveTv.PlaylistGuide.Id(id+source.Asset.Fingerprint);var path=Path.Combine(root,key+".jpg");if(File.Exists(path))return path;if(!tools.FfmpegAvailable)throw new DomainException("Fotoğraf önizlemesi için FFmpeg gerekli.",409);
  await _gate.WaitAsync(token);try{if(File.Exists(path))return path;var temporary=Path.Combine(root,key+"-"+Guid.NewGuid().ToString("N")+".jpg");try{await MediaProcess.RunAsync(tools.FfmpegExecutable,["-hide_banner","-loglevel","error","-nostdin","-protocol_whitelist","file,pipe","-i",source.Path,"-frames:v","1","-vf","scale=800:-2","-threads","1",temporary],TimeSpan.FromSeconds(30),token);_=store.ShelfSource(id,actor);File.Move(temporary,path,true);return path;}finally{if(File.Exists(temporary))File.Delete(temporary);}}finally{_gate.Release();}
 }
}
public static class ShelfEndpoints
{
 public static void MapShelves(this RouteGroupBuilder api)
 {
  api.MapGet("/shelves",(string? library,string? kind,string? folder,string? q,int? offset,CurrentAccount actor,MediaStore store)=>Results.Ok(store.ShelfAssets(actor.User,library,kind,folder,q,offset??0)));
  api.MapGet("/shelves/{id}",(string id,CurrentAccount actor,MediaStore store)=>Results.Ok(store.RequireShelfAsset(id,actor.User)));
  api.MapMethods("/shelves/{id}/file",["GET","HEAD"],(string id,CurrentAccount actor,MediaStore store,HttpContext context)=>{var source=store.ShelfSource(id,actor.User);context.Response.Headers.CacheControl="private, no-store";return Results.Stream(new FileStream(source.Path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.RandomAccess),source.Asset.Mime,enableRangeProcessing:true,entityTag:new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\""+source.Asset.Fingerprint+"\""));});
  api.MapGet("/shelves/{id}/thumbnail",async(string id,CurrentAccount actor,ShelfThumbnails thumbnails,CancellationToken token)=>Results.File(await thumbnails.GetAsync(id,actor.User,token),"image/jpeg"));
  api.MapGet("/shelves/{id}/book",(string id,CurrentAccount actor,BookService books)=>Results.Ok(books.Info(id,actor.User)));
  api.MapGet("/shelves/{id}/chapter/{index:int}",(string id,int index,CurrentAccount actor,BookService books,HttpContext context)=>{context.Response.Headers["Content-Security-Policy"]="default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; script-src 'none'";return Results.Text(books.Chapter(id,index,actor.User,context.Request.PathBase),"text/html; charset=utf-8");});
  api.MapGet("/shelves/{id}/resource",(string id,string key,CurrentAccount actor,BookService books)=>{var resource=books.Resource(id,key,actor.User);return Results.Bytes(resource.Data,resource.Mime);});
  api.MapGet("/shelves/{id}/progress",(string id,CurrentAccount actor,MediaStore store)=>Results.Ok(store.ShelfProgress(id,actor.User)));
  api.MapPut("/shelves/{id}/progress",(string id,ShelfProgressInput input,CurrentAccount actor,MediaStore store)=>{store.SaveShelfProgress(id,input,actor.User);return Results.NoContent();});
  api.MapPut("/shelves/{id}/favorite",(string id,MusicFavoriteInput input,CurrentAccount actor,MediaStore store)=>{store.FavoriteShelf(id,input.Favorite,actor.User);return Results.NoContent();});
  api.MapPut("/shelves/{id}/access",(string id,MediaAccessInput input,MediaStore store)=>{store.ShelfAccessSettings(id,input);return Results.NoContent();}).RequireAuthorization("admin");
 }
}

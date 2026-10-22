using HomeServer.Core;
using HomeServer.Infrastructure;
using HomeServer.Scanning;

namespace HomeServer.Shelves;
public static class ShelfScanner
{
 public static (string Kind,string Mime)? Format(string path)=>Path.GetExtension(path).ToLowerInvariant()switch
 {".jpg" or".jpeg"=>("photo","image/jpeg"),".png"=>("photo","image/png"),".webp"=>("photo","image/webp"),".avif"=>("photo","image/avif"),".heic" or".heif"=>("photo","image/heic"),".pdf"=>("pdf","application/pdf"),".epub"=>("epub","application/epub+zip"),".cbz"=>("comic","application/zip"),".cbr"=>("comic","application/vnd.rar"),".txt"=>("text","text/plain"),_=>null};
 public static Task ScanAsync(MediaStore store,EventHub events,Library library,ScanJob job,CancellationToken token)
 {
  var root=MediaPaths.NormalizeRoot(library.Path);var configuration=store.GetLibraryConfiguration(library.Id);var scan=Guid.NewGuid().ToString("N");var count=0;var added=0;
  foreach(var path in LibraryTraversal.Files(root,configuration.Options,token,store.LibraryIgnoredRoots(root)))
  {token.ThrowIfCancellationRequested();var format=Format(path);if(format is null||library.Kind=="photos"&&format.Value.Kind!="photo"||library.Kind=="books"&&format.Value.Kind=="photo")continue;if(store.UpsertShelfAsset(library,path,format.Value.Kind,format.Value.Mime,scan))added++;count++;if(count%100==0){if(store.GetLibraryConfiguration(library.Id).Revision!=configuration.Revision)throw new OperationCanceledException();store.UpdateJobProgress(job.Id,count,added,$"{count} belge/fotoğraf işlendi.");}}
  _=MediaPaths.NormalizeRoot(root);token.ThrowIfCancellationRequested();if(store.GetLibraryConfiguration(library.Id).Revision!=configuration.Revision)throw new OperationCanceledException();store.FinishShelfScan(library.Id,scan);store.FinishScan(library.Id,scan,job.Id,count,added);store.RecordLibraryScan(library.Id);events.Publish("shelves.changed");return Task.CompletedTask;
 }
}

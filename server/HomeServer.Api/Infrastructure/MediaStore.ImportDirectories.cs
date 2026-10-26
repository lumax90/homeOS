using HomeServer.Core;
using HomeServer.Downloads;

namespace HomeServer.Infrastructure;

public sealed partial class MediaStore
{
    public string DownloadImportDirectory(string id)
    {
        DownloadFiles.ValidId(id);using var db=Open();
        var directory=Scalar(db,"SELECT import_directory FROM downloads WHERE id=$id",("$id",id)) as string??id;
        directory=DownloadFiles.ValidateRelative(directory);
        if(directory.Contains(Path.DirectorySeparatorChar))throw new DomainException("İndirme klasörü geçersiz.",409);
        return directory;
    }
    public string ReserveDownloadImportDirectory(string id,int revision,string proposed)
    {
        proposed=DownloadFiles.ValidateRelative(proposed);if(proposed.Contains(Path.DirectorySeparatorChar))throw new DomainException("İndirme klasörü geçersiz.",409);
        lock(_writeGate)
        {
            var download=GetDownload(id)??throw new DomainException("İndirme bulunamadı.",404);
            if(download.Revision!=revision||download.DesiredState!="running")throw new OperationCanceledException();
            using var db=Open();
            Execute(db,"UPDATE downloads SET import_directory=COALESCE(import_directory,$directory) WHERE id=$id",("$id",id),("$directory",proposed));
            return DownloadImportDirectory(id);
        }
    }
    public string DownloadImportFile(string id,string relative)=>Path.Combine(DownloadImportDirectory(id),DownloadFiles.ValidateRelative(relative));
}

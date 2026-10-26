using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HomeServer.Tests;

public sealed class DownloadImportDirectoryTests
{
    [Fact]
    public void ReadableNamesArePortableStableAndDoNotChangeDownloadIdentity()
    {
        const string id="0123456789abcdef0123456789abcdef";
        var name=DownloadImportNaming.DirectoryName("ORIGINAL İstanbul: Series / Edition",id,2024,1);
        Assert.StartsWith("ORIGINAL İstanbul",name);Assert.Contains("(2024) S01",name);Assert.EndsWith("[0123456789ab]",name);
        Assert.Equal(name,DownloadFiles.ValidateRelative(name));Assert.DoesNotContain('/',name);Assert.DoesNotContain('\\',name);
        Assert.Equal(name,DownloadImportNaming.DirectoryName("ORIGINAL İstanbul: Series / Edition",id,2024,1));
        var longName=DownloadImportNaming.DirectoryName(new string('Ü',500),id);Assert.True(System.Text.Encoding.UTF8.GetByteCount(longName)<240);
    }
    [Fact]
    public void ImportDirectoryReservationSurvivesRetriesAndDoesNotResetPausedState()
    {
        using var home=new TestWorkspace();home.Store.CreateOwner(new("ISOLATED import owner","preserved"));
        var download=home.Store.AddDownload(["isolated-directory-hash"],"ORIGINAL Film","torrent","protected",100);
        Assert.Equal(download.Id,home.Store.DownloadImportDirectory(download.Id));
        var proposed=DownloadImportNaming.DirectoryName(download.Name,download.Id);
        Assert.Equal(proposed,home.Store.ReserveDownloadImportDirectory(download.Id,download.Revision,proposed));
        Assert.Equal(proposed,home.Store.ReserveDownloadImportDirectory(download.Id,download.Revision,"Changed title [other]"));
        Assert.Equal(Path.Combine(proposed,"original.mp4"),home.Store.DownloadImportFile(download.Id,"original.mp4"));
        var paused=home.Store.SetDownloadIntent(download.Id,"paused");
        Assert.Throws<OperationCanceledException>(()=>home.Store.ReserveDownloadImportDirectory(download.Id,paused.Revision,"Different"));
        Assert.Equal("paused",home.Store.GetDownload(download.Id)!.DesiredState);Assert.Equal(proposed,home.Store.DownloadImportDirectory(download.Id));
    }
    [Fact]
    public void Schema43RowsKeepTheirLegacyImportDirectoryAcrossAdditiveMigration()
    {
        using var home=new TestWorkspace();var data=Path.Combine(home.Root,"schema43");Directory.CreateDirectory(data);var database=Path.Combine(data,"home.db");
        const string id="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        using(var db=new SqliteConnection($"Data Source={database}"))
        {
            db.Open();
            foreach(var resource in typeof(MediaStore).Assembly.GetManifestResourceNames().Where(name=>name.Contains(".Migrations.")&&int.TryParse(name.Split(".Migrations.")[1][..3],out var number)&&number<=43).Order())
            {
                using var stream=typeof(MediaStore).Assembly.GetManifestResourceStream(resource)!;using var reader=new StreamReader(stream);using var command=db.CreateCommand();command.CommandText=reader.ReadToEnd();command.ExecuteNonQuery();
                if(resource.EndsWith("001_initial.sql",StringComparison.Ordinal)){command.CommandText="INSERT INTO owner VALUES(1,'ISOLATED legacy owner','preserved')";command.ExecuteNonQuery();}
            }
            using var seed=db.CreateCommand();seed.CommandText=$"""
                INSERT INTO downloads(id,info_hash,name,source_kind,source,desired_state,status,created_at,updated_at) VALUES('{id}','isolated-legacy-hash','ORIGINAL legacy film','torrent','protected','paused','paused','2026-10-26','2026-10-26');
                PRAGMA user_version=43;
                """;seed.ExecuteNonQuery();
        }
        var store=new MediaStore(new(data,Path.Combine(data,"thumbnails"),null));store.Initialize();store.Initialize();
        Assert.Equal(id,store.DownloadImportDirectory(id));Assert.Equal("paused",store.GetDownload(id)!.DesiredState);
        using var verified=new SqliteConnection($"Data Source={database}");verified.Open();using var check=verified.CreateCommand();check.CommandText="PRAGMA user_version";Assert.Equal(44L,check.ExecuteScalar());
    }
}

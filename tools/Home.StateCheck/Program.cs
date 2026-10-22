using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HomeServer.Core;
using HomeServer.Downloads;
using HomeServer.Infrastructure;
using Microsoft.Data.Sqlite;

if(args.Length!=3)throw new ArgumentException("Usage: Home.StateCheck <backup.zip> <new-isolated-directory> <evidence.json>");
var backup=Path.GetFullPath(args[0]);var directory=Path.GetFullPath(args[1]);var evidence=Path.GetFullPath(args[2]);
if(Directory.Exists(directory))throw new InvalidOperationException("Use a new isolated directory.");
Directory.CreateDirectory(directory);BackupManifest manifest;
using(var zip=ZipFile.OpenRead(backup))
{
    var entry=zip.GetEntry("manifest.json")??throw new InvalidDataException("Backup manifest missing.");
    if(entry.Length>8*1024*1024)throw new InvalidDataException("Manifest is too large.");
    using(var input=entry.Open())manifest=JsonSerializer.Deserialize<BackupManifest>(input)??throw new InvalidDataException("Invalid manifest.");
    if(manifest.Format!=1||manifest.Files.Length>50000||manifest.Files.Sum(file=>file.Length)>1024L*1024*1024||manifest.Files.Length!=zip.Entries.Count-1)throw new InvalidDataException("Invalid backup bounds.");
    var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach(var file in manifest.Files)
    {
        var relative=DownloadFiles.ValidateRelative(file.Path);if(!names.Add(relative)||file.Length<0)throw new InvalidDataException("Invalid backup file.");
        var item=zip.GetEntry(file.Path)??throw new InvalidDataException("Missing backup file.");if(item.Length!=file.Length)throw new InvalidDataException("Backup size mismatch.");
        var target=Path.Combine(directory,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await using(var input=item.Open())await using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))await input.CopyToAsync(output);
        if(StateFolders.Hash(target)!=file.Sha256)throw new InvalidDataException("Backup hash mismatch.");
    }
}
var database=Path.Combine(directory,"home.db");var before=Capture(database,null);
using(var db=Open(database)){using var version=db.CreateCommand();version.CommandText="PRAGMA user_version";if(Convert.ToInt32(version.ExecuteScalar())!=manifest.Schema)throw new InvalidDataException("Backup schema mismatch.");}
var store=new MediaStore(new(directory,Path.Combine(directory,"thumbnails"),null));store.Initialize();store.Initialize();
var after=Capture(database,before.ToDictionary(table=>table.Name,table=>table.Columns));
foreach(var table in before)
{
    var migrated=after.Single(value=>value.Name==table.Name);
    if(table.Rows!=migrated.Rows||table.Sha256!=migrated.Sha256)throw new InvalidOperationException("Original data changed in table: "+table.Name);
}
foreach(var file in manifest.Files.Where(file=>file.Path!="home.db"))if(StateFolders.Hash(Path.Combine(directory,DownloadFiles.ValidateRelative(file.Path)))!=file.Sha256)throw new InvalidDataException("Non-database backup data changed.");
using(var db=Open(database))
{
    using var check=db.CreateCommand();check.CommandText="PRAGMA integrity_check";if(check.ExecuteScalar() as string!="ok")throw new InvalidDataException("Database integrity failed.");
    check.CommandText="PRAGMA foreign_key_check";using var rows=check.ExecuteReader();if(rows.Read())throw new InvalidDataException("Database references failed.");
}
Directory.CreateDirectory(Path.GetDirectoryName(evidence)!);
await File.WriteAllTextAsync(evidence,JsonSerializer.Serialize(new{verifiedAt=DateTimeOffset.UtcNow,backup,backupSha256=StateFolders.Hash(backup),fromSchema=manifest.Schema,toSchema=MediaStore.LatestSchema,isolatedDirectory=directory,originalTables=before.Count,originalRows=before.Sum(table=>table.Rows),allOriginalColumnsAndRowsPreserved=true,keysAndManagedSubtitlesPreserved=true,foreignKeyViolations=0,integrity="ok",tables=before.Select(table=>new{table.Name,table.Rows,table.Sha256})},new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
Console.WriteLine($"Schema {manifest.Schema} -> {MediaStore.LatestSchema}: {before.Count} original tables / {before.Sum(table=>table.Rows)} rows preserved; integrity/FK/keys passed.");

static string Quote(string name)=>"\""+name.Replace("\"","\"\"")+"\"";
static SqliteConnection Open(string database){var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=database,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());db.Open();return db;}
static List<TableState> Capture(string database,Dictionary<string,string[]>? schema)
{
    using var db=Open(database);var names=new List<string>();
    using(var query=db.CreateCommand()){query.CommandText="SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";using var rows=query.ExecuteReader();while(rows.Read())names.Add(rows.GetString(0));}
    var result=new List<TableState>();
    foreach(var name in schema?.Keys??names.AsEnumerable())
    {
        string[] columns;
        if(schema is not null)columns=schema[name];
        else{var values=new List<string>();using var fields=db.CreateCommand();fields.CommandText="PRAGMA table_info("+Quote(name)+")";using var rows=fields.ExecuteReader();while(rows.Read())values.Add(rows.GetString(1));columns=values.ToArray();}
        var content=new List<string>();using(var query=db.CreateCommand()){query.CommandText="SELECT "+string.Join(',',columns.Select(Quote))+" FROM "+Quote(name);using var rows=query.ExecuteReader();while(rows.Read()){var values=new object?[rows.FieldCount];for(var column=0;column<values.Length;column++)values[column]=rows.IsDBNull(column)?null:rows.GetValue(column);content.Add(JsonSerializer.Serialize(values));}}
        content.Sort(StringComparer.Ordinal);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);foreach(var row in content)hash.AppendData(Encoding.UTF8.GetBytes(row+"\n"));
        result.Add(new(name,columns,content.Count,Convert.ToHexStringLower(hash.GetHashAndReset())));
    }
    return result;
}
internal sealed record TableState(string Name,string[] Columns,int Rows,string Sha256);

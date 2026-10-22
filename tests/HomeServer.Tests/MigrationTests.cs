using HomeServer.Core;
using HomeServer.Infrastructure;
using Microsoft.Data.Sqlite;

namespace HomeServer.Tests;

public sealed class MigrationTests
{
    [Fact]
    public void SourceCatalogMigrationKeepsExistingConnectionsSecretsHistoryAndBudgets()
    {
        using var home=new TestWorkspace();var data=Path.Combine(home.Root,"schema38");Directory.CreateDirectory(data);var database=Path.Combine(data,"home.db");
        using(var db=new SqliteConnection($"Data Source={database}"))
        {
            db.Open();using var setting=db.CreateCommand();setting.CommandText="PRAGMA foreign_keys=OFF";setting.ExecuteNonQuery();
            foreach(var name in typeof(MediaStore).Assembly.GetManifestResourceNames().Where(name=>name.Contains(".Migrations.")&&int.TryParse(name.Split(".Migrations.")[1][..3],out var number)&&number<=38).Order())
            {
                using var stream=typeof(MediaStore).Assembly.GetManifestResourceStream(name)!;using var reader=new StreamReader(stream);using var migration=db.CreateCommand();migration.CommandText=reader.ReadToEnd();migration.ExecuteNonQuery();
                if(name.EndsWith("001_initial.sql",StringComparison.Ordinal)){migration.CommandText="INSERT INTO owner VALUES(1,'Owner','preserved')";migration.ExecuteNonQuery();}
            }
            using var seed=db.CreateCommand();seed.CommandText="""
                INSERT INTO indexers(id,name,endpoint,protocol,secret,enabled,priority,categories,allowed_hosts,created_at) VALUES('source','Existing','https://fixture.invalid/api','torznab','preserved-secret',1,25,'2000','','2026-10-16');
                INSERT INTO source_transports(indexer_id,adapter_id,protected_settings) VALUES('source','nyaa-rss','preserved-network');
                INSERT INTO source_request_history(indexer_id,action,host,status,duration_ms,bytes,created_at) VALUES('source','query','fixture.invalid',200,20,512,'2026-10-16');
                INSERT INTO source_request_budgets(indexer_id,day,queries,grabs) VALUES('source','2026-10-16',7,2);
                PRAGMA user_version=38;
                """;seed.ExecuteNonQuery();
        }
        var store=new MediaStore(new(data,Path.Combine(data,"thumbnails"),null));store.Initialize();store.Initialize();var source=Assert.Single(store.GetIndexers());
        Assert.True(source.Enabled);Assert.Null(source.ProviderId);Assert.Equal("preserved-secret",store.GetIndexer(source.Id).Secret);Assert.Equal("preserved-network",store.GetSourceTransport(source.Id).Settings);
        store.PruneSourceSetups();Assert.Single(store.GetIndexers());
        using var verified=new SqliteConnection($"Data Source={database}");verified.Open();using var check=verified.CreateCommand();check.CommandText="SELECT queries FROM source_request_budgets WHERE indexer_id='source'";Assert.Equal(7L,check.ExecuteScalar());check.CommandText="SELECT COUNT(*) FROM source_request_history";Assert.Equal(1L,check.ExecuteScalar());check.CommandText="PRAGMA foreign_key_check";using var rows=check.ExecuteReader();Assert.False(rows.Read());
    }
    [Fact]
    public void SchemaThirtySevenIntegrationMigrationKeepsRecordingPartsAndClosesLegacyReceiverTokens()
    {
        using var home = new TestWorkspace(); var data = Path.Combine(home.Root, "schema37"); Directory.CreateDirectory(data); var database = Path.Combine(data, "home.db");
        using (var db = new SqliteConnection($"Data Source={database}"))
        {
            db.Open(); using var setting = db.CreateCommand(); setting.CommandText = "PRAGMA foreign_keys=OFF"; setting.ExecuteNonQuery();
            foreach (var name in typeof(MediaStore).Assembly.GetManifestResourceNames().Where(name => name.Contains(".Migrations.") && int.TryParse(name.Split(".Migrations.")[1][..3], out var number) && number <= 37).Order())
            {
                using var stream = typeof(MediaStore).Assembly.GetManifestResourceStream(name)!; using var reader = new StreamReader(stream); using var migration = db.CreateCommand(); migration.CommandText = reader.ReadToEnd(); migration.ExecuteNonQuery();
                if (name.EndsWith("001_initial.sql", StringComparison.Ordinal)) { migration.CommandText = "INSERT INTO owner VALUES(1,'Owner','preserved')"; migration.ExecuteNonQuery(); }
            }
            using var seed = db.CreateCommand(); seed.CommandText = """
                INSERT INTO libraries(id,name,path,path_key,kind,is_demo,created_at) VALUES('library','Isolated','/isolated','/isolated','movies',0,'2026-10-16');
                INSERT INTO media(id,library_id,relative_path,path_key,title,search_text,kind,mime_type,size_bytes,modified_at,added_at,last_seen_scan) VALUES('file','library','one.mp4','one.mp4','One','one','video','video/mp4',4,'2026-10-16','2026-10-16','scan');
                INSERT INTO playback_devices(id,name,created_at,last_seen_at) VALUES('device','Isolated device','2026-10-16','2026-10-16');
                INSERT INTO playback_leases(id,user_id,login_id,device_id,client_id,client_revision,media_id,fingerprint,match_revision,title,mode,status,created_at,last_seen_at,global_revision,user_revision,device_revision,position_seconds)
                  VALUES('play','1','login','device','client',1,'file','4-abc',0,'Isolated','receiver-file','active','2026-10-16','2026-10-16',0,0,0,42);
                INSERT INTO receiver_grants(id,token_hash,user_id,login_id,playback_id,target_name,mode,expires_at,created_at) VALUES('receiver','preserved-hash','1','login','play','Legacy receiver','original','2099-01-01','2026-10-16');
                INSERT INTO live_sources(id,name,protected_input,enabled,tuners,created_by,next_sync) VALUES('tv','Isolated TV','protected',1,2,'1','2026-10-16');
                INSERT INTO live_channels(id,source_id,source_key,name,group_name,protected_url) VALUES('channel','tv','isolated','Isolated channel','','protected');
                INSERT INTO dvr_recordings(id,user_id,user_version,channel_id,episode_key,title,starts_at,ends_at,priority,status,lease,bytes,created_at,updated_at)
                  VALUES('recording','1',1,'channel','episode','Preserved recording','2026-10-16','2026-10-17',25,'recording','old-lease',1234,'2026-10-16','2026-10-16');
                PRAGMA user_version=37;
                """; seed.ExecuteNonQuery();
        }
        var staging = Path.Combine(data, "downloads", "staging", "dvr", "recording"); Directory.CreateDirectory(staging); var part = Path.Combine(staging, "part0000.ts"); File.WriteAllText(part, "preserved capture bytes"); var hash = StateFolders.Hash(part);
        var store = new MediaStore(new(data, Path.Combine(data,"thumbnails"), null)); store.Initialize(); store.Initialize();
        var recording = store.GetDvr("recording")!; Assert.Equal("finalizing", recording.Status); Assert.Null(recording.Lease); Assert.Equal(1234, recording.Bytes); Assert.Equal(hash, StateFolders.Hash(part));
        Assert.True(store.GetReceiver("receiver")!.Closed); Assert.Equal(42, store.GetPlaybackLease("play")!.PositionSeconds); Assert.Equal("One", store.GetMedia("file")!.Title);
        using var verified = new SqliteConnection($"Data Source={database}"); verified.Open(); using var command = verified.CreateCommand(); command.CommandText = "PRAGMA user_version"; Assert.Equal(MediaStore.LatestSchema, Convert.ToInt32(command.ExecuteScalar()));
        command.CommandText = "PRAGMA foreign_key_check"; using var rows = command.ExecuteReader(); Assert.False(rows.Read());
    }
    [Fact]
    public void MusicCatalogMigrationPreservesLegacyAlbumIdsHistoryFollowsQueuesAndTagJournals()
    {
        using var home = new TestWorkspace(); var data = Path.Combine(home.Root, "schema29"); Directory.CreateDirectory(data); var database = Path.Combine(data, "home.db");
        using (var db = new SqliteConnection($"Data Source={database}"))
        {
            db.Open(); using var setting = db.CreateCommand(); setting.CommandText = "PRAGMA foreign_keys=OFF"; setting.ExecuteNonQuery();
            foreach (var name in typeof(MediaStore).Assembly.GetManifestResourceNames().Where(name => name.Contains(".Migrations.") && int.TryParse(name.Split(".Migrations.")[1][..3], out var number) && number <= 29).Order())
            {
                using var stream = typeof(MediaStore).Assembly.GetManifestResourceStream(name)!; using var reader = new StreamReader(stream); using var migration = db.CreateCommand(); migration.CommandText = reader.ReadToEnd(); migration.ExecuteNonQuery();
                if (name.EndsWith("001_initial.sql", StringComparison.Ordinal)) { migration.CommandText = "INSERT INTO owner VALUES(1,'Owner','preserved')"; migration.ExecuteNonQuery(); }
            }
            using var seed = db.CreateCommand(); seed.CommandText = """
                INSERT INTO libraries(id,name,path,path_key,kind,is_demo,created_at) VALUES('library','Isolated','/isolated','/isolated','music',0,'2026-10-15');
                INSERT INTO media(id,library_id,relative_path,path_key,title,search_text,kind,mime_type,size_bytes,modified_at,added_at,last_seen_scan,duration_seconds) VALUES('file','library','one.flac','one.flac','One','one','audio','audio/flac',4,'2026-10-15','2026-10-15','scan',100);
                INSERT INTO music_artists(id,mbid,name,source,local_key) VALUES('artist','11111111-1111-4111-8111-111111111111','Old Artist','tags','old-artist-key');
                INSERT INTO music_albums(id,artist_id,title,local_key,source) VALUES('album','artist','Old Album','legacy-mbid-album-key','tags');
                INSERT INTO music_tracks(media_id,album_id,title,artist,disc_number,track_number,lyrics,replay_gain) VALUES('file','album','One','Old Artist',1,1,'Original lyrics',-4.5);
                INSERT INTO music_follows(user_id,artist_mbid,name,created_at) VALUES('1','11111111-1111-4111-8111-111111111111','Old Artist','2026-10-15');
                INSERT INTO playlists(id,user_id,name,updated_at) VALUES('list','1','Personal list','2026-10-15');
                INSERT INTO playlist_items(playlist_id,position,media_id) VALUES('list',0,'file');
                INSERT INTO playback_queues(user_id,items_json,current_index,shuffle,repeat_mode,updated_at) VALUES('1','["file"]',0,0,'off','2026-10-15');
                INSERT INTO music_favorites(user_id,media_id,created_at) VALUES('1','file','2026-10-15');
                INSERT INTO user_watch_state(user_id,media_id,position_seconds,watched,updated_at) VALUES('1','file',37,0,'2026-10-15');
                INSERT INTO music_tasks(id,media_id,library_id,kind,status,plan_json,created_at,updated_at) VALUES('journal','file','library','tags','completed','{"legacy":"preserve-exactly"}','2026-10-15','2026-10-15');
                PRAGMA user_version=29;
                """; seed.ExecuteNonQuery();
        }
        var store = new MediaStore(new(data, Path.Combine(data, "thumbnails"), null)); store.Initialize(); store.Initialize(); var actor = store.GetAccount("1")!;
        Assert.Equal("album", Assert.Single(store.GetMusicAlbums(actor, 0).Items).Id); Assert.Equal("Old Artist", Assert.Single(store.GetMusicAlbums(actor, 0).Items).Artist);
        Assert.Equal("file", store.NextMusicIndex()); Assert.Null(Assert.Single(store.GetMusicAlbums(actor, 0).Items).ArtistCredits![0].Mbid);
        store.SaveMusicTags("file", new("One", "Old Artist", "Old Artist", "Old Album", null, 1, 1, "11111111-1111-4111-8111-111111111111", null, null, null, "Original lyrics", -4.5, 16));
        Assert.Equal("album", Assert.Single(store.GetMusicAlbums(actor, 0).Items).Id); Assert.Null(store.NextMusicIndex());
        Assert.Equal(37, store.GetMedia("file")!.PositionSeconds); Assert.Single(store.FollowedMusicArtists("1")); Assert.Equal("file", Assert.Single(store.GetPlaylistItems("list", actor)).Id); Assert.Equal("file", Assert.Single(store.GetQueue(actor).Items).Id);
        Assert.Equal("{\"legacy\":\"preserve-exactly\"}", store.GetMusicTask("journal")!.PlanJson);
        using var verified = new SqliteConnection($"Data Source={database}"); verified.Open(); using var command = verified.CreateCommand(); command.CommandText = "PRAGMA foreign_key_check"; using var rows = command.ExecuteReader(); Assert.False(rows.Read());
    }
    [Fact]
    public void EpisodeTableRebuildPreservesCompositeReferencesAliasesOrdersAndWatchState()
    {
        using var home = new TestWorkspace(); var data = Path.Combine(home.Root, "schema28"); Directory.CreateDirectory(data); var database = Path.Combine(data, "home.db");
        using (var db = new SqliteConnection($"Data Source={database}"))
        {
            db.Open(); using var setting = db.CreateCommand(); setting.CommandText = "PRAGMA foreign_keys=OFF"; setting.ExecuteNonQuery();
            foreach (var name in typeof(MediaStore).Assembly.GetManifestResourceNames().Where(name => name.Contains(".Migrations.") && int.TryParse(name.Split(".Migrations.")[1][..3], out var number) && number <= 28).Order())
            {
                using var stream = typeof(MediaStore).Assembly.GetManifestResourceStream(name)!; using var reader = new StreamReader(stream); using var migration = db.CreateCommand(); migration.CommandText = reader.ReadToEnd(); migration.ExecuteNonQuery();
                if (name.EndsWith("001_initial.sql", StringComparison.Ordinal)) { migration.CommandText = "INSERT INTO owner VALUES(1,'Owner','preserved')"; migration.ExecuteNonQuery(); }
            }
            using var seed = db.CreateCommand(); seed.CommandText = """
                INSERT INTO libraries(id,name,path,path_key,kind,is_demo,created_at) VALUES('library','Isolated','/isolated','/isolated','shows',0,'2026-10-15');
                INSERT INTO media(id,library_id,relative_path,path_key,title,search_text,kind,mime_type,size_bytes,modified_at,added_at,last_seen_scan) VALUES('file','library','one.mp4','one.mp4','One','one','video','video/mp4',4,'2026-10-15','2026-10-15','scan');
                INSERT INTO content(id,provider,provider_id,kind,title,original_title,search_text,overview,genres_json,provider_url,updated_at) VALUES('show','tmdb',7200,'show','Isolated','Isolated','isolated','','[]','https://www.themoviedb.org/tv/7200','2026-10-15');
                INSERT INTO content_aliases(provider,kind,provider_id,content_id,linked_at) VALUES('tmdb','show',7200,'show','2026-10-15');
                INSERT INTO episodes(id,content_id,provider_id,season_number,episode_number,title,overview) VALUES('episode','show',72001,1,1,'One','');
                INSERT INTO episode_aliases(provider,provider_id,content_id,episode_id) VALUES('tmdb',72001,'show','episode');
                INSERT INTO media_matches(media_id,content_id,episode_id,matched_at) VALUES('file','show','episode','2026-10-15');
                INSERT INTO episode_numbering(content_id,mode,source) VALUES('show','absolute','manual');
                INSERT INTO episode_order_entries(content_id,number,episode_id) VALUES('show',12,'episode');
                INSERT INTO content_watch_state(user_id,content_id,episode_key,position_seconds,watched,updated_at,source_media_id) VALUES('1','show','episode',37,0,'2026-10-15','file');
                PRAGMA user_version=28;
                """; seed.ExecuteNonQuery();
        }
        var store = new MediaStore(new(data, Path.Combine(data, "thumbnails"), null)); store.Initialize(); store.Initialize();
        Assert.Equal("episode", Assert.Single(store.GetEpisodeCatalog("show")).Id); Assert.Equal("episode", Assert.Single(store.GetMedia("file")!.Episodes!).EpisodeId); Assert.Equal(37, store.GetMedia("file")!.PositionSeconds);
        Assert.Equal(12, Assert.Single(store.GetEpisodeNumbering("show").Entries).Number); Assert.Equal(new[] { "episode" }, store.ResolveProviderEpisodeIds("show", "tmdb", new(72001, 1, 1, "One", "", null, null)));
        using var verified = new SqliteConnection($"Data Source={database}"); verified.Open(); using var command = verified.CreateCommand(); command.CommandText = "PRAGMA foreign_key_check"; using var rows = command.ExecuteReader(); Assert.False(rows.Read());
    }
    [Fact]
    public void NewMigrationsPreserveSchemaTwentySixOfflineGrantsPlaybackAndLegacyTransfers()
    {
        using var home = new TestWorkspace(); var data = Path.Combine(home.Root, "schema26"); Directory.CreateDirectory(data);
        var database = Path.Combine(data, "home.db");
        using (var db = new SqliteConnection($"Data Source={database}"))
        {
            db.Open(); using var pragmas = db.CreateCommand(); pragmas.CommandText = "PRAGMA foreign_keys=OFF"; pragmas.ExecuteNonQuery();
            var resources = typeof(MediaStore).Assembly.GetManifestResourceNames().Where(name => name.Contains(".Migrations.") && int.TryParse(name.Split(".Migrations.")[1][..3], out var number) && number <= 26).Order().ToArray();
            Assert.Equal(26, resources.Length);
            foreach (var resource in resources)
            {
                using var stream = typeof(MediaStore).Assembly.GetManifestResourceStream(resource)!; using var reader = new StreamReader(stream); using var migration = db.CreateCommand(); migration.CommandText = reader.ReadToEnd(); migration.ExecuteNonQuery();
                if (resource.EndsWith("001_initial.sql", StringComparison.Ordinal)) { migration.CommandText = "INSERT INTO owner VALUES(1,'Isolated owner','preserved-hash')"; migration.ExecuteNonQuery(); }
            }
            using var seed = db.CreateCommand(); seed.CommandText = """
                INSERT INTO libraries(id,name,path,path_key,kind,is_demo,created_at) VALUES('library','Isolated library','/fixture','/fixture','movies',0,'2026-10-14');
                INSERT INTO media(id,library_id,relative_path,path_key,title,search_text,kind,mime_type,size_bytes,modified_at,added_at,last_seen_scan)
                  VALUES('media','library','fixture.mp4','fixture.mp4','Isolated title','isolated','video','video/mp4',4,'2026-10-14','2026-10-14','scan');
                INSERT INTO playback_devices(id,name,created_at,last_seen_at) VALUES('device','Isolated device','2026-10-14','2026-10-14');
                INSERT INTO playback_leases(id,user_id,login_id,device_id,client_id,client_revision,media_id,fingerprint,match_revision,title,mode,status,created_at,last_seen_at,ended_at,end_reason,global_revision,user_revision,device_revision,position_seconds)
                  VALUES('play','1','login','device','client',5,'media','4-abc',0,'Isolated title','direct','closed','2026-10-14','2026-10-14','2026-10-14','closed',0,0,0,42);
                INSERT INTO user_offline_policies(user_id,enabled,maximum_items,maximum_bytes,lease_days,revision) VALUES('1',1,10,1073741824,3,2);
                INSERT INTO offline_exports(id,user_id,device_id,media_id,client_id,source_fingerprint,source_catalog_bytes,source_catalog_modified,match_revision,user_version,policy_revision,input_json,status,created_at,expires_at,reserved_bytes)
                  VALUES('copy','1','device','media','offline-client','4-abc',4,'2026-10-14',0,1,2,'{"clientId":"offline-client"}','ready','2026-10-14','2026-10-17',1024);
                INSERT INTO offline_progress_receipts(grant_id,event_id,sequence,request_hash,result_json,created_at) VALUES('copy','event',1,'hash','{"status":"applied"}','2026-10-14');
                INSERT INTO downloads(id,info_hash,name,source_kind,source,desired_state,status,total_bytes,received_bytes,created_at,updated_at)
                  VALUES('legacy-transfer','legacy-hash','Historical package','torrent','protected-source','paused','paused',600,123,'2026-10-14','2026-10-14');
                PRAGMA user_version=26;
                """; seed.ExecuteNonQuery();
        }
        var store = new MediaStore(new(data, Path.Combine(data, "thumbnails"), null)); store.Initialize(); store.Initialize();
        var lease = store.GetPlaybackLease("play")!; Assert.Equal(42, lease.PositionSeconds); Assert.Equal("closed", lease.Status); Assert.Null(lease.WatchRoomId);
        Assert.Equal(2, store.GetOfflinePolicy("1").Revision); Assert.Equal(1024, store.GetOfflineExport("copy")!.ReservedBytes); Assert.Equal("preserved-hash", store.GetOwner()!.PasswordHash);
        var transfer = store.GetDownload("legacy-transfer")!; Assert.Equal(123, transfer.ReceivedBytes); Assert.Null(transfer.Ownership!.UserId); Assert.Equal("legacy", transfer.Ownership.Origin);
        Assert.Equal(600, transfer.Ownership.ChargedBytes); Assert.Equal(0, store.GetDownloadBudgetUsage("1", DateTimeOffset.UtcNow).Acquisitions); Assert.Equal(0, store.GetDownloadBudgetUsage("1", DateTimeOffset.UtcNow).CommittedBytes);
        using var verified = new SqliteConnection($"Data Source={database}"); verified.Open(); using var check = verified.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM offline_progress_receipts"; Assert.Equal(1L, check.ExecuteScalar());
        check.CommandText = "PRAGMA foreign_key_check"; using var rows = check.ExecuteReader(); Assert.False(rows.Read());
    }
    [Fact]
    public void VersionThreeArchivesSyntheticRowsAndPreservesUserRecordsAndKeys()
    {
        using var home = new TestWorkspace();
        var data = Path.Combine(home.Root, "v03");
        Directory.CreateDirectory(Path.Combine(data, "keys"));
        File.WriteAllText(Path.Combine(data, "keys", "key.xml"), "preserve-together");
        using (var db = new SqliteConnection($"Data Source={Path.Combine(data, "home.db")}"))
        {
            db.Open();
            foreach (var name in new[] { "001_initial", "002_metadata", "003_downloads" })
            {
                using var stream = typeof(MediaStore).Assembly.GetManifestResourceStream($"HomeServer.Infrastructure.Migrations.{name}.sql")!;
                using var reader = new StreamReader(stream);
                using var migration = db.CreateCommand(); migration.CommandText = reader.ReadToEnd(); migration.ExecuteNonQuery();
            }
            using var seed = db.CreateCommand();
            seed.CommandText = """
                INSERT INTO owner VALUES(1,'Owner','preserved');
                INSERT INTO libraries VALUES('demo','Synthetic','/demo','/demo','movies',1,'2026-01-01');
                INSERT INTO libraries VALUES('real','My media','/media','/media','movies',0,'2026-01-01');
                INSERT INTO media(id,library_id,relative_path,path_key,title,search_text,kind,mime_type,size_bytes,modified_at,added_at,last_seen_scan)
                  VALUES('synthetic','demo','demo.mp4','demo.mp4','Demo','demo','video','video/mp4',4,'2026-01-01','2026-01-01','scan');
                INSERT INTO watch_state VALUES('synthetic',17,0,'2026-01-01');
                PRAGMA user_version=3;
                """;
            seed.ExecuteNonQuery();
        }
        var store = new MediaStore(new HomePaths(data, Path.Combine(data, "thumbnails"), null));
        store.Initialize();
        Assert.Equal("real", Assert.Single(store.GetLibraries()).Id);
        Assert.Equal(2, store.GetLibraries(true).Count);
        Assert.Null(store.GetMedia("synthetic"));
        Assert.Equal("preserved", store.GetOwner()!.PasswordHash);
        Assert.Equal("preserve-together", File.ReadAllText(Path.Combine(data, "keys", "key.xml")));
        using var verified = new SqliteConnection($"Data Source={Path.Combine(data, "home.db")}");
        verified.Open(); using var query = verified.CreateCommand(); query.CommandText = "SELECT position_seconds FROM watch_state WHERE media_id='synthetic'";
        Assert.Equal(17d, Convert.ToDouble(query.ExecuteScalar()));
    }
    [Fact]
    public void TwoServersCannotUseTheSameWritableDataDirectory()
    {
        using var home = new TestWorkspace();
        using (var first = new AppDataLease(home.Paths.Data))
            Assert.Throws<InvalidOperationException>(() => new AppDataLease(home.Paths.Data));
        using var next = new AppDataLease(home.Paths.Data);
    }

    [Fact]
    public void VersionOneUpgradePreservesOwnerMediaAndPlaybackState()
    {
        using var home = new TestWorkspace();
        var data = Path.Combine(home.Root, "legacy");
        Directory.CreateDirectory(data);
        using (var db = new SqliteConnection($"Data Source={Path.Combine(data, "home.db")}"))
        {
            db.Open();
            using var stream = typeof(MediaStore).Assembly.GetManifestResourceStream("HomeServer.Infrastructure.Migrations.001_initial.sql")!;
            using var reader = new StreamReader(stream);
            using var command = db.CreateCommand();
            command.CommandText = reader.ReadToEnd() + """
                INSERT INTO owner VALUES(1,'Original owner','preserved-password-hash');
                INSERT INTO libraries VALUES('library','Original library','/media','/media','movies',0,'2026-01-01');
                INSERT INTO media(id,library_id,relative_path,path_key,title,search_text,kind,mime_type,size_bytes,modified_at,added_at,last_seen_scan)
                  VALUES('file','library','original.mp4','original.mp4','Original title','original title','video','video/mp4',42,'2026-01-01','2026-01-01','scan');
                INSERT INTO watch_state VALUES('file',17,0,'2026-01-01');
                PRAGMA user_version=1;
                """;
            command.ExecuteNonQuery();
        }
        var upgraded = new MediaStore(new HomePaths(data, Path.Combine(data, "thumbnails"), null));
        upgraded.Initialize();
        upgraded.Initialize();
        Assert.Equal("preserved-password-hash", upgraded.GetOwner()!.PasswordHash);
        Assert.Equal("Original owner", upgraded.GetOwner()!.DisplayName);
        Assert.Equal("Original title", upgraded.GetMedia("file")!.Title);
        Assert.Equal(17, upgraded.GetMedia("file")!.PositionSeconds);
        Assert.Empty(upgraded.GetDownloads());
        Assert.Null(upgraded.GetMedia("file")!.Content);
    }
}

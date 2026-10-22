ALTER TABLE dvr_recordings ADD COLUMN publication_json TEXT;
ALTER TABLE dvr_recordings ADD COLUMN request_hash TEXT;
ALTER TABLE dvr_recordings ADD COLUMN source_revision INTEGER;
ALTER TABLE dvr_recordings ADD COLUMN capture_attempts INTEGER NOT NULL DEFAULT 0;
UPDATE dvr_recordings SET source_revision=(SELECT s.revision FROM live_channels c JOIN live_sources s ON s.id=c.source_id WHERE c.id=dvr_recordings.channel_id) WHERE status IN ('scheduled','conflict');
UPDATE dvr_recordings SET status='finalizing',lease=NULL,revision=revision+1,message='Önceki geliştirme sürümünde kesilen kayıt parçaları toparlanacak.' WHERE status='recording';
ALTER TABLE receiver_copies ADD COLUMN artifact_fingerprint TEXT;
ALTER TABLE receiver_copies ADD COLUMN sha256 TEXT;
ALTER TABLE receiver_grants ADD COLUMN artifact_json TEXT;
ALTER TABLE receiver_grants ADD COLUMN device_json TEXT;
ALTER TABLE receiver_grants ADD COLUMN kind TEXT NOT NULL DEFAULT 'cast';
ALTER TABLE receiver_grants ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;
ALTER TABLE receiver_grants ADD COLUMN observation_json TEXT;
UPDATE receiver_grants SET closed=1;
CREATE UNIQUE INDEX receiver_target_active ON receiver_grants(target_id) WHERE target_id IS NOT NULL AND closed=0;
CREATE TABLE receiver_commands(receiver_id TEXT NOT NULL REFERENCES receiver_grants(id) ON DELETE CASCADE,command_id TEXT NOT NULL,request_hash TEXT NOT NULL,result_json TEXT,created_at TEXT NOT NULL,PRIMARY KEY(receiver_id,command_id));
CREATE TRIGGER playback_receiver_closed AFTER UPDATE OF status ON playback_leases WHEN NEW.status='closed' BEGIN
 UPDATE receiver_grants SET closed=1,revision=revision+1 WHERE playback_id=NEW.id AND closed=0;
END;
ALTER TABLE indexers ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;
CREATE TRIGGER source_configuration_changed AFTER UPDATE OF revision ON indexers BEGIN
 UPDATE release_results SET catalog_invalidated=1 WHERE indexer_id=NEW.id AND download_id IS NULL;
 UPDATE music_release_results SET expires_at=strftime('%Y-%m-%dT%H:%M:%f0000+00:00','now') WHERE indexer_id=NEW.id AND download_id IS NULL;
END;
CREATE TRIGGER source_transport_added AFTER INSERT ON source_transports BEGIN
 UPDATE indexers SET revision=revision+1,caps_json=NULL,retry_after=NULL WHERE id=NEW.indexer_id;
END;
CREATE TRIGGER source_transport_changed AFTER UPDATE OF revision ON source_transports BEGIN
 UPDATE indexers SET revision=revision+1,caps_json=NULL,retry_after=NULL WHERE id=NEW.indexer_id;
END;

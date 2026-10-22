ALTER TABLE libraries ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;
ALTER TABLE libraries ADD COLUMN options_json TEXT NOT NULL DEFAULT '{}';
ALTER TABLE libraries ADD COLUMN last_scan_at TEXT;
ALTER TABLE libraries ADD COLUMN next_scan_at TEXT;
ALTER TABLE media ADD COLUMN filesystem_id TEXT;
ALTER TABLE media ADD COLUMN excluded INTEGER NOT NULL DEFAULT 0;
CREATE INDEX media_filesystem_id ON media(library_id,filesystem_id);
CREATE TABLE local_metadata (
  media_id TEXT PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE,
  metadata_json TEXT NOT NULL,
  title TEXT,
  search_text TEXT NOT NULL DEFAULT '',
  updated_at TEXT NOT NULL,
  last_error TEXT
);
CREATE TABLE auto_match_jobs (
  media_id TEXT PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE,
  library_id TEXT NOT NULL REFERENCES libraries(id),
  library_revision INTEGER NOT NULL,
  size_bytes INTEGER NOT NULL,
  modified_at TEXT NOT NULL,
  metadata_key TEXT NOT NULL,
  status TEXT NOT NULL CHECK(status IN ('queued','running','matched','review','cancelled')),
  message TEXT NOT NULL DEFAULT '',
  next_attempt TEXT NOT NULL
);

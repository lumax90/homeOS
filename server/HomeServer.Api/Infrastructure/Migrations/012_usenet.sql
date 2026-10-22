-- The migration runner disables foreign keys for this table rebuild, then checks all foreign keys before committing.
CREATE TABLE downloads_new (
  id TEXT PRIMARY KEY,
  info_hash TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL,
  source_kind TEXT NOT NULL CHECK(source_kind IN ('torrent','magnet','nzb')),
  source TEXT NOT NULL,
  desired_state TEXT NOT NULL CHECK(desired_state IN ('running','paused','cancelled')),
  revision INTEGER NOT NULL DEFAULT 0,
  status TEXT NOT NULL,
  progress REAL NOT NULL DEFAULT 0,
  total_bytes INTEGER NOT NULL DEFAULT 0,
  download_speed INTEGER NOT NULL DEFAULT 0,
  upload_speed INTEGER NOT NULL DEFAULT 0,
  peers INTEGER NOT NULL DEFAULT 0,
  message TEXT NOT NULL DEFAULT '',
  imported_library_id TEXT REFERENCES libraries(id),
  scan_job_id TEXT REFERENCES jobs(id),
  imported_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
INSERT INTO downloads_new SELECT * FROM downloads;
DROP TABLE downloads;
ALTER TABLE downloads_new RENAME TO downloads;
CREATE INDEX downloads_state ON downloads(desired_state,created_at);

CREATE TABLE usenet_servers (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  host TEXT NOT NULL,
  port INTEGER NOT NULL,
  security TEXT NOT NULL CHECK(security IN ('tls','starttls','plain')),
  username TEXT NOT NULL,
  secret TEXT NOT NULL,
  connections INTEGER NOT NULL,
  priority INTEGER NOT NULL,
  enabled INTEGER NOT NULL,
  revision INTEGER NOT NULL DEFAULT 1,
  last_test TEXT,
  last_error TEXT,
  retry_at TEXT,
  downloaded_bytes INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE download_passwords(download_id TEXT PRIMARY KEY REFERENCES downloads(id),secret TEXT NOT NULL);

CREATE TABLE downloads (
  id TEXT PRIMARY KEY,
  info_hash TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL,
  source_kind TEXT NOT NULL CHECK(source_kind IN ('torrent','magnet')),
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
CREATE INDEX downloads_state ON downloads(desired_state,created_at);
CREATE TABLE download_hashes (
  hash TEXT PRIMARY KEY,
  download_id TEXT NOT NULL REFERENCES downloads(id) ON DELETE CASCADE
);

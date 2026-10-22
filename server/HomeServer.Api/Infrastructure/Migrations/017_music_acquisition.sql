CREATE TABLE music_release_results (
  id TEXT PRIMARY KEY,
  indexer_id TEXT NOT NULL REFERENCES indexers(id),
  title TEXT NOT NULL,
  protocol TEXT NOT NULL,
  size_bytes INTEGER,
  seeders INTEGER,
  evaluation_json TEXT NOT NULL,
  release_json TEXT NOT NULL,
  rules_json TEXT NOT NULL,
  protected_url TEXT NOT NULL,
  info_hash TEXT,
  expires_at TEXT NOT NULL,
  seed_rules_json TEXT NOT NULL DEFAULT '{}',
  download_id TEXT REFERENCES downloads(id)
);
CREATE TABLE music_download_links (
  download_id TEXT PRIMARY KEY REFERENCES downloads(id),
  result_id TEXT NOT NULL REFERENCES music_release_results(id),
  release_json TEXT NOT NULL,
  rules_json TEXT NOT NULL,
  import_rules_json TEXT,
  status TEXT NOT NULL DEFAULT 'waiting',
  message TEXT NOT NULL DEFAULT ''
);
CREATE TABLE music_download_files (
  download_id TEXT NOT NULL REFERENCES downloads(id),
  relative_path TEXT NOT NULL,
  size_bytes INTEGER NOT NULL,
  disc_number INTEGER NOT NULL,
  track_number INTEGER NOT NULL,
  PRIMARY KEY(download_id,relative_path)
);

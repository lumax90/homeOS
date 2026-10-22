ALTER TABLE libraries ADD COLUMN enabled INTEGER NOT NULL DEFAULT 1;
UPDATE libraries SET enabled=0 WHERE is_demo=1;
ALTER TABLE content ADD COLUMN imdb_id TEXT;
ALTER TABLE content ADD COLUMN tvdb_id INTEGER;
ALTER TABLE content ADD COLUMN rating REAL;
ALTER TABLE content ADD COLUMN runtime_minutes INTEGER;

CREATE TABLE quality_profiles (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  settings_json TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
INSERT INTO quality_profiles VALUES('balanced','Dengeli · 1080p',
  '{"resolutions":[1080,720],"maxSizeGiB":25,"minSeeders":1,"requiredTerms":[],"forbiddenTerms":[],"preferredTerms":["web-dl","bluray"],"allowUnknownResolution":false}',datetime('now'));
INSERT INTO quality_profiles VALUES('archive','Arşiv · 4K',
  '{"resolutions":[2160,1080],"maxSizeGiB":100,"minSeeders":1,"requiredTerms":[],"forbiddenTerms":[],"preferredTerms":["remux","bluray"],"allowUnknownResolution":false}',datetime('now'));

CREATE TABLE tracking (
  content_id TEXT PRIMARY KEY REFERENCES content(id),
  monitor_mode TEXT NOT NULL CHECK(monitor_mode IN ('all','future','missing','none')),
  quality_profile_id TEXT NOT NULL REFERENCES quality_profiles(id),
  requested_at TEXT NOT NULL,
  last_sync_at TEXT,
  next_sync_at TEXT NOT NULL,
  last_error TEXT,
  sync_state TEXT NOT NULL DEFAULT 'pending',
  revision INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE content_requests (
  id TEXT PRIMARY KEY,
  content_id TEXT NOT NULL REFERENCES content(id),
  requested_by TEXT NOT NULL DEFAULT '1',
  status TEXT NOT NULL CHECK(status IN ('approved','cancelled','fulfilled')),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE UNIQUE INDEX requests_active ON content_requests(content_id,requested_by) WHERE status IN ('approved','fulfilled');

CREATE TABLE indexers (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  endpoint TEXT NOT NULL,
  protocol TEXT NOT NULL CHECK(protocol IN ('torznab','newznab')),
  secret TEXT NOT NULL,
  enabled INTEGER NOT NULL DEFAULT 1,
  priority INTEGER NOT NULL DEFAULT 25,
  categories TEXT NOT NULL DEFAULT '',
  allowed_hosts TEXT NOT NULL DEFAULT '',
  caps_json TEXT,
  last_checked_at TEXT,
  last_success_at TEXT,
  last_error TEXT,
  retry_after TEXT,
  failure_count INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL
);
CREATE TABLE release_results (
  id TEXT PRIMARY KEY,
  indexer_id TEXT NOT NULL REFERENCES indexers(id),
  content_id TEXT NOT NULL REFERENCES content(id),
  season_number INTEGER,
  episode_number INTEGER,
  profile_id TEXT NOT NULL REFERENCES quality_profiles(id),
  release_guid TEXT NOT NULL,
  title TEXT NOT NULL,
  protocol TEXT NOT NULL,
  size_bytes INTEGER,
  seeders INTEGER,
  published_at TEXT,
  protected_url TEXT NOT NULL,
  info_hash TEXT,
  evaluation_json TEXT NOT NULL,
  profile_json TEXT NOT NULL,
  created_at TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  download_id TEXT REFERENCES downloads(id)
);
CREATE INDEX releases_content ON release_results(content_id,created_at);
CREATE TABLE acquisition_links (
  download_id TEXT PRIMARY KEY REFERENCES downloads(id),
  content_id TEXT NOT NULL REFERENCES content(id),
  season_number INTEGER,
  episode_number INTEGER,
  release_id TEXT REFERENCES release_results(id),
  status TEXT NOT NULL DEFAULT 'waiting',
  message TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL
);
CREATE TABLE acquisition_files (
  download_id TEXT NOT NULL REFERENCES downloads(id),
  relative_path TEXT NOT NULL,
  size_bytes INTEGER NOT NULL,
  episode_id TEXT REFERENCES episodes(id),
  PRIMARY KEY(download_id,relative_path)
);
CREATE INDEX tracking_due ON tracking(next_sync_at);
CREATE INDEX release_expiry ON release_results(expires_at);

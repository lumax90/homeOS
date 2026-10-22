CREATE TABLE automation_rules (
  content_id TEXT PRIMARY KEY REFERENCES content(id),
  settings_json TEXT NOT NULL,
  enabled INTEGER NOT NULL DEFAULT 0,
  auto_grab INTEGER NOT NULL DEFAULT 0,
  revision INTEGER NOT NULL DEFAULT 1,
  next_run TEXT NOT NULL,
  last_run TEXT,
  last_error TEXT,
  updated_by TEXT NOT NULL DEFAULT '1',
  updated_at TEXT NOT NULL
);
CREATE TABLE automation_jobs (
  id TEXT PRIMARY KEY,
  content_id TEXT NOT NULL REFERENCES content(id),
  season_number INTEGER,
  episode_number INTEGER,
  target_key TEXT NOT NULL,
  rule_revision INTEGER NOT NULL,
  settings_json TEXT NOT NULL,
  profile_json TEXT NOT NULL,
  status TEXT NOT NULL CHECK(status IN ('queued','running','waiting','suggested','grabbed','failed','cancelled','blocked','satisfied')),
  attempts INTEGER NOT NULL DEFAULT 0,
  due_at TEXT NOT NULL,
  lease_until TEXT,
  message TEXT NOT NULL DEFAULT '',
  release_id TEXT REFERENCES release_results(id),
  fingerprint TEXT,
  download_id TEXT REFERENCES downloads(id),
  grabbed_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE UNIQUE INDEX automation_active ON automation_jobs(content_id,target_key) WHERE status IN ('queued','running','waiting');
CREATE INDEX automation_due ON automation_jobs(status,due_at);
CREATE TABLE release_observations (
  fingerprint TEXT PRIMARY KEY,
  first_seen TEXT NOT NULL,
  last_seen TEXT NOT NULL
);
CREATE TABLE release_blocklist (
  content_id TEXT NOT NULL REFERENCES content(id),
  fingerprint TEXT NOT NULL,
  reason TEXT NOT NULL,
  blocked_at TEXT NOT NULL,
  PRIMARY KEY(content_id,fingerprint)
);
CREATE TABLE automation_suppressions (
  content_id TEXT NOT NULL REFERENCES content(id),
  target_key TEXT NOT NULL,
  revision INTEGER NOT NULL,
  reason TEXT NOT NULL,
  PRIMARY KEY(content_id,target_key)
);
CREATE TABLE rss_state (
  indexer_id TEXT PRIMARY KEY REFERENCES indexers(id),
  next_run TEXT NOT NULL,
  last_run TEXT,
  last_error TEXT
);

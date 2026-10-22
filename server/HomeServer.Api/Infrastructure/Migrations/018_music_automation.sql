CREATE TABLE music_monitors (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL REFERENCES users(id),
  user_version INTEGER NOT NULL,
  kind TEXT NOT NULL CHECK(kind IN ('artist','album')),
  mbid TEXT NOT NULL,
  name TEXT NOT NULL,
  settings_json TEXT NOT NULL,
  enabled INTEGER NOT NULL,
  revision INTEGER NOT NULL DEFAULT 1,
  cursor INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  next_run TEXT NOT NULL,
  next_search TEXT,
  last_run TEXT,
  last_error TEXT,
  UNIQUE(user_id,kind,mbid)
);
CREATE TABLE music_monitor_albums (
  monitor_id TEXT NOT NULL REFERENCES music_monitors(id) ON DELETE CASCADE,
  group_mbid TEXT NOT NULL,
  metadata_json TEXT NOT NULL,
  wanted INTEGER NOT NULL,
  wanted_override INTEGER,
  first_seen TEXT NOT NULL,
  PRIMARY KEY(monitor_id,group_mbid)
);
CREATE TABLE music_automation_jobs (
  id TEXT PRIMARY KEY,
  monitor_id TEXT NOT NULL REFERENCES music_monitors(id) ON DELETE CASCADE,
  group_mbid TEXT NOT NULL,
  title TEXT NOT NULL,
  revision INTEGER NOT NULL,
  status TEXT NOT NULL,
  attempts INTEGER NOT NULL DEFAULT 0,
  due_at TEXT NOT NULL,
  lease_until TEXT,
  message TEXT NOT NULL DEFAULT '',
  release_json TEXT,
  release_cursor INTEGER NOT NULL DEFAULT 0,
  result_id TEXT REFERENCES music_release_results(id),
  fingerprint TEXT,
  download_id TEXT REFERENCES downloads(id),
  grabbed_at TEXT,
  updated_at TEXT NOT NULL
);
CREATE UNIQUE INDEX music_automation_active ON music_automation_jobs(monitor_id,group_mbid) WHERE status IN ('queued','running','waiting','grabbed','blocked');
CREATE INDEX music_automation_due ON music_automation_jobs(status,due_at);
CREATE TABLE music_release_blocklist (
  group_mbid TEXT NOT NULL,
  fingerprint TEXT NOT NULL,
  reason TEXT NOT NULL,
  blocked_at TEXT NOT NULL,
  PRIMARY KEY(group_mbid,fingerprint)
);
ALTER TABLE music_release_results ADD COLUMN fingerprint TEXT NOT NULL DEFAULT '';
ALTER TABLE music_download_links ADD COLUMN upgrade_from_json TEXT;

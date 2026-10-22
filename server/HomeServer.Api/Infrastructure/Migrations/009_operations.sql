CREATE TABLE system_jobs (
  id TEXT PRIMARY KEY,
  kind TEXT NOT NULL CHECK(kind IN ('backup')),
  status TEXT NOT NULL CHECK(status IN ('queued','running','completed','failed')),
  message TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL,
  finished_at TEXT
);
ALTER TABLE media_analysis ADD COLUMN schema_version INTEGER NOT NULL DEFAULT 0;

CREATE TABLE subtitle_sync_jobs (
  id TEXT PRIMARY KEY,
  media_id TEXT NOT NULL REFERENCES media(id),
  track_id TEXT NOT NULL,
  media_fingerprint TEXT NOT NULL,
  subtitle_fingerprint TEXT NOT NULL,
  match_revision INTEGER NOT NULL,
  audio_stream INTEGER NOT NULL,
  revision INTEGER NOT NULL DEFAULT 1,
  status TEXT NOT NULL CHECK(status IN ('queued','running','proposed','applying','completed','failed','cancelled')),
  progress INTEGER NOT NULL DEFAULT 0,
  message TEXT NOT NULL DEFAULT '',
  input_json TEXT NOT NULL,
  result_json TEXT,
  subtitle_id TEXT REFERENCES managed_subtitles(id) ON DELETE SET NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE UNIQUE INDEX subtitle_sync_active ON subtitle_sync_jobs(media_id) WHERE status IN ('queued','running','proposed','applying');

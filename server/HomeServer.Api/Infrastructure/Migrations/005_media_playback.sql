CREATE TABLE media_analysis (
  media_id TEXT PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE,
  fingerprint TEXT NOT NULL,
  analysis_json TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE TABLE managed_subtitles (
  id TEXT PRIMARY KEY,
  media_id TEXT NOT NULL REFERENCES media(id) ON DELETE CASCADE,
  language TEXT NOT NULL,
  title TEXT NOT NULL,
  extension TEXT NOT NULL CHECK(extension IN ('srt','vtt','ass','ssa')),
  created_at TEXT NOT NULL
);
CREATE INDEX managed_subtitles_media ON managed_subtitles(media_id);
ALTER TABLE watch_state ADD COLUMN event_client TEXT;
ALTER TABLE watch_state ADD COLUMN event_sequence INTEGER;

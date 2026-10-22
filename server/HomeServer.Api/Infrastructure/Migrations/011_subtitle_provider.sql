ALTER TABLE managed_subtitles ADD COLUMN forced INTEGER NOT NULL DEFAULT 0;
ALTER TABLE managed_subtitles ADD COLUMN hearing_impaired INTEGER NOT NULL DEFAULT 0;
CREATE TABLE subtitle_candidates (
  id TEXT PRIMARY KEY,
  media_id TEXT NOT NULL REFERENCES media(id),
  content_id TEXT,
  fingerprint TEXT NOT NULL,
  provider_file_id INTEGER NOT NULL,
  language TEXT NOT NULL,
  candidate_json TEXT NOT NULL,
  managed_id TEXT NOT NULL,
  created_at TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  downloaded_at TEXT
);
CREATE INDEX subtitle_candidates_media ON subtitle_candidates(media_id,created_at);
CREATE UNIQUE INDEX subtitle_candidate_identity ON subtitle_candidates(media_id,fingerprint,provider_file_id);
CREATE TABLE subtitle_wanted (
  media_id TEXT NOT NULL REFERENCES media(id) ON DELETE CASCADE,
  language TEXT NOT NULL,
  profile_revision INTEGER NOT NULL,
  status TEXT NOT NULL DEFAULT 'waiting',
  message TEXT NOT NULL DEFAULT '',
  next_run TEXT NOT NULL,
  PRIMARY KEY(media_id,language)
);

ALTER TABLE music_albums ADD COLUMN artist_display TEXT NOT NULL DEFAULT '';
UPDATE music_albums SET artist_display=(SELECT name FROM music_artists WHERE id=music_albums.artist_id);
ALTER TABLE music_tracks ADD COLUMN credit_version INTEGER NOT NULL DEFAULT 0;
CREATE TABLE music_album_credits (
  album_id TEXT NOT NULL REFERENCES music_albums(id) ON DELETE CASCADE,
  source TEXT NOT NULL CHECK(source IN ('tags','match')), position INTEGER NOT NULL,
  artist_id TEXT NOT NULL REFERENCES music_artists(id), credited_name TEXT NOT NULL, join_phrase TEXT NOT NULL,
  PRIMARY KEY(album_id,source,position)
);
CREATE INDEX music_album_credit_artist ON music_album_credits(artist_id,album_id);
CREATE TABLE music_track_credits (
  media_id TEXT NOT NULL REFERENCES media(id) ON DELETE CASCADE,
  source TEXT NOT NULL CHECK(source IN ('tags','match')), position INTEGER NOT NULL,
  artist_id TEXT NOT NULL REFERENCES music_artists(id), credited_name TEXT NOT NULL, join_phrase TEXT NOT NULL,
  PRIMARY KEY(media_id,source,position)
);
CREATE INDEX music_track_credit_artist ON music_track_credits(artist_id,media_id);
CREATE TABLE music_video_links (
  id TEXT PRIMARY KEY, audio_media_id TEXT NOT NULL REFERENCES media(id) ON DELETE CASCADE,
  video_media_id TEXT NOT NULL REFERENCES media(id) ON DELETE CASCADE,
  recording_mbid TEXT, video_recording_mbid TEXT,
  revision INTEGER NOT NULL DEFAULT 1, linked_by TEXT NOT NULL REFERENCES users(id), created_at TEXT NOT NULL,
  UNIQUE(audio_media_id,video_media_id)
);
CREATE TABLE music_list_jobs (
  id TEXT PRIMARY KEY, user_id TEXT NOT NULL REFERENCES users(id), user_version INTEGER NOT NULL,
  source TEXT NOT NULL, source_id TEXT NOT NULL, kind TEXT NOT NULL, name TEXT NOT NULL DEFAULT '',
  entity_type TEXT, source_fingerprint TEXT, seeds_json TEXT, items_json TEXT NOT NULL DEFAULT '[]', seen_json TEXT NOT NULL DEFAULT '[]',
  revision INTEGER NOT NULL DEFAULT 1, lease TEXT, status TEXT NOT NULL, cursor INTEGER NOT NULL DEFAULT 0,
  total INTEGER, skipped INTEGER NOT NULL DEFAULT 0, attempts INTEGER NOT NULL DEFAULT 0,
  message TEXT NOT NULL DEFAULT '', receipt_json TEXT, request_hash TEXT,
  created_at TEXT NOT NULL, expires_at TEXT NOT NULL, next_run TEXT NOT NULL, updated_at TEXT NOT NULL
);
CREATE INDEX music_list_jobs_user ON music_list_jobs(user_id,created_at DESC);
CREATE INDEX music_list_jobs_due ON music_list_jobs(status,next_run);

CREATE TABLE music_release_cache (
  release_mbid TEXT PRIMARY KEY,
  group_mbid TEXT NOT NULL,
  metadata_json TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE TABLE music_album_matches (
  album_id TEXT PRIMARY KEY REFERENCES music_albums(id),
  release_mbid TEXT NOT NULL REFERENCES music_release_cache(release_mbid),
  title TEXT NOT NULL,
  artist TEXT NOT NULL,
  release_date TEXT,
  group_mbid TEXT NOT NULL,
  search_text TEXT NOT NULL DEFAULT '',
  matched_at TEXT NOT NULL
);
CREATE TABLE music_track_matches (
  media_id TEXT PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE,
  album_id TEXT NOT NULL REFERENCES music_albums(id),
  release_track_id TEXT NOT NULL,
  recording_mbid TEXT NOT NULL,
  disc_number INTEGER NOT NULL,
  track_number INTEGER NOT NULL,
  title TEXT NOT NULL,
  artist TEXT NOT NULL
  ,search_text TEXT NOT NULL DEFAULT ''
);
CREATE TABLE music_album_follows (
  user_id TEXT NOT NULL REFERENCES users(id),
  group_mbid TEXT NOT NULL,
  metadata_json TEXT NOT NULL,
  created_at TEXT NOT NULL,
  PRIMARY KEY(user_id,group_mbid)
);

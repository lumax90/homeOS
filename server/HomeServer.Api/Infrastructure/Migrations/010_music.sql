CREATE TABLE music_artists (
  id TEXT PRIMARY KEY,
  mbid TEXT,
  name TEXT NOT NULL,
  sort_name TEXT,
  country TEXT,
  disambiguation TEXT,
  source TEXT NOT NULL,
  local_key TEXT UNIQUE
);
CREATE UNIQUE INDEX music_artist_mbid ON music_artists(mbid) WHERE mbid IS NOT NULL;
CREATE TABLE music_albums (
  id TEXT PRIMARY KEY,
  artist_id TEXT NOT NULL REFERENCES music_artists(id),
  title TEXT NOT NULL,
  release_date TEXT,
  release_group_mbid TEXT,
  release_mbid TEXT,
  local_key TEXT NOT NULL UNIQUE,
  source TEXT NOT NULL
);
CREATE TABLE music_tracks (
  media_id TEXT PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE,
  album_id TEXT NOT NULL REFERENCES music_albums(id),
  title TEXT NOT NULL,
  artist TEXT NOT NULL,
  disc_number INTEGER NOT NULL,
  track_number INTEGER NOT NULL,
  recording_mbid TEXT,
  lyrics TEXT,
  replay_gain REAL,
  bit_depth INTEGER
  ,search_text TEXT NOT NULL DEFAULT ''
);
CREATE INDEX music_tracks_album ON music_tracks(album_id,disc_number,track_number);
CREATE TABLE music_follows (
  user_id TEXT NOT NULL,
  artist_mbid TEXT NOT NULL,
  name TEXT NOT NULL,
  created_at TEXT NOT NULL,
  PRIMARY KEY(user_id,artist_mbid)
);
CREATE TABLE playlists (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL,
  name TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE TABLE playlist_items (
  playlist_id TEXT NOT NULL REFERENCES playlists(id) ON DELETE CASCADE,
  position INTEGER NOT NULL,
  media_id TEXT NOT NULL REFERENCES media(id),
  PRIMARY KEY(playlist_id,position)
);
CREATE TABLE playback_queues (
  user_id TEXT PRIMARY KEY,
  items_json TEXT NOT NULL,
  current_index INTEGER NOT NULL,
  shuffle INTEGER NOT NULL,
  repeat_mode TEXT NOT NULL CHECK(repeat_mode IN ('off','one','all')),
  event_client TEXT,
  event_sequence INTEGER NOT NULL DEFAULT 0,
  updated_at TEXT NOT NULL
);
UPDATE media_analysis SET schema_version=0;
CREATE TABLE music_index_state(media_id TEXT PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE,next_attempt TEXT NOT NULL,last_error TEXT);

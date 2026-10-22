CREATE TABLE owner (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  display_name TEXT NOT NULL,
  password_hash TEXT NOT NULL
);

CREATE TABLE libraries (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  path TEXT NOT NULL,
  path_key TEXT NOT NULL UNIQUE,
  kind TEXT NOT NULL CHECK (kind IN ('movies', 'shows', 'music')),
  is_demo INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL
);

CREATE TABLE media (
  id TEXT PRIMARY KEY,
  library_id TEXT NOT NULL REFERENCES libraries(id) ON DELETE CASCADE,
  relative_path TEXT NOT NULL,
  path_key TEXT NOT NULL,
  title TEXT NOT NULL,
  search_text TEXT NOT NULL,
  kind TEXT NOT NULL CHECK (kind IN ('video', 'audio')),
  mime_type TEXT NOT NULL,
  size_bytes INTEGER NOT NULL,
  modified_at TEXT NOT NULL,
  added_at TEXT NOT NULL,
  duration_seconds REAL,
  width INTEGER,
  height INTEGER,
  video_codec TEXT,
  audio_codec TEXT,
  available INTEGER NOT NULL DEFAULT 1,
  has_thumbnail INTEGER NOT NULL DEFAULT 0,
  last_seen_scan TEXT NOT NULL,
  UNIQUE (library_id, path_key)
);
CREATE INDEX media_added ON media(added_at DESC, id);
CREATE INDEX media_library ON media(library_id, available);

CREATE TABLE watch_state (
  media_id TEXT PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE,
  position_seconds REAL NOT NULL DEFAULT 0 CHECK (position_seconds >= 0),
  watched INTEGER NOT NULL DEFAULT 0,
  updated_at TEXT NOT NULL
);

CREATE TABLE jobs (
  id TEXT PRIMARY KEY,
  library_id TEXT NOT NULL REFERENCES libraries(id) ON DELETE CASCADE,
  status TEXT NOT NULL CHECK (status IN ('queued', 'running', 'completed', 'failed', 'cancelled')),
  processed INTEGER NOT NULL DEFAULT 0,
  added INTEGER NOT NULL DEFAULT 0,
  attempt INTEGER NOT NULL DEFAULT 0,
  message TEXT NOT NULL,
  cancel_requested INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  started_at TEXT,
  finished_at TEXT
);
CREATE UNIQUE INDEX jobs_one_scan ON jobs(library_id) WHERE status IN ('queued', 'running');
CREATE INDEX jobs_queue ON jobs(status, created_at);

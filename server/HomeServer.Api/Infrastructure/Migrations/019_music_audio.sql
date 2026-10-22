CREATE TABLE music_playback_preferences(user_id TEXT PRIMARY KEY REFERENCES users(id),preferences_json TEXT NOT NULL);
CREATE TABLE music_favorites(user_id TEXT NOT NULL REFERENCES users(id),media_id TEXT NOT NULL REFERENCES media(id),created_at TEXT NOT NULL,PRIMARY KEY(user_id,media_id));
CREATE TABLE music_sonic_analysis (
  media_id TEXT PRIMARY KEY REFERENCES media(id),
  fingerprint TEXT NOT NULL,
  analysis_json TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE TABLE music_tasks (
  id TEXT PRIMARY KEY,
  media_id TEXT NOT NULL REFERENCES media(id),
  library_id TEXT NOT NULL REFERENCES libraries(id),
  kind TEXT NOT NULL CHECK(kind IN ('sonic','tags','restore-tags')),
  status TEXT NOT NULL,
  revision INTEGER NOT NULL DEFAULT 1,
  message TEXT NOT NULL DEFAULT '',
  plan_json TEXT,
  history_pruned INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
CREATE UNIQUE INDEX music_tasks_active ON music_tasks(media_id) WHERE status IN ('queued','running','publishing');
UPDATE media_analysis SET schema_version=0 WHERE media_id IN (SELECT media_id FROM music_tracks);

CREATE TABLE media_segment_sets (
 media_id TEXT PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE, fingerprint TEXT NOT NULL, match_revision INTEGER NOT NULL,
 revision INTEGER NOT NULL DEFAULT 1, segments_json TEXT NOT NULL, updated_by TEXT NOT NULL REFERENCES users(id), updated_at TEXT NOT NULL
);
CREATE TABLE player_preferences(user_id TEXT PRIMARY KEY REFERENCES users(id),preferences_json TEXT NOT NULL);

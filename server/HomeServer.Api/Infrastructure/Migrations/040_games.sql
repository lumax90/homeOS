CREATE TABLE game_user_state (
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    game_id TEXT NOT NULL,
    in_library INTEGER NOT NULL DEFAULT 0 CHECK(in_library IN (0,1)),
    favorite INTEGER NOT NULL DEFAULT 0 CHECK(favorite IN (0,1)),
    last_played_at TEXT,
    played_seconds INTEGER NOT NULL DEFAULT 0 CHECK(played_seconds>=0),
    save_version TEXT,
    save_json TEXT NOT NULL DEFAULT '{}' CHECK(json_valid(save_json)),
    save_revision INTEGER NOT NULL DEFAULT 0 CHECK(save_revision>=0),
    PRIMARY KEY(user_id,game_id)
);
CREATE TABLE game_sessions (
    id TEXT PRIMARY KEY,
    ticket_hash TEXT NOT NULL,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    login_id TEXT NOT NULL REFERENCES user_sessions(id) ON DELETE CASCADE,
    user_version INTEGER NOT NULL,
    game_id TEXT NOT NULL,
    game_version TEXT NOT NULL,
    created_at TEXT NOT NULL,
    last_seen_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    running INTEGER NOT NULL DEFAULT 0 CHECK(running IN (0,1)),
    closed_at TEXT
);
CREATE TABLE game_saves (
    user_id TEXT NOT NULL,
    game_id TEXT NOT NULL,
    game_version TEXT NOT NULL,
    files_json TEXT NOT NULL DEFAULT '{}' CHECK(json_valid(files_json)),
    revision INTEGER NOT NULL DEFAULT 0 CHECK(revision>=0),
    PRIMARY KEY(user_id,game_id,game_version),
    FOREIGN KEY(user_id,game_id) REFERENCES game_user_state(user_id,game_id) ON DELETE CASCADE
);
CREATE INDEX game_sessions_expiry ON game_sessions(expires_at) WHERE closed_at IS NULL;
CREATE UNIQUE INDEX game_sessions_user_active ON game_sessions(user_id) WHERE closed_at IS NULL;

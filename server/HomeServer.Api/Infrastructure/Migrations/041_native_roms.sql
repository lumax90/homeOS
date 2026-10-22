-- User-supplied ROM metadata only. Native execution and binary saves are a separate release gate.
CREATE TABLE game_roms (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    system TEXT NOT NULL CHECK(system='gba'),
    file_name TEXT NOT NULL,
    title TEXT NOT NULL,
    sha256 TEXT NOT NULL CHECK(length(sha256)=64),
    size_bytes INTEGER NOT NULL CHECK(size_bytes BETWEEN 192 AND 33554432),
    imported_at TEXT NOT NULL,
    UNIQUE(user_id,system,sha256)
);
CREATE INDEX game_roms_owner ON game_roms(user_id,imported_at);

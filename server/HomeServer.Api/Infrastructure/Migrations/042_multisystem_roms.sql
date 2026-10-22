-- Keep every existing ROM identity, owner and save partition unchanged.
CREATE TABLE game_roms_multisystem (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    system TEXT NOT NULL CHECK(length(system) BETWEEN 1 AND 40 AND system NOT GLOB '*[^a-z0-9_-]*'),
    file_name TEXT NOT NULL,
    title TEXT NOT NULL,
    sha256 TEXT NOT NULL CHECK(length(sha256)=64),
    size_bytes INTEGER NOT NULL CHECK(size_bytes BETWEEN 16 AND 68719476736),
    imported_at TEXT NOT NULL,
    UNIQUE(user_id,system,sha256)
);
INSERT INTO game_roms_multisystem SELECT id,user_id,system,file_name,title,sha256,size_bytes,imported_at FROM game_roms;
DROP TABLE game_roms;
ALTER TABLE game_roms_multisystem RENAME TO game_roms;
CREATE INDEX game_roms_owner ON game_roms(user_id,imported_at);

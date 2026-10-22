ALTER TABLE indexers ADD COLUMN provider_id TEXT;
ALTER TABLE indexers ADD COLUMN setup_pending INTEGER NOT NULL DEFAULT 0;
CREATE TABLE source_setups(
 id TEXT PRIMARY KEY,
 user_id TEXT NOT NULL REFERENCES users(id),
 login_id TEXT NOT NULL,
 user_version INTEGER NOT NULL,
 provider_id TEXT NOT NULL,
 definition_hash TEXT NOT NULL,
 request_hash TEXT NOT NULL,
 indexer_id TEXT REFERENCES indexers(id) ON DELETE SET NULL,
 existing_indexer_id TEXT REFERENCES indexers(id) ON DELETE SET NULL,
 expected_revision INTEGER,
 enabled INTEGER NOT NULL,
 status TEXT NOT NULL,
 revision INTEGER NOT NULL DEFAULT 1,
 lease TEXT,
 error TEXT,
 expires_at TEXT NOT NULL,
 created_at TEXT NOT NULL,
 updated_at TEXT NOT NULL
);
CREATE INDEX source_setups_pending ON source_setups(status,expires_at);

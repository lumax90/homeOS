CREATE TABLE source_adapters(id TEXT PRIMARY KEY,manifest_json TEXT NOT NULL,revision INTEGER NOT NULL DEFAULT 1,enabled INTEGER NOT NULL DEFAULT 1,updated_at TEXT NOT NULL);
CREATE TABLE source_transports(indexer_id TEXT PRIMARY KEY REFERENCES indexers(id) ON DELETE CASCADE,adapter_id TEXT,protected_settings TEXT NOT NULL,revision INTEGER NOT NULL DEFAULT 1);
CREATE TABLE source_request_history(id INTEGER PRIMARY KEY AUTOINCREMENT,indexer_id TEXT NOT NULL REFERENCES indexers(id) ON DELETE CASCADE,action TEXT NOT NULL,host TEXT NOT NULL,status INTEGER NOT NULL,duration_ms INTEGER NOT NULL,bytes INTEGER,created_at TEXT NOT NULL);
CREATE INDEX source_history_source ON source_request_history(indexer_id,id DESC);
CREATE TABLE source_request_budgets(indexer_id TEXT NOT NULL REFERENCES indexers(id) ON DELETE CASCADE,day TEXT NOT NULL,queries INTEGER NOT NULL DEFAULT 0,grabs INTEGER NOT NULL DEFAULT 0,PRIMARY KEY(indexer_id,day));

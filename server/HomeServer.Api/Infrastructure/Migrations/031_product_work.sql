CREATE TABLE product_jobs (
 id TEXT PRIMARY KEY, kind TEXT NOT NULL, user_id TEXT NOT NULL REFERENCES users(id), user_version INTEGER NOT NULL,
 scope_id TEXT, input_json TEXT NOT NULL, result_json TEXT, status TEXT NOT NULL, revision INTEGER NOT NULL DEFAULT 1,
 lease TEXT, progress INTEGER NOT NULL DEFAULT 0, message TEXT NOT NULL DEFAULT '', attempts INTEGER NOT NULL DEFAULT 0,
 due_at TEXT NOT NULL, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
);
CREATE INDEX product_jobs_due ON product_jobs(kind,status,due_at);
CREATE INDEX product_jobs_user ON product_jobs(user_id,created_at DESC);
CREATE TABLE catalog_feeds (
 id TEXT PRIMARY KEY, user_id TEXT NOT NULL REFERENCES users(id), provider TEXT NOT NULL, kind TEXT NOT NULL,
 external_id TEXT NOT NULL, name TEXT NOT NULL, collection_id TEXT NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
 enabled INTEGER NOT NULL DEFAULT 0, request_new INTEGER NOT NULL DEFAULT 0, role_filter TEXT NOT NULL DEFAULT 'all',
 profile_id TEXT NOT NULL, revision INTEGER NOT NULL DEFAULT 1, next_run TEXT NOT NULL, last_run TEXT, last_error TEXT,
 UNIQUE(user_id,provider,kind,external_id,collection_id)
);
CREATE TABLE catalog_feed_items (
 feed_id TEXT NOT NULL REFERENCES catalog_feeds(id) ON DELETE CASCADE, provider TEXT NOT NULL, kind TEXT NOT NULL,
 provider_id INTEGER NOT NULL, metadata_json TEXT NOT NULL, content_id TEXT REFERENCES content(id),
 excluded INTEGER NOT NULL DEFAULT 0, first_seen TEXT NOT NULL, last_seen TEXT NOT NULL,
 PRIMARY KEY(feed_id,provider,kind,provider_id)
);
ALTER TABLE collections ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;

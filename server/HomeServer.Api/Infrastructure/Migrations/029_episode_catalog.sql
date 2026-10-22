CREATE TABLE episodes_next (
  id TEXT PRIMARY KEY,
  content_id TEXT NOT NULL REFERENCES content(id),
  provider_id INTEGER NOT NULL,
  season_number INTEGER NOT NULL CHECK(season_number>=0),
  episode_number INTEGER NOT NULL CHECK(episode_number>=1),
  title TEXT NOT NULL, overview TEXT NOT NULL, air_date TEXT, still_url TEXT,
  runtime_minutes INTEGER, retired INTEGER NOT NULL DEFAULT 0,
  UNIQUE(content_id,id)
);
INSERT INTO episodes_next(id,content_id,provider_id,season_number,episode_number,title,overview,air_date,still_url)
  SELECT id,content_id,provider_id,season_number,episode_number,title,overview,air_date,still_url FROM episodes;
DROP TABLE episodes;
ALTER TABLE episodes_next RENAME TO episodes;
CREATE UNIQUE INDEX episodes_active_coordinate ON episodes(content_id,season_number,episode_number) WHERE retired=0;
CREATE INDEX episodes_external ON episodes(content_id,provider_id);
ALTER TABLE episode_aliases ADD COLUMN retired INTEGER NOT NULL DEFAULT 0;
CREATE TABLE episode_alias_targets (
  provider TEXT NOT NULL, provider_id INTEGER NOT NULL,
  episode_id TEXT NOT NULL REFERENCES episodes(id), position INTEGER NOT NULL,
  source_start REAL, source_end REAL,
  PRIMARY KEY(provider,provider_id,episode_id), UNIQUE(provider,provider_id,position),
  FOREIGN KEY(provider,provider_id) REFERENCES episode_aliases(provider,provider_id) ON DELETE CASCADE
);
INSERT INTO episode_alias_targets(provider,provider_id,episode_id,position) SELECT provider,provider_id,episode_id,0 FROM episode_aliases;
ALTER TABLE content_requests ADD COLUMN scope_explicit INTEGER NOT NULL DEFAULT 0;
CREATE TABLE request_episode_scope (
  request_id TEXT NOT NULL REFERENCES content_requests(id) ON DELETE CASCADE,
  episode_id TEXT NOT NULL REFERENCES episodes(id), PRIMARY KEY(request_id,episode_id)
);
CREATE INDEX request_episode_scope_episode ON request_episode_scope(episode_id);
CREATE TABLE episode_refresh_plans (
  id TEXT PRIMARY KEY, content_id TEXT NOT NULL REFERENCES content(id), catalog_revision INTEGER NOT NULL,
  actor_id TEXT NOT NULL REFERENCES users(id), metadata_json TEXT NOT NULL, episodes_json TEXT NOT NULL,
  created_at TEXT NOT NULL, expires_at TEXT NOT NULL, applied_at TEXT, result_json TEXT
);
CREATE INDEX episode_refresh_content ON episode_refresh_plans(content_id,created_at DESC);
CREATE TABLE catalog_episode_orders (
  id TEXT PRIMARY KEY, content_id TEXT NOT NULL REFERENCES content(id), name TEXT NOT NULL, kind TEXT NOT NULL,
  source TEXT NOT NULL, revision INTEGER NOT NULL DEFAULT 1, entries_json TEXT NOT NULL, updated_at TEXT NOT NULL
);
CREATE INDEX catalog_episode_orders_content ON catalog_episode_orders(content_id);
ALTER TABLE episode_numbering ADD COLUMN order_id TEXT REFERENCES catalog_episode_orders(id);
CREATE TABLE episode_boundary_jobs (
  id TEXT PRIMARY KEY, media_id TEXT NOT NULL REFERENCES media(id), user_id TEXT NOT NULL REFERENCES users(id),
  fingerprint TEXT NOT NULL, match_revision INTEGER NOT NULL, episodes_json TEXT NOT NULL,
  revision INTEGER NOT NULL DEFAULT 1, lease TEXT, status TEXT NOT NULL, progress INTEGER NOT NULL DEFAULT 0,
  message TEXT NOT NULL DEFAULT '', result_json TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL
);
CREATE UNIQUE INDEX episode_boundary_active ON episode_boundary_jobs(media_id) WHERE status IN ('queued','running');

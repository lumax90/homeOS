ALTER TABLE users ADD COLUMN maximum_age INTEGER;
ALTER TABLE users ADD COLUMN allow_unrated INTEGER NOT NULL DEFAULT 1;
ALTER TABLE users ADD COLUMN pin_hash TEXT;
ALTER TABLE content ADD COLUMN minimum_age INTEGER;
ALTER TABLE content ADD COLUMN age_label TEXT;
ALTER TABLE media ADD COLUMN minimum_age INTEGER;
ALTER TABLE media ADD COLUMN blocked INTEGER NOT NULL DEFAULT 0;
CREATE TABLE content_presentation (
  content_id TEXT PRIMARY KEY REFERENCES content(id),
  title TEXT,
  overview TEXT,
  minimum_age INTEGER,
  blocked INTEGER NOT NULL DEFAULT 0,
  tags_json TEXT NOT NULL DEFAULT '[]',
  search_text TEXT NOT NULL DEFAULT '',
  updated_at TEXT NOT NULL
);
CREATE TABLE personal_content (
  user_id TEXT NOT NULL REFERENCES users(id),
  content_id TEXT NOT NULL REFERENCES content(id),
  favorite INTEGER NOT NULL DEFAULT 0,
  rating INTEGER,
  PRIMARY KEY(user_id,content_id)
);
CREATE TABLE collections (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL REFERENCES users(id),
  name TEXT NOT NULL,
  shared INTEGER NOT NULL DEFAULT 0,
  updated_at TEXT NOT NULL
);
CREATE TABLE collection_items (
  collection_id TEXT NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
  content_id TEXT NOT NULL REFERENCES content(id),
  created_at TEXT NOT NULL,
  PRIMARY KEY(collection_id,content_id)
);
CREATE TABLE household_devices (
  id TEXT PRIMARY KEY,
  token_hash TEXT NOT NULL UNIQUE,
  owner_id TEXT NOT NULL REFERENCES users(id),
  owner_version INTEGER NOT NULL,
  users_json TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  failures INTEGER NOT NULL DEFAULT 0,
  retry_at TEXT
);
ALTER TABLE user_sessions ADD COLUMN household_device_id TEXT REFERENCES household_devices(id) ON DELETE CASCADE;

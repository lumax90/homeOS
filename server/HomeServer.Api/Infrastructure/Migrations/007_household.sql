CREATE TABLE users (
  id TEXT PRIMARY KEY,
  display_name TEXT NOT NULL,
  name_key TEXT NOT NULL UNIQUE,
  password_hash TEXT NOT NULL,
  role TEXT NOT NULL CHECK(role IN ('admin','member','viewer')),
  enabled INTEGER NOT NULL DEFAULT 1,
  all_libraries INTEGER NOT NULL DEFAULT 1,
  can_discover INTEGER NOT NULL DEFAULT 1,
  can_acquire INTEGER NOT NULL DEFAULT 0,
  auto_approve INTEGER NOT NULL DEFAULT 0,
  monthly_request_limit INTEGER NOT NULL DEFAULT 20,
  version INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL
);
INSERT INTO users(id,display_name,name_key,password_hash,role,can_acquire,auto_approve,monthly_request_limit,created_at)
  SELECT '1',display_name,lower(display_name),password_hash,'admin',1,1,0,datetime('now') FROM owner;
CREATE TABLE user_libraries (
  user_id TEXT NOT NULL REFERENCES users(id),
  library_id TEXT NOT NULL REFERENCES libraries(id),
  PRIMARY KEY(user_id,library_id)
);
CREATE TABLE user_sessions (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL REFERENCES users(id),
  user_version INTEGER NOT NULL,
  device TEXT NOT NULL,
  created_at TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  revoked_at TEXT
);
CREATE INDEX sessions_user ON user_sessions(user_id,expires_at);
CREATE TABLE user_invitations (
  id TEXT PRIMARY KEY,
  token_hash TEXT NOT NULL UNIQUE,
  settings_json TEXT NOT NULL,
  created_at TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  used_at TEXT
);
CREATE TABLE user_watch_state (
  user_id TEXT NOT NULL,
  media_id TEXT NOT NULL REFERENCES media(id) ON DELETE CASCADE,
  position_seconds REAL NOT NULL,
  watched INTEGER NOT NULL,
  updated_at TEXT NOT NULL,
  event_client TEXT,
  event_sequence INTEGER,
  PRIMARY KEY(user_id,media_id)
);
INSERT INTO user_watch_state SELECT '1',media_id,position_seconds,watched,updated_at,event_client,event_sequence FROM watch_state;
CREATE TABLE content_watch_state (
  user_id TEXT NOT NULL,
  content_id TEXT NOT NULL REFERENCES content(id),
  episode_key TEXT NOT NULL,
  position_seconds REAL NOT NULL,
  watched INTEGER NOT NULL,
  updated_at TEXT NOT NULL,
  event_client TEXT,
  event_sequence INTEGER,
  source_media_id TEXT,
  PRIMARY KEY(user_id,content_id,episode_key)
);
INSERT OR IGNORE INTO content_watch_state
  SELECT '1',mm.content_id,COALESCE(mm.episode_id,''),w.position_seconds,w.watched,w.updated_at,w.event_client,w.event_sequence,w.media_id
  FROM watch_state w JOIN media_matches mm ON mm.media_id=w.media_id ORDER BY w.updated_at DESC;

DROP INDEX requests_active;
ALTER TABLE content_requests RENAME TO content_requests_v04;
CREATE TABLE content_requests (
  id TEXT PRIMARY KEY,
  content_id TEXT NOT NULL REFERENCES content(id),
  requested_by TEXT NOT NULL,
  status TEXT NOT NULL CHECK(status IN ('pending','approved','rejected','cancelled','fulfilled')),
  scope_key TEXT NOT NULL DEFAULT 'all',
  season_number INTEGER,
  episode_number INTEGER,
  monitor_mode TEXT NOT NULL DEFAULT 'all',
  quality_profile_id TEXT NOT NULL DEFAULT 'balanced' REFERENCES quality_profiles(id),
  note TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);
INSERT INTO content_requests(id,content_id,requested_by,status,monitor_mode,quality_profile_id,created_at,updated_at)
  SELECT r.id,r.content_id,r.requested_by,r.status,COALESCE(t.monitor_mode,'all'),COALESCE(t.quality_profile_id,'balanced'),r.created_at,r.updated_at
  FROM content_requests_v04 r LEFT JOIN tracking t ON t.content_id=r.content_id;
DROP TABLE content_requests_v04;
CREATE UNIQUE INDEX requests_active ON content_requests(content_id,requested_by,scope_key) WHERE status IN ('pending','approved','fulfilled');
CREATE INDEX requests_user_time ON content_requests(requested_by,created_at);
CREATE TABLE request_events (
  id TEXT PRIMARY KEY,
  request_id TEXT NOT NULL REFERENCES content_requests(id),
  actor_id TEXT NOT NULL,
  status TEXT NOT NULL,
  note TEXT NOT NULL,
  created_at TEXT NOT NULL
);
CREATE TABLE notifications (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL,
  kind TEXT NOT NULL,
  title TEXT NOT NULL,
  message TEXT NOT NULL,
  content_id TEXT REFERENCES content(id),
  created_at TEXT NOT NULL,
  read_at TEXT,
  dedup_key TEXT NOT NULL,
  UNIQUE(user_id,dedup_key)
);
CREATE INDEX notifications_user_time ON notifications(user_id,created_at);

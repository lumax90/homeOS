CREATE TABLE playback_devices (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  maximum_streams INTEGER CHECK(maximum_streams BETWEEN 0 AND 16),
  revision INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL,
  last_seen_at TEXT NOT NULL
);
ALTER TABLE user_sessions ADD COLUMN playback_device_id TEXT REFERENCES playback_devices(id);
INSERT INTO playback_devices(id,name,created_at,last_seen_at)
  SELECT id,device,created_at,created_at FROM user_sessions;
UPDATE user_sessions SET playback_device_id=id;

CREATE TABLE user_playback_limits (
  user_id TEXT PRIMARY KEY REFERENCES users(id),
  maximum_streams INTEGER NOT NULL CHECK(maximum_streams BETWEEN 0 AND 16),
  maximum_devices INTEGER NOT NULL CHECK(maximum_devices BETWEEN 1 AND 16),
  revision INTEGER NOT NULL
);
CREATE TABLE playback_intents (
  login_id TEXT NOT NULL,
  client_id TEXT NOT NULL,
  revision INTEGER NOT NULL,
  closed INTEGER NOT NULL,
  request_hash TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  PRIMARY KEY(login_id,client_id)
);
CREATE TABLE playback_leases (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL REFERENCES users(id),
  login_id TEXT NOT NULL,
  device_id TEXT NOT NULL REFERENCES playback_devices(id),
  client_id TEXT NOT NULL,
  client_revision INTEGER NOT NULL,
  media_id TEXT NOT NULL,
  fingerprint TEXT NOT NULL,
  match_revision INTEGER NOT NULL,
  title TEXT NOT NULL,
  mode TEXT NOT NULL,
  status TEXT NOT NULL CHECK(status IN ('starting','active','closed')),
  created_at TEXT NOT NULL,
  last_seen_at TEXT NOT NULL,
  paused_at TEXT,
  position_seconds REAL NOT NULL DEFAULT 0,
  ended_at TEXT,
  end_reason TEXT,
  session_json TEXT,
  global_revision INTEGER NOT NULL,
  user_revision INTEGER NOT NULL,
  device_revision INTEGER NOT NULL
);
CREATE UNIQUE INDEX playback_active_client ON playback_leases(login_id,client_id) WHERE status<>'closed';
CREATE INDEX playback_active_user ON playback_leases(user_id,status);
CREATE INDEX playback_active_device ON playback_leases(device_id,status);
CREATE INDEX playback_history ON playback_leases(created_at DESC);
CREATE INDEX playback_closed_history ON playback_leases(ended_at DESC) WHERE status='closed';
CREATE TABLE playback_media (
  lease_id TEXT NOT NULL REFERENCES playback_leases(id) ON DELETE CASCADE,
  media_id TEXT NOT NULL,
  fingerprint TEXT NOT NULL,
  match_revision INTEGER NOT NULL,
  frames INTEGER,
  touched_at TEXT NOT NULL,
  PRIMARY KEY(lease_id,media_id)
);

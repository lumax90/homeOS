CREATE TABLE watch_rooms (
  id TEXT PRIMARY KEY,
  owner_id TEXT NOT NULL REFERENCES users(id),
  owner_login_id TEXT NOT NULL,
  mode TEXT NOT NULL CHECK(mode IN ('together','remote')),
  status TEXT NOT NULL CHECK(status IN ('paused','waiting','playing','closed')),
  revision INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  document_json TEXT NOT NULL CHECK(json_valid(document_json))
);
CREATE INDEX watch_rooms_active ON watch_rooms(status,owner_id);
CREATE TABLE watch_room_clients (
  login_id TEXT NOT NULL,
  client_id TEXT NOT NULL,
  revision INTEGER NOT NULL,
  closed INTEGER NOT NULL,
  room_id TEXT REFERENCES watch_rooms(id) ON DELETE SET NULL,
  member_id TEXT,
  request_hash TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  PRIMARY KEY(login_id,client_id)
);
CREATE TABLE watch_room_commands (
  room_id TEXT NOT NULL REFERENCES watch_rooms(id) ON DELETE CASCADE,
  command_id TEXT NOT NULL,
  user_id TEXT NOT NULL REFERENCES users(id),
  login_id TEXT NOT NULL,
  client_id TEXT NOT NULL,
  request_hash TEXT NOT NULL,
  action TEXT NOT NULL,
  revision INTEGER NOT NULL,
  created_at TEXT NOT NULL,
  PRIMARY KEY(room_id,command_id)
);
CREATE INDEX watch_room_commands_recent ON watch_room_commands(room_id,revision DESC);
ALTER TABLE playback_leases ADD COLUMN watch_room_id TEXT;
ALTER TABLE playback_leases ADD COLUMN watch_member_id TEXT;
ALTER TABLE playback_leases ADD COLUMN watch_epoch INTEGER;
CREATE INDEX playback_watch_member ON playback_leases(watch_member_id) WHERE status<>'closed';

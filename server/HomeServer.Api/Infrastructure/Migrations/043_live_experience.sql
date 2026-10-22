ALTER TABLE live_sources ADD COLUMN public_preset TEXT;
CREATE UNIQUE INDEX live_public_preset ON live_sources(public_preset) WHERE public_preset IS NOT NULL;
ALTER TABLE live_channels ADD COLUMN protected_headers TEXT;
ALTER TABLE live_channels ADD COLUMN protected_logo TEXT;
ALTER TABLE live_channels ADD COLUMN search_text TEXT NOT NULL DEFAULT '';
UPDATE live_channels SET search_text=lower(name || ' ' || group_name);
CREATE TABLE live_channel_preferences(
  user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  channel_id TEXT NOT NULL REFERENCES live_channels(id) ON DELETE CASCADE,
  favorite INTEGER NOT NULL DEFAULT 0,
  last_watched_at TEXT,
  PRIMARY KEY(user_id,channel_id)
);
CREATE INDEX live_preferences_recent ON live_channel_preferences(user_id,last_watched_at);
ALTER TABLE live_sessions ADD COLUMN client_id TEXT;
ALTER TABLE live_sessions ADD COLUMN client_revision INTEGER NOT NULL DEFAULT 0;
CREATE INDEX live_session_client ON live_sessions(login_id,client_id,status);

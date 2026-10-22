CREATE TABLE user_offline_policies (
  user_id TEXT PRIMARY KEY REFERENCES users(id),
  enabled INTEGER NOT NULL,
  maximum_items INTEGER NOT NULL,
  maximum_bytes INTEGER NOT NULL,
  lease_days INTEGER NOT NULL,
  revision INTEGER NOT NULL
);
CREATE TABLE offline_exports (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL REFERENCES users(id),
  device_id TEXT NOT NULL REFERENCES playback_devices(id),
  media_id TEXT NOT NULL REFERENCES media(id),
  client_id TEXT NOT NULL,
  source_fingerprint TEXT NOT NULL,
  source_catalog_bytes INTEGER NOT NULL,
  source_catalog_modified TEXT NOT NULL,
  match_revision INTEGER NOT NULL,
  user_version INTEGER NOT NULL,
  policy_revision INTEGER NOT NULL,
  input_json TEXT NOT NULL,
  status TEXT NOT NULL CHECK(status IN ('queued','running','ready','failed','cancelled','revoked')),
  revision INTEGER NOT NULL DEFAULT 1,
  lease TEXT,
  progress INTEGER NOT NULL DEFAULT 0,
  message TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  reserved_bytes INTEGER NOT NULL,
  manifest_json TEXT,
  last_sequence INTEGER NOT NULL DEFAULT 0,
  UNIQUE(user_id,device_id,client_id)
);
CREATE INDEX offline_exports_due ON offline_exports(status,created_at);
CREATE INDEX offline_exports_user ON offline_exports(user_id,device_id,status);
CREATE TABLE offline_progress_receipts (
  grant_id TEXT NOT NULL REFERENCES offline_exports(id) ON DELETE CASCADE,
  event_id TEXT NOT NULL,
  sequence INTEGER NOT NULL,
  request_hash TEXT NOT NULL,
  result_json TEXT NOT NULL,
  created_at TEXT NOT NULL,
  PRIMARY KEY(grant_id,event_id),
  UNIQUE(grant_id,sequence)
);

CREATE TABLE media_subtitle_overrides (
  media_id TEXT PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE,
  profile_json TEXT,
  revision INTEGER NOT NULL DEFAULT 1,
  hold_upgrades INTEGER NOT NULL DEFAULT 0,
  updated_at TEXT NOT NULL
);
ALTER TABLE managed_subtitles ADD COLUMN origin TEXT NOT NULL DEFAULT 'upload';
ALTER TABLE managed_subtitles ADD COLUMN score INTEGER;
ALTER TABLE managed_subtitles ADD COLUMN provider_file_id INTEGER;
ALTER TABLE managed_subtitles ADD COLUMN retired INTEGER NOT NULL DEFAULT 0;
ALTER TABLE managed_subtitles ADD COLUMN media_fingerprint TEXT;
ALTER TABLE managed_subtitles ADD COLUMN match_revision INTEGER;
ALTER TABLE managed_subtitles ADD COLUMN payload_hash TEXT;
ALTER TABLE managed_subtitles ADD COLUMN supersedes_id TEXT REFERENCES managed_subtitles(id) ON DELETE SET NULL;
ALTER TABLE managed_subtitles ADD COLUMN sync_job_id TEXT;
UPDATE managed_subtitles SET origin='provider',provider_file_id=(SELECT provider_file_id FROM subtitle_candidates WHERE managed_id=managed_subtitles.id LIMIT 1),
  score=(SELECT json_extract(candidate_json,'$.score') FROM subtitle_candidates WHERE managed_id=managed_subtitles.id LIMIT 1),
  media_fingerprint=(SELECT fingerprint FROM subtitle_candidates WHERE managed_id=managed_subtitles.id LIMIT 1),
  match_revision=(SELECT association_revision FROM subtitle_candidates WHERE managed_id=managed_subtitles.id LIMIT 1)
WHERE EXISTS(SELECT 1 FROM subtitle_candidates WHERE managed_id=managed_subtitles.id);
ALTER TABLE subtitle_candidates ADD COLUMN connection_revision INTEGER NOT NULL DEFAULT 0;
ALTER TABLE subtitle_candidates ADD COLUMN protected_ticket TEXT;
ALTER TABLE subtitle_candidates ADD COLUMN ticket_expires_at TEXT;
ALTER TABLE subtitle_wanted ADD COLUMN override_revision INTEGER NOT NULL DEFAULT 0;
ALTER TABLE subtitle_wanted ADD COLUMN connection_revision INTEGER NOT NULL DEFAULT 0;
ALTER TABLE subtitle_wanted ADD COLUMN policy_json TEXT;
ALTER TABLE subtitle_wanted ADD COLUMN lease_token TEXT;
ALTER TABLE subtitle_wanted ADD COLUMN lease_until TEXT;
ALTER TABLE subtitle_wanted ADD COLUMN attempts INTEGER NOT NULL DEFAULT 0;
CREATE TABLE subtitle_download_attempts (
  id TEXT PRIMARY KEY,
  candidate_id TEXT NOT NULL,
  media_id TEXT NOT NULL REFERENCES media(id),
  provider_file_id INTEGER NOT NULL,
  connection_revision INTEGER NOT NULL,
  automatic INTEGER NOT NULL,
  created_at TEXT NOT NULL
);
CREATE INDEX subtitle_attempt_day ON subtitle_download_attempts(automatic,created_at);

ALTER TABLE downloads ADD COLUMN owner_id TEXT REFERENCES users(id);
ALTER TABLE downloads ADD COLUMN budget_origin TEXT NOT NULL DEFAULT 'legacy';
ALTER TABLE downloads ADD COLUMN budget_generation INTEGER NOT NULL DEFAULT 0;
ALTER TABLE downloads ADD COLUMN budget_charged_bytes INTEGER NOT NULL DEFAULT 0;
ALTER TABLE downloads ADD COLUMN budget_wait_reason TEXT;
ALTER TABLE downloads ADD COLUMN budget_retry_at TEXT;
ALTER TABLE automation_jobs ADD COLUMN budget_wait_reason TEXT;
ALTER TABLE music_automation_jobs ADD COLUMN budget_wait_reason TEXT;
CREATE INDEX downloads_owner ON downloads(owner_id,archived,desired_state);

-- Historical explicit automation authors are recoverable; ordinary legacy transfers have no inferred owner.
UPDATE downloads SET owner_id=(SELECT m.user_id FROM music_automation_jobs j JOIN music_monitors m ON m.id=j.monitor_id
  JOIN users u ON u.id=m.user_id WHERE j.download_id=downloads.id LIMIT 1),budget_origin='music-automation'
  WHERE EXISTS(SELECT 1 FROM music_automation_jobs j JOIN music_monitors m ON m.id=j.monitor_id JOIN users u ON u.id=m.user_id WHERE j.download_id=downloads.id);
UPDATE downloads SET owner_id=(SELECT r.updated_by FROM automation_jobs j JOIN automation_rules r ON r.content_id=j.content_id AND r.revision=j.rule_revision
  JOIN users u ON u.id=r.updated_by WHERE j.download_id=downloads.id LIMIT 1),budget_origin='video-automation'
  WHERE owner_id IS NULL AND EXISTS(SELECT 1 FROM automation_jobs j JOIN automation_rules r ON r.content_id=j.content_id AND r.revision=j.rule_revision
    JOIN users u ON u.id=r.updated_by WHERE j.download_id=downloads.id);
-- Already transmitted bytes are historical, never invented as current-month usage.
UPDATE downloads SET budget_charged_bytes=CASE WHEN received_bytes>0 OR imported_at IS NOT NULL THEN MAX(total_bytes,received_bytes) ELSE 0 END;

CREATE TABLE user_download_budgets (
  user_id TEXT PRIMARY KEY REFERENCES users(id), enabled INTEGER NOT NULL,
  monthly_acquisitions INTEGER, monthly_payload_bytes INTEGER, maximum_payload_bytes INTEGER,
  maximum_concurrent INTEGER NOT NULL, revision INTEGER NOT NULL
);
CREATE TABLE download_budget_periods (
  user_id TEXT NOT NULL REFERENCES users(id), period TEXT NOT NULL,
  acquisitions INTEGER NOT NULL DEFAULT 0, committed_bytes INTEGER NOT NULL DEFAULT 0,
  received_bytes INTEGER NOT NULL DEFAULT 0, uploaded_bytes INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY(user_id,period)
);
CREATE TABLE download_budget_runs (
  id TEXT PRIMARY KEY, download_id TEXT NOT NULL REFERENCES downloads(id), download_revision INTEGER NOT NULL,
  user_id TEXT REFERENCES users(id), policy_revision INTEGER NOT NULL, generation INTEGER NOT NULL,
  phase TEXT NOT NULL CHECK(phase IN ('transfer','seeding','closed')),
  period TEXT NOT NULL, reserved_bytes INTEGER NOT NULL DEFAULT 0, payload_bytes INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL, lease_until TEXT NOT NULL, ended_at TEXT
);
CREATE UNIQUE INDEX download_budget_active_job ON download_budget_runs(download_id) WHERE phase<>'closed';
CREATE INDEX download_budget_active_user ON download_budget_runs(user_id,phase);
CREATE TABLE download_budget_events (
  id TEXT PRIMARY KEY, user_id TEXT NOT NULL REFERENCES users(id), download_id TEXT REFERENCES downloads(id),
  title TEXT NOT NULL, action TEXT NOT NULL, bytes INTEGER NOT NULL, period TEXT NOT NULL, created_at TEXT NOT NULL
);
CREATE INDEX download_budget_events_user ON download_budget_events(user_id,created_at DESC);
CREATE TABLE download_counter_receipts (
  id TEXT PRIMARY KEY, budget_run_id TEXT NOT NULL REFERENCES download_budget_runs(id) ON DELETE CASCADE,
  uploaded INTEGER NOT NULL DEFAULT 0, received INTEGER NOT NULL DEFAULT 0, seed_seconds INTEGER NOT NULL DEFAULT 0
);

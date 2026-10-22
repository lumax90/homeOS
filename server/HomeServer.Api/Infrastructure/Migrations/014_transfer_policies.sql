ALTER TABLE downloads ADD COLUMN archived INTEGER NOT NULL DEFAULT 0;
ALTER TABLE downloads ADD COLUMN priority INTEGER NOT NULL DEFAULT 25;
ALTER TABLE downloads ADD COLUMN uploaded_bytes INTEGER NOT NULL DEFAULT 0;
ALTER TABLE downloads ADD COLUMN received_bytes INTEGER NOT NULL DEFAULT 0;
ALTER TABLE downloads ADD COLUMN seed_seconds INTEGER NOT NULL DEFAULT 0;
ALTER TABLE downloads ADD COLUMN telemetry_run TEXT;
ALTER TABLE downloads ADD COLUMN run_uploaded INTEGER NOT NULL DEFAULT 0;
ALTER TABLE downloads ADD COLUMN run_received INTEGER NOT NULL DEFAULT 0;
ALTER TABLE downloads ADD COLUMN run_seed_seconds INTEGER NOT NULL DEFAULT 0;
ALTER TABLE downloads ADD COLUMN payload_cleaned_at TEXT;
ALTER TABLE release_results ADD COLUMN seed_rules_json TEXT NOT NULL DEFAULT '{}';
CREATE TABLE transfer_policies (
  download_id TEXT PRIMARY KEY REFERENCES downloads(id),
  settings_json TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

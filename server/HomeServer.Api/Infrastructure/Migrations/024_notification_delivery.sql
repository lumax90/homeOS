CREATE TABLE notification_channels (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL REFERENCES users(id),
  name TEXT NOT NULL,
  kind TEXT NOT NULL CHECK(kind IN ('webhook','smtp')),
  enabled INTEGER NOT NULL,
  events_json TEXT NOT NULL,
  maximum_hourly_attempts INTEGER NOT NULL,
  revision INTEGER NOT NULL,
  target TEXT NOT NULL,
  protected_secrets TEXT NOT NULL,
  created_at TEXT NOT NULL,
  dropped INTEGER NOT NULL DEFAULT 0,
  deleted INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE notification_deliveries (
  id TEXT PRIMARY KEY,
  channel_id TEXT NOT NULL REFERENCES notification_channels(id),
  channel_revision INTEGER NOT NULL,
  user_id TEXT NOT NULL REFERENCES users(id),
  user_version INTEGER NOT NULL,
  notification_id TEXT REFERENCES notifications(id) ON DELETE CASCADE,
  kind TEXT NOT NULL,
  title TEXT NOT NULL,
  message TEXT NOT NULL,
  content_id TEXT,
  created_at TEXT NOT NULL,
  is_test INTEGER NOT NULL DEFAULT 0,
  status TEXT NOT NULL CHECK(status IN ('queued','sending','sent','failed','cancelled')),
  attempts INTEGER NOT NULL DEFAULT 0,
  revision INTEGER NOT NULL DEFAULT 1,
  lease TEXT,
  next_attempt_at TEXT NOT NULL,
  finished_at TEXT,
  result TEXT NOT NULL DEFAULT '',
  UNIQUE(channel_id,notification_id)
);
CREATE INDEX notification_delivery_due ON notification_deliveries(status,next_attempt_at);
CREATE INDEX notification_delivery_channel ON notification_deliveries(channel_id,status);
CREATE INDEX notification_delivery_history ON notification_deliveries(finished_at DESC);
CREATE TABLE notification_delivery_attempts (
  delivery_id TEXT NOT NULL REFERENCES notification_deliveries(id) ON DELETE CASCADE,
  channel_id TEXT NOT NULL REFERENCES notification_channels(id),
  number INTEGER NOT NULL,
  started_at TEXT NOT NULL,
  finished_at TEXT,
  result TEXT NOT NULL DEFAULT 'Gönderim başladı.',
  PRIMARY KEY(delivery_id,number)
);
CREATE INDEX notification_hourly_attempts ON notification_delivery_attempts(channel_id,started_at);
CREATE TRIGGER notification_outbox AFTER INSERT ON notifications BEGIN
  UPDATE notification_channels SET dropped=MIN(dropped+1,2147483647) WHERE enabled=1 AND deleted=0 AND user_id=NEW.user_id
    AND EXISTS(SELECT 1 FROM json_each(events_json) e WHERE e.value=NEW.kind)
    AND (SELECT COUNT(*) FROM notification_deliveries d WHERE d.channel_id=notification_channels.id AND d.status IN ('queued','sending'))>=1000;
  INSERT INTO notification_deliveries(id,channel_id,channel_revision,user_id,user_version,notification_id,kind,title,message,content_id,created_at,status,next_attempt_at)
    SELECT lower(hex(randomblob(16))),c.id,c.revision,NEW.user_id,u.version,NEW.id,NEW.kind,NEW.title,NEW.message,NEW.content_id,NEW.created_at,'queued',NEW.created_at
    FROM notification_channels c JOIN users u ON u.id=c.user_id
    WHERE c.enabled=1 AND c.deleted=0 AND u.enabled=1 AND c.user_id=NEW.user_id
      AND EXISTS(SELECT 1 FROM json_each(c.events_json) e WHERE e.value=NEW.kind)
      AND (SELECT COUNT(*) FROM notification_deliveries d WHERE d.channel_id=c.id AND d.status IN ('queued','sending'))<1000;
END;

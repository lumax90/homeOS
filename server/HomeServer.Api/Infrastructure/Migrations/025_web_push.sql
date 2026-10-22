DROP TRIGGER notification_outbox;
CREATE TABLE notification_channels_v021 (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL REFERENCES users(id),
  name TEXT NOT NULL,
  kind TEXT NOT NULL CHECK(kind IN ('webhook','smtp','push')),
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
INSERT INTO notification_channels_v021 SELECT * FROM notification_channels;
DROP TABLE notification_channels;
ALTER TABLE notification_channels_v021 RENAME TO notification_channels;

CREATE TABLE browser_push_subscriptions (
  channel_id TEXT PRIMARY KEY REFERENCES notification_channels(id) ON DELETE CASCADE,
  user_id TEXT NOT NULL REFERENCES users(id),
  login_id TEXT NOT NULL,
  device_id TEXT NOT NULL UNIQUE REFERENCES playback_devices(id),
  endpoint_hash TEXT NOT NULL UNIQUE,
  keys_hash TEXT NOT NULL,
  binding_id TEXT NOT NULL,
  key_revision INTEGER NOT NULL,
  expires_at TEXT NOT NULL
);
CREATE INDEX browser_push_login ON browser_push_subscriptions(login_id);
CREATE TRIGGER notification_outbox AFTER INSERT ON notifications BEGIN
  UPDATE notification_channels SET dropped=MIN(dropped+1,2147483647) WHERE enabled=1 AND deleted=0 AND user_id=NEW.user_id
    AND EXISTS(SELECT 1 FROM json_each(events_json) e WHERE e.value=NEW.kind)
    AND (SELECT COUNT(*) FROM notification_deliveries d WHERE d.channel_id=notification_channels.id AND d.status IN ('queued','sending'))>=1000;
  INSERT INTO notification_deliveries(id,channel_id,channel_revision,user_id,user_version,notification_id,kind,title,message,content_id,created_at,status,next_attempt_at)
    SELECT lower(hex(randomblob(16))),c.id,c.revision,NEW.user_id,u.version,NEW.id,NEW.kind,NEW.title,NEW.message,NEW.content_id,NEW.created_at,'queued',NEW.created_at
    FROM notification_channels c JOIN users u ON u.id=c.user_id
    WHERE c.enabled=1 AND c.deleted=0 AND u.enabled=1 AND c.user_id=NEW.user_id
      AND EXISTS(SELECT 1 FROM json_each(c.events_json) e WHERE e.value=NEW.kind)
      AND (c.kind<>'push' OR EXISTS(SELECT 1 FROM browser_push_subscriptions p JOIN user_sessions s ON s.id=p.login_id
        WHERE p.channel_id=c.id AND p.expires_at>NEW.created_at AND s.revoked_at IS NULL AND s.expires_at>NEW.created_at AND s.user_version=u.version))
      AND (SELECT COUNT(*) FROM notification_deliveries d WHERE d.channel_id=c.id AND d.status IN ('queued','sending'))<1000;
END;

CREATE TRIGGER browser_push_closed AFTER UPDATE OF deleted ON notification_channels WHEN NEW.kind='push' AND NEW.deleted=1 BEGIN
  DELETE FROM browser_push_subscriptions WHERE channel_id=NEW.id;
END;
CREATE TRIGGER browser_push_login_revoked AFTER UPDATE OF revoked_at ON user_sessions WHEN NEW.revoked_at IS NOT NULL AND OLD.revoked_at IS NULL BEGIN
  UPDATE notification_deliveries SET status='cancelled',revision=revision+1,lease=NULL,finished_at=NEW.revoked_at,result='Push giriş oturumu iptal edildi.'
    WHERE channel_id IN (SELECT channel_id FROM browser_push_subscriptions WHERE login_id=NEW.id) AND status IN ('queued','sending');
  UPDATE notification_delivery_attempts SET finished_at=NEW.revoked_at,result='Push giriş oturumu iptal edildi.'
    WHERE channel_id IN (SELECT channel_id FROM browser_push_subscriptions WHERE login_id=NEW.id) AND finished_at IS NULL;
  UPDATE notification_channels SET enabled=0,deleted=1,protected_secrets='',revision=revision+1 WHERE id IN (SELECT channel_id FROM browser_push_subscriptions WHERE login_id=NEW.id);
END;
CREATE TRIGGER browser_push_login_deleted AFTER DELETE ON user_sessions BEGIN
  UPDATE notification_deliveries SET status='cancelled',revision=revision+1,lease=NULL,finished_at=strftime('%Y-%m-%dT%H:%M:%fZ','now'),result='Push giriş oturumu kaldırıldı.'
    WHERE channel_id IN (SELECT channel_id FROM browser_push_subscriptions WHERE login_id=OLD.id) AND status IN ('queued','sending');
  UPDATE notification_delivery_attempts SET finished_at=strftime('%Y-%m-%dT%H:%M:%fZ','now'),result='Push giriş oturumu kaldırıldı.'
    WHERE channel_id IN (SELECT channel_id FROM browser_push_subscriptions WHERE login_id=OLD.id) AND finished_at IS NULL;
  UPDATE notification_channels SET enabled=0,deleted=1,protected_secrets='',revision=revision+1 WHERE id IN (SELECT channel_id FROM browser_push_subscriptions WHERE login_id=OLD.id);
END;

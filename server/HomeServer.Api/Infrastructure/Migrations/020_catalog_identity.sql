ALTER TABLE content ADD COLUMN merged_into TEXT REFERENCES content(id);
ALTER TABLE content ADD COLUMN catalog_revision INTEGER NOT NULL DEFAULT 1;
ALTER TABLE media_matches ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;
ALTER TABLE media ADD COLUMN association_revision INTEGER NOT NULL DEFAULT 0;
UPDATE media SET association_revision=1 WHERE id IN (SELECT media_id FROM media_matches);
CREATE TRIGGER association_insert AFTER INSERT ON media_matches BEGIN UPDATE media SET association_revision=association_revision+1 WHERE id=NEW.media_id; END;
CREATE TRIGGER association_update AFTER UPDATE ON media_matches BEGIN UPDATE media SET association_revision=association_revision+1 WHERE id=NEW.media_id; END;
CREATE TRIGGER association_delete AFTER DELETE ON media_matches BEGIN UPDATE media SET association_revision=association_revision+1 WHERE id=OLD.media_id; END;
ALTER TABLE content_requests ADD COLUMN merged_into TEXT REFERENCES content_requests(id);
ALTER TABLE release_results ADD COLUMN catalog_invalidated INTEGER NOT NULL DEFAULT 0;
ALTER TABLE release_results ADD COLUMN catalog_revision INTEGER NOT NULL DEFAULT 1;
ALTER TABLE acquisition_links ADD COLUMN episodes_json TEXT;
ALTER TABLE acquisition_links ADD COLUMN numbering_json TEXT;
ALTER TABLE acquisition_files ADD COLUMN episodes_json TEXT;
ALTER TABLE acquisition_files ADD COLUMN part_number INTEGER;
CREATE TABLE content_aliases (
  provider TEXT NOT NULL,
  kind TEXT NOT NULL,
  provider_id INTEGER NOT NULL,
  content_id TEXT NOT NULL REFERENCES content(id),
  metadata_json TEXT NOT NULL DEFAULT '{}',
  search_text TEXT NOT NULL DEFAULT '',
  linked_at TEXT NOT NULL,
  PRIMARY KEY(provider,kind,provider_id)
);
INSERT INTO content_aliases(provider,kind,provider_id,content_id,metadata_json,search_text,linked_at)
  SELECT provider,kind,provider_id,id,json_object('title',title,'originalTitle',original_title),search_text,updated_at FROM content;
CREATE INDEX content_alias_target ON content_aliases(content_id);
CREATE TABLE content_names(content_id TEXT NOT NULL REFERENCES content(id),name TEXT NOT NULL,source TEXT NOT NULL,search_text TEXT NOT NULL DEFAULT '',PRIMARY KEY(content_id,name));
ALTER TABLE subtitle_candidates ADD COLUMN association_revision INTEGER NOT NULL DEFAULT 0;
UPDATE subtitle_candidates SET association_revision=COALESCE((SELECT association_revision FROM media WHERE media.id=subtitle_candidates.media_id),0);
CREATE TABLE episode_aliases (
  provider TEXT NOT NULL,
  provider_id INTEGER NOT NULL,
  content_id TEXT NOT NULL REFERENCES content(id),
  episode_id TEXT NOT NULL REFERENCES episodes(id),
  PRIMARY KEY(provider,provider_id)
);
INSERT OR IGNORE INTO episode_aliases SELECT c.provider,e.provider_id,e.content_id,e.id FROM episodes e JOIN content c ON c.id=e.content_id;
CREATE TABLE catalog_merge_log (
  id TEXT PRIMARY KEY,
  target_id TEXT NOT NULL REFERENCES content(id),
  source_id TEXT REFERENCES content(id),
  snapshot_json TEXT NOT NULL,
  actor_id TEXT NOT NULL,
  created_at TEXT NOT NULL
);
CREATE TABLE media_episode_segments (
  media_id TEXT NOT NULL REFERENCES media_matches(media_id) ON DELETE CASCADE,
  episode_id TEXT NOT NULL REFERENCES episodes(id),
  position INTEGER NOT NULL,
  start_seconds REAL,
  end_seconds REAL,
  PRIMARY KEY(media_id,episode_id),
  UNIQUE(media_id,position)
);
CREATE INDEX media_episode_membership ON media_episode_segments(episode_id,media_id);
INSERT INTO media_episode_segments SELECT media_id,episode_id,0,0,NULL FROM media_matches WHERE episode_id IS NOT NULL;
CREATE TABLE movie_editions (
  id TEXT PRIMARY KEY,
  content_id TEXT NOT NULL REFERENCES content(id),
  name TEXT NOT NULL,
  revision INTEGER NOT NULL DEFAULT 1,
  part_count INTEGER NOT NULL
);
CREATE TABLE movie_parts (
  edition_id TEXT NOT NULL REFERENCES movie_editions(id) ON DELETE CASCADE,
  media_id TEXT NOT NULL UNIQUE REFERENCES media_matches(media_id) ON DELETE CASCADE,
  part_number INTEGER NOT NULL,
  PRIMARY KEY(edition_id,part_number)
);
CREATE TRIGGER media_match_episode_insert AFTER INSERT ON media_matches WHEN NEW.episode_id IS NOT NULL BEGIN
  INSERT INTO media_episode_segments(media_id,episode_id,position,start_seconds) VALUES(NEW.media_id,NEW.episode_id,0,0);
END;
CREATE TRIGGER media_match_episode_update AFTER UPDATE OF content_id,episode_id ON media_matches
WHEN OLD.content_id<>NEW.content_id OR OLD.episode_id IS NOT NEW.episode_id BEGIN
  DELETE FROM media_episode_segments WHERE media_id=NEW.media_id;
  INSERT INTO media_episode_segments(media_id,episode_id,position,start_seconds) SELECT NEW.media_id,NEW.episode_id,0,0 WHERE NEW.episode_id IS NOT NULL;
  DELETE FROM movie_parts WHERE media_id=NEW.media_id AND (NEW.episode_id IS NOT NULL OR edition_id IN (SELECT id FROM movie_editions WHERE content_id<>NEW.content_id));
END;
CREATE TABLE episode_numbering (
  content_id TEXT PRIMARY KEY REFERENCES content(id),
  mode TEXT NOT NULL CHECK(mode IN ('seasonal','daily','absolute')),
  source TEXT NOT NULL,
  revision INTEGER NOT NULL DEFAULT 1
);
CREATE TABLE episode_order_entries (
  content_id TEXT NOT NULL REFERENCES episode_numbering(content_id) ON DELETE CASCADE,
  number INTEGER NOT NULL,
  episode_id TEXT NOT NULL REFERENCES episodes(id),
  PRIMARY KEY(content_id,number),
  UNIQUE(content_id,episode_id)
);

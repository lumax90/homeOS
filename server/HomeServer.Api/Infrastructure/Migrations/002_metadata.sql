CREATE TABLE content (
  id TEXT PRIMARY KEY,
  provider TEXT NOT NULL CHECK (provider IN ('tmdb','tvmaze')),
  provider_id INTEGER NOT NULL,
  kind TEXT NOT NULL CHECK (kind IN ('movie','show')),
  title TEXT NOT NULL,
  original_title TEXT NOT NULL,
  search_text TEXT NOT NULL,
  overview TEXT NOT NULL,
  release_date TEXT,
  poster_url TEXT,
  backdrop_url TEXT,
  genres_json TEXT NOT NULL DEFAULT '[]',
  provider_url TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  UNIQUE(provider,kind,provider_id)
);
CREATE TABLE episodes (
  id TEXT PRIMARY KEY,
  content_id TEXT NOT NULL REFERENCES content(id),
  provider_id INTEGER NOT NULL,
  season_number INTEGER NOT NULL CHECK(season_number>=0),
  episode_number INTEGER NOT NULL CHECK(episode_number>=1),
  title TEXT NOT NULL,
  overview TEXT NOT NULL,
  air_date TEXT,
  still_url TEXT,
  UNIQUE(content_id,season_number,episode_number),
  UNIQUE(content_id,id)
);
CREATE TABLE media_matches (
  media_id TEXT PRIMARY KEY REFERENCES media(id) ON DELETE CASCADE,
  content_id TEXT NOT NULL REFERENCES content(id),
  episode_id TEXT,
  edition TEXT NOT NULL DEFAULT '',
  matched_at TEXT NOT NULL,
  FOREIGN KEY(content_id,episode_id) REFERENCES episodes(content_id,id)
);
CREATE INDEX media_matches_content ON media_matches(content_id,episode_id);
CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);

# HTTP API — first vertical slice

Base path: `/api`. JSON uses camelCase. IDs are opaque strings. The application and its browser client use the same origin.

The 0.2 metadata, matching, artwork and download endpoints are documented in [milestone-02.md](milestone-02.md). Catalog items now include `sourceTitle` and nullable `content`; download history never exposes stored torrent/magnet source credentials.

The current 0.3 discovery, tracking, paginated episode/calendar, source, quality and acquisition endpoints are documented in [milestone-03.md](milestone-03.md).

0.4.1 adds authenticated track/subtitle, playback session/segment and explicit import-profile review endpoints: [milestone-04.md](milestone-04.md).

## Session

- `GET /health`: process health and product version.
- `GET /session`: `{ configured, authenticated, preview, displayName, csrfToken }`.
- `POST /setup`: `{ displayName, password }`. Atomically creates the single local owner and signs in. Requires a CSRF token even before setup. Subsequent setup requests return 409.
- `POST /auth/login`: the same credential shape.
- `POST /auth/logout`: signs out.

All mutations require `X-CSRF-TOKEN` from `/session`, together with the session/CSRF cookies. Refresh the token after login, setup or logout because the principal changed. Credentials are rate-limited. Public health/session responses do not expose local media paths.

## Authenticated catalog and jobs

| Method | Path | Meaning |
| --- | --- | --- |
| GET | `/system` | Real media-tool capabilities, version and server paths |
| GET | `/stats` | Catalog counts, readable size and active job count |
| GET | `/libraries` | Configured source roots |
| POST | `/libraries` | `{ name, path, kind }`; creates root and queues a scan |
| POST | `/libraries/{id}/scan` | Returns 202 and a persistent job; active jobs are deduplicated |
| GET | `/catalog?q=&kind=&library=&offset=0&limit=60` | Paged search; maximum page size 120 |
| GET | `/continue` | Unfinished playback positions |
| GET | `/media/{id}` | File-derived content detail |
| GET | `/media/{id}/thumbnail` | Authenticated generated JPEG |
| GET | `/media/{id}/stream` | Original file with range/conditional request support |
| PUT | `/media/{id}/progress` | `{ positionSeconds, durationSeconds }` |
| GET | `/jobs` | Latest 50 durable scan jobs |
| POST | `/jobs/{id}/cancel` | Persistent cancellation request |
| GET | `/events` | SSE `ready` / `change` events and idle heartbeats |

Library kinds are `movies`, `shows`, `music`; a catalog file's kind is `video` or `audio`. 0.3 adds file-independent content tracking and provider episode refresh. Synthetic libraries are archived from the normal catalog and playback lookup.

Jobs transition through `queued → running → completed/failed/cancelled`. Interrupted running jobs are recovered to the queue unless cancellation was requested. Each execution attempt has a fresh scan token. The completion transaction marks unseen files unavailable, retaining their identity and playback history.

Media-tool availability means the executables were found. Actual probing/thumbnail failures leave the affected fields unavailable; no generated metadata is fabricated. 0.4.1 supports direct/remux/software HLS decisions and verified SDR codec conversions. HDR/GPU/device support is not implied by executable discovery; see the explicit playback matrix and acceptance gates.

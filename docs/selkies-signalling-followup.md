# Selkies browser signalling route — SELKIES-SIGNAL-02

Date **2026-10-26**. Requirements **GAM-05/10/19, CLI-01/05, OPS-08**. User selected the already-published Selkies backend and reported a viewer that briefly appears/reloads without opening the game. Console evidence shows repeated WebSocket failures and a **404** at `/game-native/{session}/selkies/api/webrtc/signaling/`.

**Status: local correction compiled; not published or device-accepted.** Existing media/HLS/home/controller follow-ups and game/save/runtime identities are preserved. No household file, session or setting was modified.

## Cause and correction

- The pinned **Selkies 2.1.0** browser builds its signalling URL with a **trailing slash**. Its own server accepts both forms (`signaling{slash:/?}`). Home's asset allowlist and WebSocket branch accepted only `api/webrtc/signaling` without the slash. The browser request therefore stopped at Home's 404 allowlist rejection, before reaching upstream signalling.
- `NativeSelkiesProxy.CanonicalAsset` now maps **only that exact single-slash alias** to the existing canonical endpoint. No general `TrimEnd`, arbitrary API access, traversal normalization or filesystem route expansion is added.
- Both forms still require the current authenticated account and active game/login lease. A WebSocket additionally requires the exact session Origin, uses the same viewer token/role upstream, has the same reader/message limits and is cancelled with the session. Desktop input, token management, file transfer and other management APIs remain blocked.
- Selkies's browser also probes this endpoint with a **normal GET before reconnecting/reloading**. Previously Home fabricated 403 for every non-WebSocket signalling request. It now forwards this authenticated read to the existing upstream handler, preserving **204** when ready, **409** when inactive and **503 / Retry-After** while starting/stopping.
- The existing raw protocol verification definition now uses the actual released browser URL with its trailing slash; the earlier slashless probe did not expose this browser/proxy discrepancy. Its long multi-system suite was **not rerun**.

## Other console messages

- The unrecognized `Permissions-Policy` features are browser header warnings, not the source of this exact Home allowlist 404. No HTTPS/TLS change, browser security override, GPU claim or gamepad-policy bypass is made to address it.
- `share-modal.js` and its null `addEventListener` error were not found in Home's web sources or as a file in the pinned Selkies source tree inspected for this report. Its full script URL/origin is not supplied; it is not attributed to Home/Selkies or declared fixed without evidence. No dummy DOM, global error suppression or unreviewed sharing-script modification is added. The confirmed signalling defect is addressed first.

## Verification and next acceptance

- Backend and the retained regression definitions compiled using the installed .NET 10 SDK: **0 warnings / 0 errors**. New contracts cover both exact signalling forms, rejection of repeated-slash/traversal/encoded aliases, and continued anonymous denial.
- Per the user's **2026-10-25** direction, no automated browser suite, repeated backend/native test tour, benchmark, VM/game launch or internet-media acquisition was run. Compilation is not claimed as proof that the household device now plays.
- Publication/deployment has not occurred. The current household **0.45.0** image still has the route defect; retrying that unchanged image is not acceptance of this local correction. After a separately authorised fixed-image publication/update, the user will try Selkies again and report any remaining issue.

## References

Reviewed **2026-10-26**. Context7 quota was exhausted; actual pinned upstream source and Home's current implementation were inspected.

- [Released browser URL and HTTP retry probe](https://github.com/selkies-project/selkies/blob/6ee68a7a815956e6745e916741f13335e0b3ff53/addons/selkies-web-core/selkies-wr-core.js), around lines 3107–3166.
- [Released optional-slash server route and 204/409/503 handling](https://github.com/selkies-project/selkies/blob/6ee68a7a815956e6745e916741f13335e0b3ff53/src/selkies/webrtc_mode.py), around lines 3540–3585.
- [Existing Selkies integration/scope](selkies-streaming.md), `server/HomeServer.Api/Gaming/NativeSelkiesProxy.cs`, `NativeSelkiesSession.cs`, `tests/HomeServer.Tests/NativeSelkiesTests.cs`.

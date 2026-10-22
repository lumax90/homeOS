# Games library / input transport — 0.35.0

2026-10-20 · GAM-04/05/10/11, GAMING-LIBRARY-02. Kullanıcı daha düşük latency için incelemeyi ve ardından Games redesign'ını istedi; automated tests/browser/screenshot yerine kod/build, gerçek cihaz kabulü kullanıcıda.

## ROM-first UI

- Ana sekme **My ROM library / ROM kütüphanem**; gerçek owned/in-library ROM'lar, mevcut source/save/favorite kayıtları korunur. Ücretsiz/browser games ayrı **Free games / Ücretsiz oyunlar** sekmesinde, mevcut kurulu web oyunları silinmez veya yeniden indirilmez.
- Movie/content-first library yönünde portrait covers, soft artwork-backed featured own game, temiz type/spacing, responsive grids. System/category/A–Z/no grouping + recent/name/play-time + favorites/search. Groups actual ROM `system` field'den; current importer/runtime hâlâ GBA. DS/PS/Wii label map, bu sistemlerin Home'a emülasyon entegrasyonu olduğu anlamına gelmez; hayali oyun/kategori üretmez.
- Ad/kategori owner ROM presentation'ında düzenlenir; checksummed source ROM veya emulator save değiştirilmez. Favorites tekrar kullanılmakta.

## Anlaşılır settings

Uzun “first prototype local package already verified” ana ekran metni çıkarıldı. Kısa açıklama: kendi ROM'u Home'a ekle; bu host'ta GBA mevcut. Actual current host/client/owner restrictions **Server and support information** altında saklanır ama gizlenmez.

Settings artık sadece enable/disable değil:

- Per-user/per-base-path client preference: original aspect, integer pixel scaling, fill; smoothing; volume. Actual video element'te uygulanır.
- Server next-launch preferences: stream quality (`auto`/quality/lower bandwidth), actual mGBA 0.10.5 low-pass audio filter, solar sensor **0–10**. Pinned installed core option definitions incelendi; desteklenmeyen GPU/color/transcoding seçeneği sunulmaz.
- Revision-bound, bounded state JSON; owner/local command; active game altında native options değişmez; source/save silinmez. Power enable/disable advanced support kısmında ayrı.

## Latency code

Native input persistent same-origin **WebSocket** yolu: authenticated owner/local cookie + exact Origin; ticket first bounded message'da (URL'de değil), game/login lease validation, one stream/session, text messages **1KiB**, per-message rate **120/s**, cancellation/deadman/neutral cleanup. WS media/codec motoru değildir: ASP.NET Core transport'tur. Browser WS hazırsa input edge gönderimi **16ms**, fallback HTTP **20ms**; native relay **16ms**. FIFO press/release ve pause/epoch safety korunur; security RPC hot input loop'a geri dönmez.

HTTP fallback eski cihaz/baglantı için kalır. Paket/clipboard/global desktop yok. Bu değişiklik ölçülmüş browser improvement iddiası değildir; kullanıcı latency kabulünü yapar. Existing video jitter/decode/minimum-buffer kontrolleri korunur; şu tur tests/screenshots yapılmaz.

## Real covers, not fabricated thumbnails

Optional **libretro-thumbnails** exact HTTPS server/index. Yeni user ROM import cover lookup'ı existing durable/bounded `rom-artwork` job'una koyar. Old ROM'lar için **Find missing covers** explicit command; lookup returns only same-system named covers. Exact normalized title unique ise alınır; ambiguous/missing → **Choose cover** search and selection. Filename-based lookup checksum identity iddiası değildir. User PNG upload <=4MiB de var; source/provenance/copyright attribution korunur.

Assets content-addressed private owner/game area; normal ROM/core/game saves ve other library untouched. Owner required for writes/selection; bounded HTTP reads, fixed HTTPS origin, no redirects, PNG dimensions/size limits; no arbitrary browser URL fetch. Data licensing `[dependencies](dependencies.md)`'de kaydedildi: art copyright respective publishers; **no blanket open-data licence**, public distribution gate açık, no cover dataset bundled. Development verification hiçbir internet cover/ROM/media acquisition başlatmaz; provider actual acceptance user command'a bağlıdır.

## Build/status

Backend/TypeScript compile and frontend/worker/PWA build check only; user specifically declined lengthy automated test/screenshot work. Old tests were not run or claimed to validate new library/WS/provider changes. Browser/device/metadata source acceptance is still user-owned. Signed publication/data verification recorded after apply; product scope/release gates remain open.

Current sources/types checked 2026-10-20: HeroUI v3 controlled Modal/Button, installed declarations; ASP.NET Core WebSockets/Origin/bounded receive guidance via Context7; mGBA 0.10.5 installed `libretro_core_options.h`; libretro-thumbnails README/naming/index docs/copyright inventory.

## Canlı 0.35.0

Backend build **0 warnings/0 errors**, TypeScript/worker/frontend/PWA succeeded; **no automated test suites/browser/screenshot acceptance** this turn, as requested. A missing accessible label on two source selects was caught by the UI build gate and added, not bypassed.

Normal signed update API published **0.35.0 / schema41**, PID **19976**, same trusted key. Package **907 files / 116018307 bytes**; prior **0.34.4**, backup **166aa77ce1bd450d8f61921f0c3977cf**. `artifacts/update-live-0.35.0.json`.

Read-only runtime/user-data preservation check `artifacts/live-native-v035.json` matches prior capture: native **enabled/available=true**, original native core state partition unchanged, ROM/game-save/media/request/personal/collection digests identical, **0 FK violations**, all three downloads still paused, no game/acquisition/resume command. Client **`/assets/index-B0v92oCM.js`**, PWA **0b4496fbee82c67f**.

No provider fetch/cover/game was exercised by development verification. Actual cover availability, WebSocket input/device latency, display/core controls and visual acceptance are for the user. This release does not claim new DS/PS/Wii emulation, fresh-host automatic installation, public artwork licence, or completion of the whole product.

# Home içinden GBA etkinleştirme — 0.34.1

Kontrol **2026-10-20**, şema **41**. GAM-04/05/10/11, GAMING-SETUP-01. Bu, **bu hazırlanmış Windows sunucusunda / sahip hesabında / localhost tarayıcısında** native GBA deneme dilimidir; bütün ürün/TV/Linux/public distribution tamamlandı değildir.

## Kullanıcı akışı

1. Sunucu bilgisayarında Home → **Games / Oyunlar → Library / Kütüphanem**.
2. **Enable emulation / Emülasyonu etkinleştir**: local prototype kapsamını okuyup checkbox ile kabul et. Etkinleştirme **ROM indirmez, oyun açmaz, firewall/driver değiştirmez**.
3. **Add ROM / ROM ekle** ile kullanma hakkın olan tek, uncompressed **`.gba`, en fazla 32 MiB** dosyayı seç; kopyası profile bağlı managed ROM alanına girer, kaynak dosya değişmez.
4. Detay sayfasında **Play / Oyna**. Native core sunucuda; görüntü/ses/kontrol aynı Home oynatıcısında. Emülatör UI'si veya özel masaüstü capture yok.
5. Klavye yön tuşları / J-K / Enter-Shift / Q-W veya standard gamepad. Menü **Esc / Start+Select**; devam için gamepad tuş/stick neutral gate. Audio engellenirse sound icon'a kullanıcı gesture'ıyla tıkla.
6. **Save and exit / Kaydet ve çık**; emülasyonu kapatmak için önce oyun oturumunu kapat. Native ROM/save silinmez; diğer oyunların CAS/save/return-position akışı korunur.

**Gerçek ROM/browser/audio/controller/device kabulü kullanıcıya aittir.** Geliştirme verification'ında internetten oyun/ROM/BIOS edinilmedi, normal katalogda synthetic oyun oluşturulmadı ve otomatik browser suite/screenshot çalıştırılmadı.

## Hazırlanmış paket, otomatik bütün-host kurulum değil

`NativeRuntimeSetup`, normal state'teki bounded **`games/native-runtime-candidate.json`** descriptor'ını ve hash-bound native acceptance report'u okur. Bu descriptor source/core/codec/input doğrulaması yapılmış mevcut local cache'i işaret eder. CLI staging ile **candidate** hazırlanır; etkin runtime profile ancak owner/local/CSRF/actor-matched explicit POST ile yazılır.

- Runtime: RetroArch **1.22.2 + upstream input fix cfb12043**, değişmeyen released **mGBA 0.10.5**, MediaMTX **1.21.2**, original MIT reader.js.
- File hashes, rejected legacy EXE, full native held+combined/save/load/WHEP/windowless proof ve effective firewall policy enable/launch'ta kontrol edilir. Missing/changed files → unavailable/fail-closed, existing ROM/save korunur.
- Bu first prototype binary'ler approved cache'te kalır; Home ZIP'e emulator/codec binary'si eklenmez. Cache temizlenirse native availability kaybolur ama ROM/save metadata silinmez. Managed binary distribution/fresh-host automatic install ve corresponding-source/signing/licence kabulü ayrı açık kapıdır.
- Yeni driver, service, port allow, trust-policy veya OS sandbox yapılmaz. Mevcut user-approved exact EXE block kullanılır. Emulator **AppContainer sandbox değil**; yalnız owner/local scope ve güvenilir ROM kabulü vardır. Arayüz bunu gizlemez.

## Lifecycle / veri sınırları

Setup POST yalnız bool enable/accept ve expected version alır; browser executable/path/proof gönderemez. Enable için açık prototype onayı zorunlu; stale version/conflicting profile korunur. Existing JSON'da yalnız `enabled` değiştirilir, unknown operator metadata korunur. Profile publication same-directory temporary + flush + atomik move; cancelled attempt önceki state'i korur.

Setup ve native launch **aynı manager gate** ile serileştirilir: active game sırasında toggle 409; driver/executable profile oyun altından değiştirilmez. Etkinleştirme native process başlatmaz. Native launch/commands/ticket/reader, binary CAS snapshots, timeout/revocation cleanup ve original media/input motorları [backport kaydındadır](gaming-native-frontend-backport.md).

Player audio/video track'lerini aynı MediaStream'e toplar. İlk playback muted; gerçek kullanıcı gesture'ında sound/play istenir; browser audio rejection sunucu resume'unu bekletmez. Ses elle açılıp kapatılır; heartbeat kendi başına local pause menüsünü kapatmaz. Keyboard Shift+J/K dahil normalized keys; receive tracks/reader/interval cleanup korunur.

## Verification

- **86 backend, 37 Node / 0 skipped**, `artifacts/test-results/native-home-v034-final.trx`; TypeScript/worker/frontend/PWA build geçti. Vite chunk-size / dependency `use no memo` uyarıları var, build tamamlandı.
- Gerçek runtime ile isolated SQLite/manager **candidate disabled → explicit enable → scoped WHEP201 → active toggle rejected → CAS save/end/relaunch → disable → 0 active sessions**: `...\native-home-isolated-tests\attempt-0aa1c9a2f586459eb1b39aac54c87fb0\home-native-smoke.json`.
- Unit/API checks: owner/local/CSRF/matching Home headers, missing/changed/source-only proof, rejected prerequisite, stale/conflicting profile, cancellation ve user source/save/extra metadata korunumu. Tests'teki fake prerequisite yalnız isolated tests; production verifier native binaries/firewall'ı bypass etmez.
- Preview preflight **0.33.0 / schema40**, **0 playback / 0 game sessions / 0 running downloads**; archived dahil üç transfer paused. Güncel **5 game user states** önceki tarihi sayılara döndürülmedi. Backup **f20d494281f0416a9f0ba1feda3b7fbf**, `artifacts/preview-before-native-v034.json`; complete game/save/data digest capture `artifacts/native-preview-data-before.json`.
- Signed update/live schema/data/HTTP sonuçları uygulama sonrası burada kaydedilir. `tools/verify-live-native.mjs` salt okunurdur: enable/play/acquisition yapmaz; prepared=true, enabled=false bekler ve mevcut user-data/save/download digest'lerini karşılaştırır.

## Canlı yayın / başlangıç düzeltmesi

**0.34.1 / şema 41** canlı: `http://localhost:5080/games`, PID **20576**, release **32b36a2ede204ad484df0b68c35267bb**. Trusted-key fingerprint değişmedi: **b6ebb7e0a2b2821afd3143dc3bd9c8b666189d9a41953aa6657cf60d789b566b**. Signed Windows package **901 files / 115949323 bytes**; native emulator/codec binaries ZIP'e eklenmedi.

İlk Windows PowerShell buffered package upload timeout verdi; pending update oluşmadığı kontrol edildi. Streamed authenticated update API ile 0.34.0 imzalı olarak uygulandı fakat cold startup, kullanıcının daha önce kurduğu **HexGL** asset adlarındaki internal spaces'i reddetti. Dört exact upstream texture adı (`cloud - Copie.png`, `spark - Copie.png`, full/normal texture dizinleri) manifest/hash doğrulaması geçerliydi; dosyalar silinmedi veya değiştirilmedi.

`GameCatalog.IsSafeAssetPath` internal spaces kabul eder; aynı bounded extension whitelist ve portable `DownloadFiles.ValidateRelative` ile traversal/absolute/reserved-device/trailing-space-dot sınırlarını korur. Isolated store install → cold startup testi ve 7 negative path testi eklendi. **0.34.1** aynı trusted key ile doğrulandı, yeni release dizinine kopyalanıp stopped preview için offline active-pointer recovery yapıldı. **DB restore/rollback veya user game silme yapılmadı**, last healthy rollback 0.33.0/backup **fde2dc60be1841e3ad92ad82fa15487e** korunur. Kurtarma: `artifacts/update-live-0.34.1.json`; 0.34.0 failed release/log'lar kanıt olarak kalır.

Final **94 backend / 37 Node / 0 skipped**, `native-home-v0341-final.trx`; frontend/worker/PWA ve signed publish geçti. Read-only live doğrulama `artifacts/live-native-v034.json`:

- **prepared=true / canConfigure=true / enabled=false / available=false**: kullanıcı Home checkbox/onayını verene kadar emülasyon açılmaz.
- **0 FK violations**, media/request/personal/collection/game-user-state/**game-saves** digest'leri aynı; **5** güncel game states korunur, metadata dili değişmez.
- Archived dahil YTS / Torrents.csv / Tool-Lateralus **üçü de paused**, hiçbir acquisition/resume komutu yok. Native ROM import/play yapılmadı.
- Client **`/assets/index-BJ8QbF-q.js`**, official reader SHA-256 değişmedi; PWA **1ecba6a95a0c60f5**. Gerçek browser decode/audio/controller/ROM kabulü halen kullanıcıda.

Kaynaklar/kimlik/lisanslar: [native foundation](gaming-native-foundation.md), [native runtime](gaming-native-runtime-slice.md), [limited backport](gaming-native-frontend-backport.md), [dependencies](dependencies.md). React effect cleanup, HeroUI v3 installed Button/Modal types ve Query mutation invalidation güncel Context7 ile **2026-10-20** kontrol edildi.

**0.34.2 devamı:** Kullanıcı emülasyonu etkinleştirip kendi ROM'unu yükledi; Play'de absolute URL integration hatası bildirdi. [Reader URL/auth fix](gaming-reader-url-fix.md) canlı uygulandı. User enabled=true ve ROM/game saves korunur; önceki disabled/5-state snapshot geri yüklenmedi. Actual browser success henüz kullanıcı tarafından doğrulanmadı.

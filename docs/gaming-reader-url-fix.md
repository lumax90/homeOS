# Native reader URL/auth fix — 0.34.2

Tarih: **2026-10-20**. GAM-05/11 / GAMING-READER-FIX-01. Kullanıcı kendi ROM'unu yükleyip emülasyonu etkinleştirdi ve Play'de **`TypeError: Failed to construct 'URL': Invalid base URL, retrying in some seconds`** bildirdi. Bu, gerçek browser denemesinin başlangıç hatasıdır; başarılı gameplay/audio/device kabulü değildir.

## Neden ve dar düzeltme

Pinned MediaMTX **1.21.2** reader'ın `Conf.url` sözleşmesi **absolute WHEP URL** ister. Home `appUrl(launch.url)` ile relative `/game-native/<id>/whep` veriyordu. Fetch bunu kabul ediyor, ancak başarılı POST'un `Location` cevabı `new URL(location, conf.url)` ile çözüldüğünde relative base TypeError veriyor.

`nativeWhepUrl` şimdi Home relative route'unu current page URL ile absolute yapar; Home origin/base-path korunur. Arbitrary route, external origin, credentials, query veya fragment reddedilir; bearer ticket URL'ye yazılmaz.

Aynı exact upstream reader, token'ı **OPTIONS/POST**'a ekliyor ancak resource **PATCH/DELETE**'e eklemiyor. Home WHEP proxy bütün işlemlerde game/login-bound bearer doğrular. Bu kontrol gevşetilmedi ve global fetch değiştirilmedi.

`tools/build-native-reader.mjs` pinned source hash'ini doğrular ve **aynı, değiştirilmemiş upstream class body** etrafında per-reader lexical HTTP adapter üretir (`native/home-reader.js`). Adapter yalnız aynı Home origin ve aynı WHEP session path'ine Authorization ekler; foreign Location, redirect ve URL token engellenir. ICE, SDP, peer/media ve retry motoru upstream'dir; Home yeni RTC/codec/emulation motoru yazmaz. Original `reader.js` SHA-256 **a802f229b803c33713d4c69c4cc0d480108a5bf384947aeee4aaf04268bf85c1** ve MIT notice korunur. Emülatör/core/binary/firewall ayarı değişmez.

## Kontrol

- Pure URL test eski hatayı **aynı URL constructor** ile yeniden üretir ve localhost + `/home` base-path için absolute fix'i doğrular.
- Actual original SDK class, isolated peer/HTTP fixtures ile **OPTIONS → POST201/relative Location → PATCH204 → DELETE** yolunda çalıştırılır. Her aşamada correct bearer, If-Match, same origin/session ve no URL token kontrol edilir. Foreign Location'ya credentialed fetch yapılmaz. **Bu Node protocol-contract testidir, automated browser/gameplay suite değildir.**
- **4 new tests**, zero skipped; focused gaming/localization checks **38 passed/0 skipped** (driver-only üç test bu filtrede yok). TypeScript/worker/PWA build geçti; final bundler'ın önceki chunk/directive warnings'i sürer.
- Current user ROM metadata, native enable tercihi, **7 current game user states**, binary save manifest/other media/request/personal/collection/paused-transfer digests update öncesi `native-preview-data-before-v0342.json` ile yakalandı. Historical 5-state/disabled snapshot geri yüklenmez. Active playback/game **0** kontrol edildi; gerçek ROM development tool ile oynatılmadı.
- Signed publish/update ve readonly post-update sonuçları yayın sonrası eklenir. Browser/real audio/controller acceptance kullanıcıya aittir.

## Yayın sonucu

**0.34.2 / şema 41** signed Windows update normal authenticated API ile uygulandı; PID **22048**, mevcut trusted-key fingerprint değişmedi. Paket **904 verified files / 115965028 bytes**, rollback **0.34.1**, apply backup **0e674b25835b44e89ca44f5637838f6c**. `artifacts/update-live-0.34.2.json`.

Readonly live kontrol `artifacts/live-native-v0342.json`: native **prepared/enabled/available=true**, kullanıcının etkinleştirme tercihi korunur. Mevcut ROM metadata ve media/request/personal/collection/game states/saves/download digests aynı, **0 FK violations**. Üç transfer paused; native gameplay/acquisition verification komutu yok. Yeni client **`/assets/index-BoPF1B4l.js`**, scoped `home-reader.js` aktif, original vendor reader SHA değişmez; PWA **5fac1706f0b350b6**. Kullanıcı aynı ROM'u yeniden yüklemeden sayfayı yenileyip Play'i tekrar deneyebilir. Bu, actual browser decode/audio/controller success iddiası değildir.

Kaynak **2026-10-20**: exact installed `web/public/native/reader.js` `Conf.url`, `#sendOffer`, `#authHeader`, `#sendLocalCandidates`, `#handleError`; current MediaMTX Context7 doc aynı davranışı doğrular. Source/transport/license sınırları [Home enable](gaming-home-enable.md) ve [native backport](gaming-native-frontend-backport.md) ile aynı.

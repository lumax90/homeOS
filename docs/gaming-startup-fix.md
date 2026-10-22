# Oyun başlatma hatası — 0.32.1

Tarih **2026-10-19**, kaynak hedefi **0.32.1-dev.1**, şema **40**. Gereksinimler **GAM-02/03**, **CLI-01/05**, **OPS-04**; kart **GAMING-FIX-01**. Kullanıcı oyun seçince “The game engine could not start. Check browser compatibility.” mesajı bildirdi. Bu hata gerçek bir tarayıcı uyumsuzluğu teşhisi değildi.

## Doğrulanan nedenler

1. **Godot 3.6.1 native PWA hook'u:** WASM bootstrap `_godot_js_pwa_cb`, `'serviceWorker' in navigator` kontrolünden sonra `navigator.serviceWorker.getRegistration()` çağırıyordu. Opaque sandbox'ta özellik mevcut görünür, fakat getter `SecurityError` atar. İzole gerçek browser frame'inde `engineInitialized=true`, `engineReady=false` ve bu tam hata doğrulandı. Optional native PWA hook'ları getter erişimini try/catch ile kontrol eder. **`allow-same-origin` verilmedi; sandbox/CSP gevşetilmedi.** Home shell'de persistence/PWA kapalı kalır.
2. **Eksik paket kaynakları:** `media/font/*` filtresi sadece font binary'lerini değil, oyunların gerekli `m3x6.tres`, `profile/OptionsItem.tres` gibi font ayarlarını da çıkarmıştı. Font binary filtreleri `.ttf/.otf/.woff/.woff2` ile daraltıldı; bu formatların tüm actual references'ı denetlenmiş Roboto'ya çevrilir. `.tres` font kaynakları korunur. PCK resource graph kontrolü eski iki pakette hatayı yeniden üretti; yeni paketlerde **368 Tiny Crate / 490 Wrap It Up** kaynağının bağımlılıkları kapalıdır.
3. **Gezinme `Illegal invocation`:** page position host'una doğrudan verilen `cancelAnimationFrame`, host nesnesinin metodu olarak çağrıldığında yanlış Window receiver kullanıyordu. Window'a bağlı wrapper'lar ve receiver zorlayan regresyon testi eklendi.
4. **Yanlış açık-oyun güncelleme kontrolü:** PowerShell `@(Invoke-RestMethod ...).Count`, boş `[]` yanıtını bir pipeline nesnesi sayıyordu. Gerçek endpoint üzerinde **actual 0 / wrapped 1 / valid-session 0** doğrulandı. Betikler response'u değişkene alıp gerçek session ID'lerini sayar. Kullanıcının “kapattım ama görünüyorsa bug olabilir” geri bildirimiyle kontrol edildi; zorla kapatılacak gerçek bir oyun oturumu yoktu.

## Kapanış koruması

İncelemede ayrıca, belge kapanınca `flush().finally(close)` zincirinin close isteğini hiç gönderemeyebileceği görüldü. Bu, yukarıdaki PowerShell false-positive'nin kanıtlanmış nedeni olarak sunulmaz; ayrı yaşam döngüsü korumasıdır.

- `pagehide` ve React unmount, son bilinen bounded checkpoint + revision'ı **tek authenticated keepalive `/games/sessions/{id}/end` isteğiyle** hemen gönderir.
- Server, aynı user/login yetkisini doğrular. Save CAS çakışırsa iyi kaydı korur; kendi oturumunu yine kapatır. Başka login/session kapatılamaz.
- Açık save-and-exit akışı frame flush/confirmed save beklemeyi sürdürür; discard için save gönderilmez. Tekrar close gönderimi engellenir.
- BFCache'den dönen kapalı ticket yeni oturumla açılmak üzere reload edilir. Ani süreç/ağ kaybında keepalive teslimi garanti değildir; mevcut 3 dakikalık lease süresi fallback olarak kalır.
- Game/save sürümü **classic-home.1** değişmedi; WASM core aynı, loader/eksik UI resource düzeltmesi runtime revision **2**. Şema/migration ve profil izinleri değişmedi.

## Doğrulama

- **104/104 Node**, 0 atlanan: throwing sandbox getter, normal PWA hook'u, adapter idempotency/upstream değişimi reddi, font binary/resource ayrımı, PCK dependency closure, native Window receiver, empty REST array, unload snapshot ve mevcut input/navigation/localization/player kontrolleri. `artifacts/gaming-v0321.tap`.
- **27/27 odaklı backend**, 0 atlanan: `GameTests`, `MigrationTests`, `NetworkTests`; yeni tek-komut end/CAS/cross-login ve HTTP endpoint kontrolleri. `artifacts/test-results/games-boundaries-v0321.trx`.
- Kullanıcı hesabı/kütüphanesi yerine **ayrı Temp/opencode data path ve 127.0.0.1:5081 origin** ile teşhis yapıldı. Geçici browser sekmesi kapandı; yalnız bize ait teşhis process'i durduruldu. Test login'i kapatıldı ve bu origin'in worker'ı unregister edildi.
- İki gerçek oyun, aynı Chromium ortamında **phase started / engineReady true / startError null / opaque origin null / 0 console error** durumuna ulaştı. `artifacts/gaming-startup-browser-v0321.json`. Bu, fiziksel TV/gamepad, ses/fullscreen user activation veya sustained gameplay kabulü değildir; desktop click tool'u UnknownVizError döndürdüğünden oyun içi oynama başarısı ilan edilmez.
- Empty-array gerçek HTTP/PowerShell kanıtı: `artifacts/gaming-empty-session-guard-v0321.json`.

## Kullanıcı niyeti / veri korunumu

Kullanıcı olası artık oyun oturumunu kapatma izni verdi; kontrol gerçek oturum **0** gösterdi, zorla kapatma yapılmadı. Sonraki preflight, **Tool - Lateralus (2001) FLAC**, ID `eee9c5de62814d43b4907c297bba86a1`, transferinin kullanıcıca running olduğunu gördü. Kullanıcı açıkça **“Sen duraklat”** dedi; yalnız bu transfer pause edildi ve güncelleme sonrasında paused bırakılacak. Yeni medya/ROM edinmesi başlatılmadı; hiçbir paused transfer resume edilmedi.

Çalışan önizleme hesabı **home**, kullanıcının seçtiği mevcut parola ve diğer UI/metadata tercihleri korunur. Genel backend regresyonu ve gerçek cihaz kabul kapıları açık kalır.

## Canlı 0.32.1 yayını

- **27/27 backend, 104/104 Node, 0 atlanan**; frontend/worker production build ve .NET publish geçti. Kanıt `artifacts/gaming-v0321.tap`, `artifacts/test-results/games-boundaries-v0321.trx`, `artifacts/gaming-build-v0321.log`.
- Güncelleme ön kontrolü gerçek **0 oyun / 0 medya oynatma** ve paused transferlerle geçti. Pre-update backup **444adbfe071043089ef09b8c6dc2ea44**, `artifacts/preview-before-v0321.json`.
- İmzalı Windows ZIP **879 dosya / 115.662.760 byte**: `artifacts/packages/preview-v0321/home-0.32.1-dev.1-win-x64.zip`; SHA-256 **f1953ed169503235c1694209e6b1fe1cb05d78fe6c428b571b8535785970e3ff**. Önceki güvenilen anahtarla `--verify-package` geçti; signing identity değişmedi.
- **http://localhost:5080/games**, runtime **0.32.1**, schema **40**, PID **19576**, release `9a81f457eb4942d98ae95af528333c65`; PWA **d99ad3f1d87e9189**, ana istemci `/assets/index-fmy_GMkf.js`.
- Update istemcisi hesabı korudu; rollback **0.32.0**, apply backup **8995de6aad4540ab8026d1c8a8abe174**. `artifacts/update-live-v0321.json`.
- Canlı read-only kontrolde **7 medya / 2 istek / 3 indirme / 5 kişisel kayıt**, mevcut **2 game state**, metadata **en-US**, **0 FK ihlali**. İki yeni kaynak paketi ve PNG kapakları sunuluyor; oyun/save sürümü classic-home.1. `artifacts/live-games-v0321.json`.
- Kullanıcının izniyle pause edilen **Tool - Lateralus** ve diğer iki transfer paused kaldı; resume/edinme komutu verilmedi. Ana kullanıcı sekmesi yeniden yüklenmedi veya gezinmesi değiştirilmedi; teşhis sekmesi/işlemi kapatıldı.

Kullanıcı Ctrl+F5 ile yeni istemciyi yükleyip tekrar Oyna → Oyuna dön akışını denemeli. Bu kayıt bütün-TV/gamepad/ses/uzun oyun kabulü değildir.

## Kullanıcı geri bildirimi — 2026-10-19

Kullanıcı **iki oyunun güzel çalıştığını** bildirdi; 0.32.1 başlatma düzeltmesi kullanılan istemcide olumlu kabul aldı. Cihaz/TV modeli, gamepad, sustained play, kayıt/kopuş ve fullscreen/ses matrisi ayrıca doğrulanmış sayılmaz; ACCEPT-32-GAMES'in bu kapıları açık kalır.

Daha iyi oyun seçkisi ve Oyunlar içinde mağaza/katalog fikri kullanıcı tarafından gündeme getirildi. Bu bir sonraki dilim adayıdır; mağaza, ödeme, harici oyun sağlayıcısı veya yeni oyun paketi uygulama kararı/verisi olarak kabul edilmedi. Emülasyon tarafı kullanıcı isteğiyle beklemede kalır. Mevcut Tiny Crate Classic/Wrap It Up, Godot'un WASM web build'leridir; konsol emülasyonu değildir.

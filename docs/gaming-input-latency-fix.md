# Native input stalls — 0.34.3

Kontrol **2026-10-20**, GAM-05/10/11, GAMING-INPUT-LATENCY-01. Kullanıcı görüntünün geldiğini fakat **klavye/gamepad girdisinin bazen kaybolup yaklaşık 3 saniye sonra görüldüğünü** bildirdi; hardware'ın yeterli olduğunu belirtti. Siyah ekran teşhisi bırakıldı; hardware yetersizliği varsayılmadı.

## Ölçülen neden

`Home.NativeAssets --measure-input-guard` exact reviewed EXE için eski iki `NativeLocalInputGuard.Inspect` çağrısını **2120 / 1882 / 1881 ms** ölçtü. Check her saniye **aynı 20 ms native relay döngüsünde** synchronous Windows firewall/COM enumeration yapıyordu. O sırada kısa input edges yalnız `Desired` latest value'da tutulduğu için kaybolabiliyor, uzun basış da sonraki relay'e kadar gecikiyordu. Bu güvenlik kontrolü gerekli olabilir; **hot input path'te çalıştırılması hataydı**.

İstemcide ayrıca **100 ms coarse polling** ve in-flight latest-state skip vardı. Bu bir güvenlik gereği değildi. User feedback'le bu sınır kaldırıldı.

## Sınırı gevşetmeden değişiklik

- Launch/enable exact EXE/file/core/policy doğrulaması değişmez. İki UDP portunun kontrolü **tek effective-policy snapshot** üzerinden yapılır.
- Runtime policy monitor **tek ayrı background worker**; COM/RPC hiçbir şekilde 20 ms relay döngüsünde çalışmaz. Denied/failed, **5s stale** veya future-clock result fail-closed; oturum kontrollü kapatılır. Shutdown bounded. Firewall kapatma/rule değişikliği veya runtime auth bypass yok.
- Native server **32-edge bounded queue** kullanır; press/release aynı relay aralığına gelse bile sırasıyla uygulanır. Pause/deadman/epoch reset ve overflow neutral/fail-closed; unchanged held packets queue büyütmez.
- Browser keyboard edge anında kuyruğa girer, gamepad RAF sample; **20ms / en fazla 50 request/s** bounded serialized dispatch. Press/release in-flight sırasında düşmez. Stable state 200ms keepalive; paket flood veya native motor yeniden yazımı yok. Server 60/s rate boundary korunur.

## Verification

**98 backend / 46 Node, 0 skipped**, `artifacts/test-results/native-input-v0343-final.trx`; TypeScript/frontend/worker/PWA build geçti. Bir parallel web-assets rebuild ile backend build aynı `wwwroot` snapshot'ını değiştirdiği için build-only race verdi; frontend tamamlandıktan sonra serial backend run yukarıdaki 98/0 sonucu verdi. User preview/data etkilenmedi.

Original isolated ARM fixture ile **actual Home manager + released mGBA + unchanged patched RetroArch** kontrolü, policy monitor çalışırken key-register readback ölçtü: **103.6 / 111.8 / 112.8 ms**. Report:

`...\native-home-isolated-tests\attempt-511df37e97504d79ae6455e71c441ba0\home-native-smoke.json`

Bu **server-stage → fixture KEYINPUT observation** ölçümüdür; fixture full Mode-3 paint/VBlank ve command readback süresini içerir. Browser/network/encoded-video total input-to-photon ölçümü **değildir**. Gerçek ROM'u development tooling oynatmadı. Webcam/mic/private desktop yok. Girdi tarafındaki 2s block kaldırıldı; actual browser lag acceptance kullanıcıdadır.

Tests: short pulse while HTTP pending; edge release order; stable keepalive; cancellation/stale epoch; queue overflow neutral; server pulse/duplicate/reset; policy denied/stale/future. Native engine/codec/frame limiter değiştirilmedi. Mevcut ROM/save/enable preference/paused transfers update öncesi `native-preview-data-before-v0343.json` ile korunur.

Signed 0.34.3/live acceptance burada publication sonrası kaydedilir. Browser suite/screenshot çalıştırılmaz; bu bütün native platform/GPU/TV kabulü değildir.

## Canlı 0.34.3

Normal trusted signed-update API ile **0.34.3 / şema41**, PID **18988** canlı. Paket **904 files / 115967891 bytes**, önceki sürüm **0.34.2** ve backup **cebbb49cfe9341389db86bbfacef2b14** rollback olarak korunur. `artifacts/update-live-0.34.3.json`.

Readonly `artifacts/live-native-v0343.json`: user native **enabled/available=true** tercihi, ROM metadata, **7 game states**, existing game saves/media/request/personal/collection digests aynı; **0 FK violations**. Üç transfer hâlâ paused; native gameplay/acquisition/resume komutu yok. Native core/EXE/version partition değişmedi; yeni kayıt partition'ı oluşturulmadı. Client **`/assets/index-L4Xcmne6.js`**, PWA **fda30b497ac74f31**. Kullanıcı sayfayı yenileyip aynı ROM ile kısa basış/held/gamepad denemesini tekrar yapabilir; toplam görüntü gecikmesi ve gerçek cihaz kabulü henüz doğrulanmış değildir.

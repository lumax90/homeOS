# 0.21 — Hesap ve cihaza bağlı web/PWA push

Tarih: **2026-10-14**, **0.21.0 yayımlandı**. Gereksinimler: **OPS-07, USR-01/02, CLI-01/02, OPS-01/03**.

## İstemci ve sunucu

Home sunucusu aynı kişisel bildirim/outbox altyapısından tarayıcının push servisine şifreli ileti gönderir. Web/PWA istemcisi izin, cihaz aboneliği, service worker bildirimi ve tıklama akışını yönetir. Bu dilim web/PWA içindir; TV ve yerel mobil uygulamalar sonraki istemci hedefleridir.

- Yönetici **Ayarlar → Bildirim kanalları → Web/PWA push sunucusu** bölümünde açık/kapalı durumunu ve `mailto:`/HTTPS VAPID iletişim adresini kaydeder.
- İlk etkinleştirmede ayrı **ECDSA P-256** anahtarı üretilir. Özel anahtar Data Protection ile korunur; API yalnız açık anahtar/ayar revizyonunu verir. İletişim adresi değişikliği anahtarı korur. Açık anahtar yenileme komutu bütün eski push aboneliklerini kapatır.
- Her kullanıcı **Ayarlar → Bu cihazda push** bölümünde izin verir, cihaz adı ve olay filtrelerini seçer, deneme gönderir veya kendi cihazlarının push'unu kapatır. İzin isteme yalnız düğme etkileşiminden başlar.
- HTTPS/secure context ve gerçek API feature detection kullanılır. iOS/iPadOS'ta Home Screen PWA gereksinimi açıklanır. Bekleyen service worker sürümü için kullanıcı komutuyla güncelleme/yeniden açma vardır.

## Protokol ve ağ sınırı

**Lib.Net.Http.WebPush 3.3.1** ve **Lib.Net.Http.EncryptedContentEncoding 2.1.1**: RFC 8030, RFC 8291 (`aes128gcm`, ECDH/HKDF/AES-GCM), RFC 8292 (ES256 VAPID). .NET 6 / .NET Standard 2.1 asset'leri kullanılır; eski Portable.BouncyCastle paketi eklenmez. Lisanslar MIT, paket içinde kaynak revizyonlarıyla bulunur.

- Push endpoint yalnız HTTPS/443, credentials/fragment olmadan kabul edilir. Desteklenen servisler `fcm.googleapis.com`, `updates.push.services.mozilla.com`, `*.push.apple.com`, `*.notify.windows.com`; sahte suffix, özel IP/localhost ve keyfi HTTP hedefleri reddedilir. Bu sınır normal kullanıcının push kaydını sunucudan keyfi HTTP isteğine çevirmesini engeller.
- `p256dh` gerçek 65-byte uncompressed P-256 noktası, auth secret 16 byte olmalıdır. Sunucu açık anahtarı ve anahtar sürümü mevcut ayarla eşleşmelidir.
- VAPID süresi 1 saat, push servis TTL'si **60 saniye**, istemci zarfı süresi **2 dakika**. Gönderimin `Topic` alanı kalıcı delivery ID'dir.
- Kütüphanenin otomatik HTTP retry döngüsü kapalıdır. 408/425/429/5xx ve ağ hataları Home'un kalıcı kuyruğuna döner. 404/410 aboneliği kapatır ve endpoint/key materyalini kaldırır.
- HTTP cookie/Home oturumu dış push servisine gönderilmez. Yanıt gövdeleri kütüphaneye boş olarak verilir; capability endpoint/anahtar/uzak hata metni log ve sonuçlara yazılmaz. Hedef TLS doğrulaması ve yönlendirme yasağı uygulanır.

## Hesap, cihaz ve izin

- Abonelik, sunucunun mevcut **giriş oturumu + korumalı tarayıcı cihazı + Home hesabına** bağlanır. Bir cihaz kaydında bir aktif push aboneliği vardır. Aktif endpoint başka cihaz/giriş tarafından sahiplenilemez.
- POST gövdesindeki beklenen kullanıcı/giriş ve ayar revizyonları denetlenir. Profil değişiminden sonra geciken eski kayıt komutu yeni hesaba bağlanamaz. Aynı komut tekrarında aynı kayıt/binding döner.
- Çıkış, parola/PIN/hesap değişikliğinin giriş iptali ve giriş kaydı silinmesi aynı DB işleminin trigger'larıyla pending/sending push'u iptal eder, sırları temizler ve aboneliği kaldırır. Süre bitimi ve hane yetki değişiklikleri gönderim kontrolü/periyodik uzlaştırmayla elenir.
- Abonelik ömrü tarayıcının bildirdiği süre ile Home girişinin kalan ömrünün küçüğüdür. Oturum açınca bildirimler otomatik başka profile aktarılmaz; kullanıcı yeni girişte etkinleştirir.
- Mevcut kanal sınırı: kullanıcı 8 / sunucu 64. Push kanalı 60 deneme/saat; hesap genelinde **120 push denemesi/saat** ve **30 yeni push kaydı/saat**. Kanalı silip yeniden oluşturmak bu hesabın bütçesini sıfırlamaz. Son saatin bütçe/tombstone kayıtları korunur.

## Service worker ve paylaşılan cihaz

- Şifreli zarf yalnız sürüm, binding ID, delivery ID, deneme işareti ve bitiş zamanı taşır. **Medya başlığı, özel bildirim gövdesi ve hesap adı kilit ekranına gönderilmez.** Bildirime tıklamak Home'un güncel kişisel inbox'ını açar.
- Worker, çevrimiçiyken sunucunun geçerli hesap/binding bilgisini kontrol eder. Home ağına ulaşılamıyorsa yalnız süresi geçmemiş yerel binding için kısa ömürlü genel bildirim gösterilebilir.
- Binding ve son 128 delivery kimliği scope'a özel IndexedDB'de tutulur. Eski hesap iletisi, yinelenen delivery, bozuk/bitmiş veya aşırı ileri tarihli zarf gösterilmez. Geciken sync/clear cevabı yeni hesabın binding'ini ezemez.
- Gösterim, bağlama, temizleme ve tıklama etkileri aynı worker sırasından geçer. Eski binding'in görünür bildirimleri kapatılır. Bildirim tıklaması hesap durumunu yeniden kontrol eder; payload'dan keyfi URL açılmaz.
- Kayıt/kapatma/yenileme işlemleri destekleyen tarayıcılarda scope'a özel **Web Lock** kullanır. `pushsubscriptionchange` yeni aboneliği verirse, mevcut cookie/CSRF ve kullanıcı/giriş revizyonları doğrulanarak yenilenir. Kullanıcı izni otomatik istenmez.
- Push servisi kabulü cihazda gösterim veya kullanıcının okuması anlamına gelmez. Önceden kabul edilmiş ağ iletileri geri çekilemez; binding/TTL/istemci filtreleri geç kalan mesajı sınırlar. Gerçek cihaz kabulü aşağıda açıktır.

## Şema ve uygulama yüzeyi

Şema **24 → 25**, `025_web_push.sql`. `notification_channels` tür constraint'i kontrollü rebuild ile push'a genişletilir; önceki kanal/delivery/attempt kayıtları korunur ve migration sonunda FK denetlenir. `browser_push_subscriptions` ve giriş iptal/silme trigger'ları eklenir. Önceki migration dosyaları değişmedi.

- `/api/push/configuration` yönetici GET/PUT.
- `/api/push/device` kişisel durum/abonelik; `/device/test`, `/devices/{channelId}` kapatma.
- `WebPushSettings`, `WebPushTransport`, `MediaStore.WebPush`; mevcut `NotificationDispatcher`/outbox.
- `WebPush.tsx`, `pushBridge.ts`, `push-controller.mjs`, `service-worker.js` ve üretim worker derlemesi.

## Doğrulama

- **20/20 odaklı backend/API/kriptografi kontrolü**, atlanan 0; **8/8 saf worker durum makinesi testi**. Frontend TypeScript/üretim build ve oluşturulmuş service worker sözdizimi kontrolü geçti.
- Gönderilen VAPID JWT'si bağımsız .NET ECDSA alıcısıyla doğrulandı. AES128GCM zarfı, gönderen kütüphanenin yardımcıları kullanılmadan RFC 8291 ECDH/HKDF/AES-GCM işlemleriyle çözüldü; özel başlık/gövde taşınmadığı denetlendi.
- Geçersiz curve/base64, keyfi endpoint, başka kullanıcı/cihaz/giriş ve CSRF reddi; çıkış/profil/anahtar değişikliği; 404/410, tek kalıcı retry ve sil-yeniden-kur bütçe koruması doğrulandı.
- Şema 24'te oluşturulan gerçek kanal/delivery/attempt verisi 25'e taşındı; sır çözümü, sonuçlar ve FK bütünlüğü doğrulandı.
- **240/240 backend/native test, atlanan 0**, `artifacts/test-results/home-0.21.0-full.trx`; **8/8 worker testi**, üretim frontend ve derlenmiş worker sözdizimi kontrolü geçti.
- Windows x64/Linux x64/Linux ARM64 paketleri aynı güvenilir anahtarla üretildi. Gerçek Windows sürecinde **0.20.0/şema 24 → 0.21.0/şema 25 → önceki sürüm/veri** update/rollback geçti; hesap korundu, restore eski oturumları iptal etti (`artifacts/update-roundtrip-0.20.0-0.21.0.json`). Linux runtime kabulü açıktır.
- Canlı Home imzalı uygulama içi güncellemeyle 0.21.0'a geçti; sahip oturumu korundu. Şema 25, FK ihlali 0; worker push kodu sunuluyor. Push varsayılan olarak kapalı, canlı VAPID anahtarı/aboneliği oluşturulmadı. API/DB'de 0 push aboneliği/denemesi, 0 oynatma, 0 indirme, 0 etkin sentetik kütüphane; VAD CPU kullanılabilir (`artifacts/live-runtime-v021.json`).
- Browser otomasyonu veya gerçek push servisine geliştirme mesajı gönderilmedi; son cihaz/gateway kabulü kullanıcı sonucunu bekler.

## Kaynaklar — 2026-10-14

1. Context7 araması C# paketine uygun kayıt döndürmedi. Resmi pinned kaynak/API ve kurulu XML tipleri kullanıldı: [Lib.Net.Http.WebPush](https://github.com/tpeczek/Lib.Net.Http.WebPush/tree/5f601ff75f9de62ab2fa83206ff0ac9b9ab10d4c), [EncryptedContentEncoding](https://github.com/tpeczek/Lib.Net.Http.EncryptedContentEncoding/tree/6b98f8257e43badcd306cb989dc6913460017cfd); NuGet listed 3.3.1 ve asset/dependency kontrolü.
2. [MDN PushManager.subscribe](https://developer.mozilla.org/en-US/docs/Web/API/PushManager/subscribe): secure context, kullanıcı etkileşimi, userVisibleOnly ve applicationServerKey; kurulu TypeScript DOM tipleri.
3. [WebKit Web Push / iOS-PWA](https://webkit.org/blog/13878/web-push-for-web-apps-on-ios-and-ipados/): Home Screen uygulaması ve `*.push.apple.com` servis sınırı. Eski kütüphane README'sindeki Safari destek tablosu güncel destek kanıtı olarak alınmadı.
4. [MDN Web Locks](https://developer.mozilla.org/en-US/docs/Web/API/LockManager/request): aynı origin'deki tab/worker işlemlerinde exclusive lock ve cancellation; [RFC 8291](https://www.rfc-editor.org/rfc/rfc8291), [RFC 8292](https://www.rfc-editor.org/rfc/rfc8292).

## Kullanıcı kabulü — ACCEPT-21

1. Yönetici push iletişim adresini kaydedip sunucuyu etkinleştirsin. HTTPS Home'da **Bu cihazda push** bölümünden izin ver; iPhone/iPad'de Home Screen PWA'yı kullan.
2. **Deneme gönder** ile cihaz bildirimini kontrol et; tıklayınca kendi bildirimlerin açılmalı. Worker güncellemesi bekliyorsa **İstemciyi güncelle ve aç** düğmesini kullan.
3. Profil değiştir/çıkış yap; önceki hesabın push'u kapanmalı. Yeni profilde açıkça etkinleştirdikten sonra filtre ve diğer cihazdan kapatma akışlarını dene.
4. Kullanacağın Chrome/Firefox/Edge/Safari/PWA cihazlarında arka plan ve ağ değişimi davranışını bildir. Push gateway kabulü ile cihaz sonucu ayrı izlenir.

Kalan ana ürün işleri, TV/yerel istemci hedefleri, gerçek kaynak/cihaz ve Linux/dağıtım kabul kapıları [panoda](product/execution.md) açıktır.

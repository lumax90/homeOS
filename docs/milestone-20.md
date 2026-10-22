# 0.20 — Webhook ve SMTP bildirim teslimi

Tarih: **2026-10-14**, **0.20.0 yayımlandı**. Gereksinimler: **OPS-07, OPS-01/03, USR-01/02**.

## Olaydan kalıcı gönderime

- Yönetici, alıcı Home hesabına bağlı **webhook** veya **SMTP** kanalı tanımlar. İstek durumu, onay bekleyen istek, oynatmaya hazır, otomasyon, müzik/yeni yayın, altyazı ve yedekleme olayları ayrı seçilir.
- Mevcut kişisel inbox'a bildirim eklenirken SQLite trigger'ı aynı transaction'da ilgili dış gönderimi oluşturur. `(channel_id,notification_id)` tekilliği tekrarları engeller. Kanal açılmadan önceki inbox geçmişi geriye dönük gönderilmez.
- Her kanalın bağlantısı Data Protection ile şifrelenir. Tam webhook yolu/query, imza sırrı, SMTP kullanıcı/parola, adresler ve isteğe bağlı sertifika pin'i API'ye geri verilmez. Maskeli hedef yalnız host/port/protokol bilgisidir. Boş düzenleme alanları saklı değerleri korur; kaldırma komutları açıktır.
- Kanal türü ve alıcı Home hesabı mevcut kayıtta değiştirilemez. Yeni alıcı/tür için yeni kanal oluşturulur. Bağlantı/filtre/bütçe değişikliği revizyonu artırır ve eski bekleyen/çalışan gönderimleri iptal eder. Silme bağlantı sırlarını kaldırır.
- Kullanıcı yalnız kendi teslim geçmişini görür, kendi gönderimini iptal/tekrar edebilir. Yönetici kanal yapılandırır ve hane geçmişini görür. Başka kullanıcının teslim/deneme kimliği 404 verir. Worker lease token'ı JSON'a yazılmaz.

## Webhook sözleşmesi

`POST` JSON gövdesi: `version=1`, `deliveryId`, `eventId`, `kind`, `title`, `message`, `contentId`, `occurredAt`, `test`. Gövde ve teslim kimliği tekrarlar arasında sabittir. Başlık en fazla 500, ileti 4.000 karakter; kontrol karakterleri ayıklanır.

- `Idempotency-Key`: kalıcı `deliveryId`.
- `X-Home-Event`: olay türü.
- `X-Home-Timestamp`: denemenin Unix saniyesi.
- İmza sırrı seçildiyse `X-Home-Signature: sha256=<hex>`; imza girdisi UTF-8 **`timestamp + "." + özgün JSON byte'ları`**, algoritma HMAC-SHA256. Alıcı timestamp yaşını ve teslim kimliğini denetleyebilir.

Yönlendirmeler izlenmez; cookie veya Home giriş yetkisi gönderilmez. Tam hedef ve yanıt gövdesi loglanmaz. 2xx kabul edilir; 408/425/429/5xx ve ağ/zaman aşımı yeniden denenebilir; diğer durumlar kalıcı hata olarak kaydedilir. `Retry-After` delta/tarih desteklenir ve 5 saniye–24 saat sınırına alınır. Yanıt başlıkları 16 KiB, HTTP istemci süresi 20 saniye ile sınırlıdır; yanıt gövdesi okunmaz.

## SMTP ve TLS

**MailKit/MimeKit 4.18.1** ile MIME text/plain e-posta gönderilir. UTF-8 konu/gövde, SMTP kimliği ve sabit `Message-Id: <deliveryId@home.local>` kullanılır. Gönderen/alıcı tek mailbox olarak doğrulanır; başlık satırına kullanıcı kontrol karakteri geçirilmez.

- Varsayılan **zorunlu STARTTLS**; doğrudan TLS/SMTPS ayrıca seçilebilir. STARTTLS desteği yoksa şifresiz devam edilmez.
- Sistem sertifika güveni varsayılandır. Kullanıcının yerel SMTP sunucusu için isteğe bağlı **64 hex SHA-256 sertifika pin'i** vardır; pin ve geçerlilik tarihleri birlikte doğrulanır. Yanlış pin reddedilir.
- Şifresiz SMTP yalnız açıkça seçilen `localhost`/loopback hedefinde kabul edilir.
- Kullanıcı/parola veya uygulama parolası desteklenir. OAuth/Graph sağlayıcı entegrasyonu bu dilimde uygulanmadı.
- SMTP 4xx geçici, 5xx kalıcı hata olarak ele alınır. Kimlik/TLS hatası kör retry yapmaz. Protokol loglayıcısı kullanılmaz; uzak sunucunun metni/parolalar hata geçmişine taşınmaz.

MailKit'in MimeKit üzerinden getirdiği **System.Security.Cryptography.Pkcs 10.0.0** ve denenmiş 10.0.12, yeni NU1902/NU1903 denetiminde reddedildi. 2026-10-13 tarihli iki resmi advisory ilk düzeltilmiş sürümü **10.0.13** olarak belirtiyor; bu paket açıkça sabitlendi, denetimler bastırılmadı. MailKit/MimeKit ve transitif BouncyCastle 2.7.0 MIT lisansları paketlenir; [envanter](dependencies.md).

## Kuyruk, kota, iptal ve restart

- Kanal başına varsayılan **60 deneme/saat**, ayarlanabilir 1–1.000. Otomatik bildirim, test iletisi, başarısız deneme ve manual retry aynı kalıcı kayan saat bütçesini kullanır. İzin, ağ isteğinden önce DB'ye ayrılır.
- Tek işleyici; ağ işi bütçesi 30 saniye. Aynı kanaldaki çalışan iş claim sırasında dışlanır. Normal backoff 10 saniyeden başlayıp iki kat artar; otomatik toplam en fazla 8, açık kullanıcı retry'siyle toplam en fazla 16 deneme.
- Hesap başına en fazla 8, sunucuda 64 kanal; kanal başına 1.000 bekleyen/çalışan gönderim. Kuyruk dolduğunda inbox yazımı korunur ve kanalın `dropped` sayacı artar. Yedi günden eski bekleyen olaylar iptal edilir.
- Tamamlanmış geçmiş 30 gün/son 10.000 kayıt için periyodik temizlenir. Son bir saatin deneme bütçesini oluşturan kayıtlar o saat dolana kadar korunur. UI/API son 100 teslimi gösterir.
- Kanal revizyonu, alıcı hesap sürümü ve güncel inbox/content erişimi claim ve gönderim sırasında kontrol edilir. Çalışan iş 250 ms aralıkla iptal koşulunu denetler; bağlantı değişikliği ayrıca token'ı iptal eder. Bağlantı sırları aynı revizyonun snapshot'ından çözülür; eski iş yeni hedefe yöneltilemez.
- Restart'ta yarım `sending` işi yeni lease/revizyonla toparlanır; önceki ağ sonucu **bilinmiyor** olarak işaretlenir. Eski worker başarı yazamaz. Aynı webhook idempotency key / SMTP Message-Id tekrar kullanılır. Uzak hedefin kabulünden sonra süreç kesilirse tekrar teslim olabilir; exactly-once iddiası yoktur.
- **Deneme gönder** açık kullanıcı komutudur; otomatik kanal kapalıyken de gerçek test iletisi kuyruğa alınabilir. Test `test=true` ve Türkçe deneme başlığı/gövdesiyle belirtilir.

## Şema ve arayüz

Şema **23 → 24**, `024_notification_delivery.sql`: `notification_channels`, `notification_deliveries`, `notification_delivery_attempts` ve transaction içi outbox trigger'ı. Önceki yayımlanmış migration'lar korunur; DB/anahtar yedeği bu ayar ve kuyruğu da kapsar.

- `/api/notification-channels`: yönetici GET/POST/PUT/DELETE; `/capabilities`, `/{id}/test`.
- `/api/notification-deliveries`: kişisel görünüm; yönetici `?all=true`; `/{id}/attempts`, `/cancel`, `/retry`.
- **Ayarlar → Bildirim kanalları**, ayrıca kişisel bildirim penceresinde **Dış gönderim geçmişi**.
- `NotificationSettings`, `NotificationTransport`, `NotificationDispatcher`, `MediaStore.Notifications`; istemci `NotificationChannels.tsx`.

## Doğrulama

- **15/15 odaklı backend/SMTP/erişim testi**, atlanan 0; TypeScript ve üretim frontend build başarılı.
- Gerçek yerel TCP SMTP sunucusunda STARTTLS ve doğrudan TLS, SASL kimliği, Türkçe MIME, sabit Message-Id; yanlış pin/güvenilmeyen sertifika ve STARTTLS downgrade reddi doğrulandı. Windows fixture sertifikası, Schannel'in geçici anahtar kısıtı nedeniyle PKCS#12'den yüklenir; sistem trust store'una sertifika eklenmez.
- HMAC'ın alıcı tarafı doğrulaması, tekrar gövde/kimliği, Retry-After, test/olay ortak deneme bütçesi, kanal/hesap iptali, stale lease, inbox'a zarar vermeyen kuyruk doluluğu ve uzak hata sırlarının gösterilmemesi geçti.
- **221/221 backend/native test, atlanan 0**, `artifacts/test-results/home-0.20.0-full.trx`. Frontend build geçti; mevcut ses zamanlayıcısının 7 testi de doğrulanmıştır.
- Windows x64/Linux x64/Linux ARM64 paketleri aynı güvenilir anahtarla imzalandı. Windows paketinde `MailKit.dll`/`MimeKit.dll` **4.18.1.0**, `System.Security.Cryptography.Pkcs.dll` **10.0.13+4099f83977f4d29604a9aeb971fb0a7bc1d5f699** dosya sürümleri doğrulandı; lisanslar paket içindedir.
- Gerçek süreçte **0.19.0/şema 23 → 0.20.0/şema 24 → eski sürüm/veri** update/rollback geçti (`artifacts/update-roundtrip-0.19.0-0.20.0.json`). Sahip hesabı korundu, restore eski oturumları iptal etti. Linux runtime kabulü açıktır.
- Canlı önizleme imzalı uygulama içi güncellemeyle 0.20.0/şema 24'e geçti; sahip oturumu korundu. API/DB'de **0 bildirim kanalı, 0 dış teslim denemesi, 0 açık oynatma, 0 indirme ve 0 etkin sentetik kütüphane**; VAD CPU başlangıcı başarılı (`artifacts/live-runtime-v020.json`). Canlı hedefe e-posta/webhook denemesi veya internet medya edinmesi başlatılmadı.
- Browser/cihaz kabulü kullanıcıya aittir; otomatik browser suite çalıştırılmadı.

## Kaynaklar — 2026-10-14

1. Context7 `/jstedfast/mailkit`; kurulu `MailKit 4.18.1/lib/net10.0/MailKit.xml`: ConnectAsync/SecureSocketOptions, AuthenticateAsync, SendAsync, SmtpCommandException.StatusCode ve iptal.
2. [MailKit 4.18.1 NuGet](https://api.nuget.org/v3/registration5-gz-semver2/mailkit/4.18.1.json), MailKit/MimeKit nuspec ve pinned `4.18.1` LICENSE; [BouncyCastle 2.7.0 LICENSE](https://github.com/bcgit/bc-csharp/blob/release-2.7.0/LICENSE.md).
3. [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7), [GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx); NuGet sürüm dizini ve restore denetimi.

## Kullanıcı kabulü — ACCEPT-20

1. Ayarlardan kendi webhook/SMTP hedefini ekle, olayları ve saatlik deneme bütçesini seç. Önce **Deneme gönder** ile kendi hedefinde iletiyi kontrol et.
2. Otomatik iletimi aç; ilgili gerçek istek/altyazı/müzik/backup olayı üretildiğinde teslim geçmişini ve hedefteki sonucu karşılaştır.
3. Kanalı duraklat/düzenle; eski bekleyen işler iptal olmalı. Geçici hedef hatasında retry/backoff ve kullanıcı iptalini kontrol et.

Web/PWA push/VAPID abonelikleri **NOTIFY-01B** üzerinde açık. Diğer cihaz/gerçek hesap, Linux runtime ve ürün kapsamı kapıları [panoda](product/execution.md) izlenir.

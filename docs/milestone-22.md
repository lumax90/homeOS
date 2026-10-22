# 0.22 — Şifreli çevrimdışı cihaz kütüphanesi

Tarih: **2026-10-14**, **0.22.0 yayımlandı**. Gereksinimler: **CLI-02, USR-02/03, PLY-02/03/09, SUB-06, OPS-01/03**.

## Kullanıcı akışı

**Cihazım** ekranında kendi Home hesabı için bir cihaz arşivi oluşturulur. Kullanıcı, yalnız o cihaz arşivini açan en az 8 karakterlik bir parola belirler. Parola Home sunucusuna gönderilmez. Arşiv açıldıktan sonra medya dosyasının ayrıntısındaki **Cihazıma indir** ile profil/ses/altyazı seçilir.

- **Özgün dosya:** kaynak byte'ları korunur; codec desteği cihazın tarayıcısına bağlıdır. Metin altyazısı seçilirse ayrı WebVTT varlığı eklenir.
- **Uyumlu 720p/1080p:** Home sunucusunda H.264/AAC MP4; ses dosyası için AAC stereo MP4. Ses izi seçilebilir. PGS/DVD veya seçilen stil görüntüye işlenebilir; metin ayrı WebVTT olarak da tutulabilir.
- Aktarım Home açıkken sürer. Tamamlanan şifreli parçalar kalıcıdır; pencere kapanması/kilit ve geçici ağ hatasından sonra uygun bağlantıda devam eder. Tarayıcı kapalıyken süresiz arka plan indirme iddiası yoktur.
- Hazır içerik **Cihazdan oynat** ile yerel Range yanıtından açılır. Sunucu erişilemediğinde başlangıç ekranından **Çevrimdışı cihaz kopyalarım** açılabilir. Offline yüzey, sunucu oturumu varmış gibi davranmaz.

## Sunucu hazırlığı ve dosya sınırı

- Kaynak klasörler salt okunur. Kullanıcı komutu `offline_exports` içinde kalıcı, revision/lease korumalı iş oluşturur. Kütüphane/yaş erişimi, dosya kimliği, kaynak fingerprint'i, eşleşme ve politika revizyonu denetlenir.
- Özgün dosya için sunucuda ikinci medya kopyası oluşturulmaz. Uyumlu çıktı ve WebVTT yalnız `.data/offline-exports/<id>` Home işaretli cache alanına yazılır.
- FFmpeg video/ses/altyazı argümanları mevcut oynatıcıyla ortaklaştırılmıştır. HDR dönüşümü mevcut doğrulanmış tone-map koşullarına uyar; desteklenmeyen DV/profil reddedilir. NVENC başlangıç hatasında yazılım fallback vardır.
- MP4 `faststart`, seçilen boyut ve bit hızı kullanılır. FFmpeg çıktı boyutu sınırı yanında gerçek çıktı süre/codec denetimi yapılır; yarım çıktı hazır sayılmaz. Eski lease yayın yapamaz. Kesilen hazırlık yeniden başlar, Home'un kendi yarım cache'i temizlenir.
- Aktarım **2 MiB parça** halinde; her parça sunucuda SHA-256 ile işaretlenir, istemci hash/uzunluk/fingerprint'i doğrular. Yanlış dosya sürümüne ait parça kaldığı yerden devam işlemine karıştırılmaz. Bu hash, o aktarım parçasının bütünlüğünü gösterir; kullanıcı kaynak dosyasının önceden bilinmeyen içeriğine dair harici doğrulama iddiası değildir.
- İzinler hesaba ve korumalı tarayıcı cihazı kaydına bağlıdır. İndirme isteğindeki beklenen hesap/giriş, arada profil değişmişse reddedilir. Online parça/progress uçları cookie + yetki sınırını kullanır; başka cihaz grant ID'sini kullanamaz.

## Şifreli cihaz arşivi

- Arşiv anahtarı rastgele **AES-256-GCM** anahtarıdır. Paroladan **PBKDF2-HMAC-SHA-256 / 600.000 tur / 16-byte salt** ile türetilen anahtarla sarılır. Kullanım anahtarı dışa aktarılamaz; kalıcı depolamada açık halde tutulmaz.
- Başlık/manifest/konum verisi ve her medya parçası AES-GCM ile ayrı şifrelenir. Her kayıtta 12-byte rastgele IV ve 128-bit tag vardır. AAD, arşiv/hesap/cihaz ve parça/asset-fingerprint kimliğini bağlar; yanlış arşiv, sıralama veya dosya sürümüyle çözme reddedilir.
- IndexedDB'de parça ve ilerleme sayacı aynı transaction'la yazılır. Kota hatasında ikisi de geri alınır. İptal/kilit/revision değişikliği geç gelen parça commit'ini engeller. Hazır durumu ancak tüm kayıtlar mevcutsa verilir.
- Cihaz kotası varsayılan **8 GiB**, UI aralığı 256 MiB–64 GiB. Aynı origin/scope'taki arşivler ortak rezervasyon sayacını kullanır; bekleyen dosya da yer ayırır. Tarayıcının `storage.estimate()` değeri yaklaşık kapasitedir; `QuotaExceededError` ayrıca ele alınır. İsteğe bağlı kalıcı depolama izni kullanıcı komutudur.
- Parolayı unutmak, o yerel anahtarla şifrelenmiş cihaz kopyalarının yeniden indirilmesini gerektirir. Sunucudaki medya dosyaları ve hesap parolası etkilenmez.

## Yerel oynatma ve hesap ayrımı

- Service worker yalnız scope altındaki `/api/offline/local/<vault>/<entry>/<asset>` GET/HEAD isteklerini karşılar; sunucu bu sanal adresten medya vermez.
- Her istekte gerçek `FetchEvent.clientId` üzerinden ilgili açık Home penceresine erişim sorulur. Anahtar, yalnız kilidi açık ve doğru arşivi tutan pencerenin MessagePort cevabından alınır. Worker yeniden başlasa da açık pencereden yeniden alınabilir; kalıcı anahtar cache'i yoktur.
- Tek Range, suffix, HEAD ve 416 davranışı vardır. Veri bir parça çözülerek geri basınçlı `ReadableStream` ile verilir; bütün film RAM'e alınmaz. Kilit/iptal mesajı devam eden çözmeyi durdurur. Önceden tarayıcıya verilmiş byte'lar geri çekilemez.
- Yerel arşiv kilidi sekmelere bildirilir; içerik adları kilitli arayüzde gösterilmez. Arşiv parolası doğru olsa da çevrimiçi başka Home hesabı açıkken o arşiv açılmaz. Bilinen sunucu erişim iptali cihaz verilerini temizler.
- Ağ yokken uygulama son doğrulanmış offline iznin süresini uygular. Uzak yetki iptalinin bağlantısız cihaza anında ulaştırılması veya DRM/işletim sistemi yöneticisine karşı koruma iddiası yoktur.

## Konum eşitleme

- Çevrimdışı konum, şifreli metadata içinde tek bir son olay olarak biriktirilir. Event ID/sequence ve grant kimliği korunur.
- Sunucu, dosya ve paylaşılan film/bölüm konumlarının bir stamp'ini verir. Başka cihaz bu kapsamı değiştirdiyse eski offline konum otomatik üstüne yazılmaz. UI **Cihazdakini kullan / Sunucudakini tut** seçimini gösterir.
- Kabul edilmiş konum ve idempotent receipt aynı SQLite transaction'ında yazılır. Geç yanıt daha yeni yerel konumu silmez; tekrar aynı olayı uygulamaz. Eşleşme/erişim değişmişse eski konum yeni içeriğe yazılamaz.
- Bir kaynak diskin geçici yokluğu tamamlanmış cihaz iznini silmez. Açık ACL/politika/katalog kimliği değişiklikleri ayrı değerlendirilir. Cihaz lease'i uygun çevrimiçi hesapla yenilenebilir.

## Sınırlar

- Sunucu hesap varsayılanı 50 dosya / 64 GiB / 7 gün; yönetici 1–100 dosya, 256 MiB–256 GiB ve 1–7 gün seçebilir.
- Tek hazırlama worker'ı; 32 bekleyen/çalışan hazırlık, dosya başına 32 GiB, dönüştürme için 4 saat medya ve 1 saat iş bütçesi. Sunucu dönüştürme cache'i 16 GiB; 6 global / cihaz başına 2 online parça okuması.
- İstemci transferi tek yazıcı Web Lock ve kalıcı parçalarla yürür. Son bir parça okunurken durdurma olabilir; tam dosyanın sınırsız kopyası tutulmaz.
- Cihaz deposunda en fazla 200 kayıt, arşiv başına 100 kayıt. Varsayılan kuyruk kendi depolama rezervasyonundan önce ağ edinmesi başlatmaz.
- Hazırlık cache'i sona ermişse eksik cihaz aktarımı yeni manifest/sürümle yeniden hazırlanır; eski parçalar karıştırılmaz. Tamamlanmış cihaz kopyası sunucu cache'inin tutulmasına bağımlı değildir.

## Şema ve kod

Şema **25 → 26**, `026_offline_media.sql`: `user_offline_policies`, `offline_exports`, `offline_progress_receipts`. Önceki yayımlanmış migration'lar korunur.

- `/api/offline/device`, `/offline/media/{id}`, `/offline/exports/{id}` ve manifest/chunks/renew/retry/cancel/progress.
- `/api/users/{id}/offline-policy` yönetici politikası.
- `OfflineService`, `MediaStore.Offline`, ortak encoding argümanları ve transaction'a bağlı progress receipt.
- `web/src/offline/` şifreleme/IndexedDB/controller/Range/sync; `web/workers/offline.ts`; `Offline.tsx` kullanıcı yüzeyi. Worker, Vite JS API ile ayrı IIFE olarak derlenip PWA worker'ına eklenir.

## Doğrulama

- **13 odaklı backend/native kontrolü**, 0 atlanan: özgün byte'lar + SHA-256 + WebVTT, gerçek 720p H.264/AAC/altyazı pikselleri, gerçek AAC müzik çıktısı; kaynak hash korunumu, cihaz/CSRF, değişen kaynak, lease/restart, kota ve çapraz cihaz konum çatışması.
- **9 şifreli depo/Range/progress testi** ve **3 üretim controller testi**, Node üzerinde `fake-indexeddb 6.2.5` ile: AES/AAD/yanlış parola, atomik kota, write sonrası quota-fault rollback, paused resume, cross-chunk seek/HEAD/416, kilitte abort, tam aktarım ve bilinen iptalde temizleme.
- Frontend + ayrı worker TypeScript ve üretim build, üretilmiş worker sözdizimi başarılı. Browser suite çalıştırılmadı; gerçek tarayıcı depolama/medya kabulü kullanıcıya aittir.
- **248/248 backend/native test, atlanan 0**, `artifacts/test-results/home-0.22.0-full.trx`; offline 12 + push 8 + ses 7 olmak üzere **27/27 Node testi** aynı yayın kodunda geçti.
- Windows x64, Linux x64, Linux ARM64 imzalı paketleri üretildi. Gerçek süreçte **0.21.0/şema 25 → 0.22.0/şema 26 → önceki sürüm/veri** update/rollback geçti; hesap korundu, restore eski oturumları iptal etti (`artifacts/update-roundtrip-0.21.0-0.22.0.json`). Linux runtime kabulü açıktır.
- Canlı önizleme imzalı güncellemeyle 0.22.0/şema 26'ya geçti; sahip oturumu korundu. Offline worker sunuluyor; test bağımlılığı worker içinde yok. API/DB'de **0 offline export, 0 progress receipt, 0 oynatma, 0 indirme, 0 etkin sentetik kütüphane ve 0 FK ihlali**; VAD CPU kullanılabilir (`artifacts/live-runtime-v022.json`).
- Geliştirme doğrulamasında kullanıcı medyası/internet edinmesi veya canlı cihaz indirmesi başlatılmadı. Gerçek browser/cihaz kabulü kullanıcı sonucunu bekler.

## Kaynaklar — 2026-10-14

1. Context7 `/vitejs/vite`: `build()` inline config, `write:false`, IIFE library mode; kurulu Vite 8.3.4 tipleri.
2. Context7 `/dumbmatter/fakeindexeddb`, npm 6.2.5 metadata ve pinned Apache-2.0 lisansı. Yalnız test bağımlılığı; runtime bundle'ına girmez.
3. [MDN SubtleCrypto.deriveKey](https://developer.mozilla.org/en-US/docs/Web/API/SubtleCrypto/deriveKey), [StorageManager.estimate](https://developer.mozilla.org/en-US/docs/Web/API/StorageManager/estimate), kurulu DOM/WebWorker tipleri; PBKDF2/AES ve yaklaşık kota.
4. Context7 `/websites/ffmpeg_documentation`: MP4/progress pipe, `-fs` sınırının hafif aşılabilmesi; bu yüzden ek boyut/süre/probe doğrulaması. Mevcut FFmpeg 9.0.2 native çıktı.

## Kullanıcı kabulü — ACCEPT-22

1. HTTPS Home/PWA'da **Cihazım** arşivini oluştur/aç; küçük gerçek bir dosyayı ayrıntısından indir. Özgün ve uyumlu seçeneklerini dene.
2. Aktarımı duraklat, Home'u kapat-aç ve devam et; tamamlanan parçalar korunmalı. Cihaz kotası doluluğunu ve kalıcı depolama iznini kontrol et.
3. Ağı kapatıp cihaz kopyasını oynat/ileri sar; varsa altyazıyı dene. Arşivi kilitlediğinde açık yerel akış durmalı.
4. Başka cihazda konumu değiştir; yeniden bağlandığında çakışma seçimini ve kişisel hesabını kontrol et. Profil değiştirip diğer arşivin parolasız açılmadığını doğrula.

Senkron odalar/cast, geniş medya/TV istemcileri ve kalan ürün kapıları [panoda](product/execution.md) sürer.

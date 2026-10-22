# Uygulama durumu

Son doğrulama: **2026-10-17**. Çalışma adı **Home**, çalışan önizleme **0.27.0 / şema 39** (kaynak/paket etiketi 0.27.0-dev.1); son tam regresyonlu yayın **0.26.0**.

## 0.27.0-dev.1 — birleşik geliştirme ve çalışan önizleme

Kullanıcının 2026-10-16 kararıyla kalan yedi alan ara paket/yayın turları yerine tek geliştirme çalışmasında kodlandı. Kişi/listeden koleksiyon, yerel ASR/bölgesel senkron, cihaz kodu/Cast/AirPlay/DLNA, atlama işaretleri, log/ölçüm/bakım/config, doğrudan kaynak/SDK ve Live TV/DVR/raflar/podcast arayüzleri eklendi. Güncel şema **39**, yeni migration'lar **031–039**. Yaşam döngüsü entegrasyonu DVR atomik yayın/restart, kalıcı alıcı kumandası, gerçek DLNA konumu, sabit kopyalar/komut makbuzları ve kaynak revizyonu/proxy yanıt ömrünü kapsar; ardından tür keşfi ve hazır kaynak kurulumu kullanıcı geri bildirimiyle tamamlandı. [Ayrıntılı durum](development-completion.md).

Frontend/worker ve .NET derlemeleri, worker sözdizimi ve son kaynak diliminde **42/42 odaklı backend kontrolü** geçti (`artifacts/test-results/source-catalog-v027-complete.trx`, 0 atlanan; önceki 30 entegrasyon kontrolü dahildir). **2026-10-17:** gerçek yedekte 30→39 veri korunumu ve ayrı ortamda 0.26→0.27→0.26 update/rollback sonrası Windows önizleme güncellendi. Mevcut 1 istek/1 içerik ve sahip oturumu korundu; yeni kaynak sihirbazı/12 profil ve tür filtresi canlıda sunuluyor. [Etkin release, yedek ve kanıtlar](preview-027.md). Geniş native/regresyon ve gerçek cihaz/kaynak kabulü açıktır.

2026-10-16 kullanıcı geri bildirimiyle **DISC-03 / CAT-02** tür keşfi tamamlandı: TMDB'nin ayrı film/dizi tür listeleri, `with_genres` tabanlı keşif ve tür + başlık aramasında sınırlı sayfa/cursor takibi. Frontend/worker/PWA ve .NET derlemeleri ile **8/8 metadata/API kontrolü** geçti (`artifacts/test-results/genre-discovery-v027.trx`); 2026-10-17'de önizlemeye yayımlandı.

**SOURCE-02 / IDX-07/OPS-02** kaynak ekleme geri bildirimi uygulandı: 12 hazır profil, filtreli site seçimi, otomatik adres/protokol, gerektiğinde API anahtarı, yalıtılmış bağlantı testi ve atomik ekleme/düzenleme. YTS/EZTV/API Bay/Torrents.csv doğrudan adaptörleri eklendi; 6 doğrudan kaynakta gerçek metadata/RSS ve 6 üyelikli kaynakta caps Home ayrıştırıcısıyla doğrulandı. Hesaplı arama/edinme ve kullanıcı browser kabulü açık. [Kapsam ve kanıt](source-catalog.md).

## 0.26 son tam regresyonlu yayın / geri dönüş

**MUSIC-04** tamamlandı: kimlikli çoklu sanatçı/konuk katkıları ve Sanatçılarım gezintisi; MusicBrainz koleksiyonu veya Home çalma listesinden kalıcı, seçimli toplu takip; ELRC kelime zamanı ve sözden seek; gerçek albüm/recording ilişkileri ve yerel müzik videosu bağlantıları; Ogg/Vorbis, Opus, WAV, AIFF dahil yönetilen etiket/kapak/tarih/rename/undo. [0.26 sözleşmesi](milestone-26.md).

**326/326 backend/native, atlanan 0; 40/40 Node** testi, frontend/worker build ve worker sözdizimi geçti (`artifacts/test-results/home-0.26.0-full.trx`). Native dokuz uzantıda çoklu MBID/ad/tarih/kapak, ses-paket hash'i ve tam-byte undo; metadata önizleme/yayın revizyonu, liste restart/iptal/kota/hesap/CSRF ve eski müzik kayıtlarının korunumu doğrulandı. Native testin yakaladığı AIFF ID3 ad bölme ve Ogg `N×255` sıfır-segment sorunları düzeltildi; ilk failure kanıtları `artifacts/test-results/music-026-*.trx` altında korunuyor.

Üç platformun imzalı **0.26.0** paketleri ve **0.25.0/şema 29 → 0.26.0/şema 30 → önceki veri/sürüm** update/rollback geçti (`artifacts/update-roundtrip-0.25.0-0.26.0.json`). Kapanmış önizleme kayıtlı 0.25.0 paketiyle yeniden açılıp imzalı güncellendi; sahip oturumu korundu. Canlı **http://localhost:5080**, PID **8228**, release `.data/preview/updates/releases/32af901765ec42dc9d697fd6957910ec`; veri `.data/preview`, şema **30**.

Canlıda **0 indirme/oynatma/oda, 0 müzik liste işi/video bağlantısı, 0 etkin sentetik kütüphane, 0 FK ihlali**; VAD CPU, güncel müzik/katalog istemcisi ve offline worker kullanılabilir (`artifacts/live-runtime-v026.json`). Gerçek MusicBrainz Radiohead **107 albüm grubu**, OK Computer **12 parçalı yayın**, Airbag recording ve ilişkisi okundu; geçici 429 bounded backoff ile ele alındı (`artifacts/live-music-v026.json`). Medya edinmesi ve oynatma başlatılmadı.

Paketler `artifacts/packages/home-0.26.0-{win-x64,linux-x64,linux-arm64}.zip`; geri dönüş **0.25.0**, yedek `b3455fcd3ae141dc991cbc4db5d60365.zip`. Gerçek kaynak/cihaz/koleksiyon kabulü **ACCEPT-26**, Linux/Docker ve kalan tam ürün işleri [panoda](product/execution.md) açıktır.

## 0.25 güncel yayın

**CATALOG-04** tamamlandı: birden-çoğa sağlayıcı bölüm karşılıkları; kalıcı bölüm ID'sini/dosya/istek/kişisel konumu koruyan sağlayıcı numara ve ID onarımı; saklanan DVD/dijital/özel sıraların dosya eşleme, kaynak arama ve import snapshot'ına bağlanması; gerçek dosyadaki sahne/siyah görüntü/sessizlikten bölüm sınırı önerisi. [0.25 sözleşmesi](milestone-25.md).

**296/296 backend/native, atlanan 0; 38/38 Node** kontrolü, frontend/worker build ve worker sözdizimi geçti. Native görüntü/ses sınırı, per-episode watched eşiği, numara takasında ayrı istekler, eski provider ID reddi, kaydedilmiş sıraların birleştirmede korunması, API/CSRF ve composite-FK migration doğrulandı (`artifacts/test-results/home-0.25.0-full.trx`).

Üç platformun imzalı **0.25.0** paketleri ve **0.24.0/şema 28 → 0.25.0/şema 29 → önceki veri/sürüm** update/rollback geçti (`artifacts/update-roundtrip-0.24.0-0.25.0.json`). Canlı **http://localhost:5080**, PID **9452**, release `.data/preview/updates/releases/043a5825997d43b08654fb85884a45e7`; veri `.data/preview`, şema **29**. Sahip oturumu korundu.

Canlı katalog istemcisi ve önceki offline worker sunuluyor; **0 bölüm planı/sıra/sınır işi, 0 indirme/oda/oynatma, 0 etkin sentetik kütüphane, 0 FK ihlali**; VAD CPU kullanılabilir. Doğrulama canlı katalog değişikliği, medya edinmesi veya oynatma başlatmadı (`artifacts/live-runtime-v025.json`).

Paketler `artifacts/packages/home-0.25.0-{win-x64,linux-x64,linux-arm64}.zip`; geri dönüş 0.24.0 + `3e47a358c61d41adaa45b6fb170882e1.zip` yedeği. Gerçek katalog/cihaz kabulü ve kalan ürün işleri [panoda](product/execution.md) açıktır.

## 0.24 güncel yayın

**ACCOUNT-BUDGETS-01** tamamlandı: kalıcı indirme sahibi, aylık yeni edinme ve doğrulanmış paket boyutu bütçesi, eşzamanlı aktarım, üye/yönetici görünürlüğü, tüketim/geçmiş ve kota bekleme/devam akışı. Manuel ve video/müzik otomasyonu aynı bütçeyi kullanır; duplicate/resume ikinci borç yaratmaz, staging temizliği yeni paket nesli oluşturur. [0.24 sözleşmesi](milestone-24.md).

**277/277 backend/native, atlanan 0; 38/38 Node**, frontend/worker build ve worker sözdizimi geçti. Gerçek loopback torrent/magnet/NNTP, payload öncesi kota, yetki/CSRF, atomik rezervasyon, UTC ay sınırı, kapanış sayaçları ve gerçek süreç çökmesi doğrulandı. Genel kontrolün ortaya çıkardığı FFmpeg terminal-durum yarışı `PlaybackLifecycle` ile düzeltildi; dört sıralama/durum ve üç native altyazı kontrolü sonrası genel testler geçti. Kanıt `artifacts/test-results/home-0.24.0-full.trx`; ilk bulgu `home-0.24.0-pre-lifecycle.trx` içinde kayıtlıdır.

Üç imzalı platform paketi ve **0.23.1/şema 27 → 0.24.0/şema 28 → önceki sürüm/veri** update/rollback geçti (`artifacts/update-roundtrip-0.23.1-0.24.0.json`). Canlı önizleme başlangıçta kapalıydı; kayıtlı 0.23.1 açılıp imzalı güncelleme uygulandı. **http://localhost:5080**, PID **11004**, release `.data/preview/updates/releases/087253bde30d460cbfd258afaffe89eb`; veri `.data/preview`, şema **28**. Sahip oturumu korundu.

Canlı bütçe/istemci ve önceki oda/offline yolları yanıt veriyor. **0 indirme, 0 aktif bütçe run'ı, 0 bütçe dönem/override kaydı, 0 oda/oynatma, 0 etkin sentetik kütüphane, 0 FK ihlali**; VAD CPU kullanılabilir. Kanıt `artifacts/live-runtime-v024.json`. Varsayılan aylık bütçeler sınırsız, hesap başına eşzamanlı sınır 3; gerçek medya edinmesi doğrulama tarafından başlatılmadı.

Paketler `artifacts/packages/home-0.24.0-{win-x64,linux-x64,linux-arm64}.zip`; geri dönüş 0.23.1 + `14c2741cab2d41f9a3bf502dddf4e7bb.zip` yedeği. Yayın araçları `tools/update-live.ps1` / `tools/verify-live.mjs`. Gerçek kaynak/cihaz kabulü ve kalan kapsam [panoda](product/execution.md) açıktır.

## 0.23.1 güncel yayın

**Birlikte** ekranı, hesap davetli izleme/dinleme odaları ve kendi açık web oynatıcısına uzak kumanda eklendi. Ortak sunucu zaman çizelgesi, hazır bariyeri, oynat/pause/seek/hız, cihazlara özel ses/altyazı/kalite ve tüm katılımcılara yetkili dosya değişimi vardır. Kumanda ekranı ek oynatma slot'u tüketmez; her renderer kendi yetki/kotasını kullanır. [0.23 sözleşmesi](milestone-23.md).

Oda/istemci/komut receipt'i kalıcıdır. Gecikmiş join/komut/heartbeat ve eski source-epoch engellenir. Oda kapanışı henüz tamamlanmamış kalite hazırlığını da iptal eder. Gerçek restart/çökme sonrası oda duraklatılarak döner. **0.23.1** istemci düzeltmesi, aynı epoch içinde kota veya elle kapatılmış lease için açık kullanıcı yeniden denemesini korur.

**259/259 backend/native, atlanan 0** 0.23 ana yayın koşusunda geçti (`artifacts/test-results/home-0.23.0-full.trx`). **38 Node kontrolü**: oda 11, offline 12, push 8, ses 7. Bakım düzeltmesi sonrası 11/11 oda testi, frontend/worker build ve 0.23.1'in yayımlanmış süreç restart/çökme kontrolü başarılı (`home-0.23.1-published-room.trx`). Migration testi şema-26 offline grant/receipt/politika ve playback geçmişini korudu.

Üç platformun imzalı **0.23.1** paketleri üretildi. **0.22.0/şema 26 → 0.23.0/şema 27 → önceki sürüm/veri** ve **0.23.0 → 0.23.1 → önceki sürüm/veri** update/rollback geçti. Canlı **http://localhost:5080**, PID **17668**, etkin release `.data/preview/updates/releases/1ee04f3fa8d843a8b3ded811239fa0d2`; veri `.data/preview`, şema **27**. Sahip oturumu korundu. Oda istemcisi ve offline worker sunuluyor; **0 oda/komut/oda istemcisi, 0 oynatma, 0 indirme, 0 etkin sentetik kütüphane, 0 FK ihlali**; VAD CPU kullanılabilir. Kanıt `artifacts/live-runtime-v0231.json`.

Paketler `artifacts/packages/home-0.23.1-{win-x64,linux-x64,linux-arm64}.zip`; geri dönüş 0.23.0 + `9b03ed3e1ae94548851e190c6ba9aa52.zip` yedeği. Gerçek cihazda oda/kumanda kabulü, cast/native istemciler ve diğer ana işler [panoda](product/execution.md) açıktır.

## 0.22 güncel yayın

**Cihazım** şifreli offline arşivi, parola ile yerel anahtar açma, özgün veya H.264/AAC MP4 hazırlığı, ses/altyazı seçimi, 2 MiB hash doğrulamalı devamlı aktarım, yerel kota ve Range oynatma eklendi. İzleme konumu, bağlantıda diğer cihazın daha yeni durumunu ezmeden ve idempotent receipt ile eşitlenir. [0.22 sözleşmesi](milestone-22.md).

**248/248 backend/native ve 27/27 Node testi, atlanan 0**; frontend + ayrı worker TypeScript/üretim build ve sözdizimi geçti. Node toplamı offline 12, push 8, ses 7'dir. Gerçek MP4/AAC/altyazı pikselleri, kaynak hash'leri, AES/AAD, IndexedDB quota-fault rollback, cross-chunk seek/HEAD/416 ve üretim aktarım controller'ı doğrulandı. fake-indexeddb 6.2.5 Apache-2.0 yalnız testte kullanılır.

Üç imzalı paket ve **0.21.0/şema 25 → 0.22.0/şema 26 → önceki sürüm/veri** update/rollback geçti. Canlı **http://localhost:5080**, PID **7720**, etkin release `.data/preview/updates/releases/d5b87854fff74759a102daa1213d150f`; veri `.data/preview`. Sahip oturumu korundu. Canlı offline worker sunuluyor; 0 export/progress receipt, 0 oynatma, 0 indirme, 0 etkin sentetik kütüphane, 0 FK ihlali; VAD CPU kullanılabilir. Kullanıcı cihazına medya indirmesi başlatılmadı.

Kanıtlar `artifacts/test-results/home-0.22.0-full.trx`, `artifacts/update-roundtrip-0.21.0-0.22.0.json`, `artifacts/live-runtime-v022.json`; paketler `artifacts/packages/home-0.22.0-{win-x64,linux-x64,linux-arm64}.zip`. Geri dönüş 0.21.0 + `d4441a0c01bd4287ae707e23f9a0c950.zip` yedeği. Gerçek offline/PWA cihaz kabulü, senkron odalar/cast ve diğer ana işler [panoda](product/execution.md) sürer.

## 0.21 güncel yayın

Web/PWA push: yönetici VAPID ayarı, kullanıcı izni/olay filtreleri, giriş ve korumalı cihaz kaydına bağlı abonelik, aynı outbox/kota ve şifreli mesaj eklendi. Genel kilit ekranı bildirimi ve güncel hesabın inbox'ına tıklama; eski profil/çıkış/anahtar dönüşü ve worker güncelleme/yenileme akışları uygulandı. [0.21 sözleşmesi](milestone-21.md).

**240/240 backend/native, atlanan 0; 8/8 push worker testi**, frontend build ve üretilen worker sözdizimi başarılı. VAPID imzası ve RFC 8291 ECDH/HKDF/AES-GCM paketi bağımsız alıcıda denetlendi. Şema 24 kanal/delivery/attempt kayıtları korundu; özel endpoint/yanlış curve/CSRF, kullanıcı geçişi, kota ve stale komut testleri geçti. Yeni iki MIT bağımlılığın kaynak/lisansları paket içindedir.

Üç imzalı paket ve **0.20.0/şema 24 → 0.21.0/şema 25 → önceki sürüm/veri** roundtrip geçti. Canlı **http://localhost:5080**, PID **16832**, etkin release `.data/preview/updates/releases/4fed737b144349d2a2e2341821484517`; veri `.data/preview`. Güncellemede sahip oturumu korundu. Canlı FK ihlali 0; push worker sunuluyor, VAD CPU kullanılabilir. Push kapalı; 0 VAPID/abonelik, 0 teslim denemesi, 0 oynatma, 0 indirme ve 0 etkin sentetik kütüphane. Gerçek push kaydı/mesajı veya medya edinmesi doğrulama tarafından başlatılmadı.

Kanıtlar `artifacts/test-results/home-0.21.0-full.trx`, `artifacts/update-roundtrip-0.20.0-0.21.0.json`, `artifacts/live-runtime-v021.json`; paketler `artifacts/packages/home-0.21.0-{win-x64,linux-x64,linux-arm64}.zip`. Canlı geri dönüş 0.20.0 + `83cb0340199246f1acc79672f97eb38d.zip` yedeği. Gerçek gateway/cihaz push kabulü, Linux runtime ve kalan ana işler [panoda](product/execution.md) açıktır.

## 0.20 güncel yayın

Hesaba bağlı webhook ve SMTP bildirim kanalları, olay filtreleri, transaction içi outbox, şifreli bağlantı ayarları, saatlik deneme bütçesi, iptal/retry/restart ve teslim geçmişi eklendi. Webhook HMAC/idempotency; SMTP zorunlu STARTTLS veya doğrudan TLS, sistem güveni/isteğe bağlı sertifika pin'i ve Türkçe MIME destekler. [0.20 sözleşmesi](milestone-20.md).

**221/221 backend/native test, atlanan 0** ve frontend build geçti. Yerel gerçek SMTP TLS/STARTTLS/SASL, yanlış/güvenilmeyen sertifika reddi, webhook imzası, kota/retry, işlem iptali ve veri izolasyonu doğrulandı. Önceki ses zamanlayıcı doğrulaması 7/7. MailKit/MimeKit 4.18.1, BouncyCastle 2.7.0 MIT lisansları paketlenir; yeni advisory'ler nedeniyle PKCS **10.0.13** açıkça sabitlendi ve yayımlanmış DLL sürümü denetlendi.

Üç imzalı paket ve **0.19.0/şema 23 → 0.20.0/şema 24 → eski sürüm/veri** update/rollback başarılı. Canlı **http://localhost:5080**, PID **19860**, etkin release `.data/preview/updates/releases/9d93371f30db4d9d89aba23389947857`; veri `.data/preview`. Sahip oturumu güncellemede korundu. API/DB'de 0 bildirim kanalı/teslim denemesi, 0 açık oynatma, 0 indirme ve 0 etkin sentetik kütüphane; VAD CPU başlangıcı başarılı. Canlı hedefe bildirim veya internet medya edinmesi başlatılmadı.

Kanıtlar `artifacts/test-results/home-0.20.0-full.trx`, `artifacts/update-roundtrip-0.19.0-0.20.0.json`, `artifacts/live-runtime-v020.json`; paketler `artifacts/packages/home-0.20.0-{win-x64,linux-x64,linux-arm64}.zip`. Canlı geri dönüş 0.19.0 + `c7d1fd793ac04b3896bf6f7c00adff21.zip` yedeği. Web/PWA push, gerçek hedef/cihaz kabulü, Linux runtime ve diğer ürün işleri [panoda](product/execution.md) açıktır.

## 0.19 güncel yayın

Direct/HLS/PCM için ortak, girişe bağlı oynatma oturumu; hesap/etkin cihaz/toplam sunucu kotası, korumalı tarayıcı cihazı, revizyonlu sınır düzenleme ve kişisel/yönetici oturum geçmişi eklendi. Açık HTTP yanıtı ve hazırlanmakta olan medya iptal edilebilir; kaynaklar salt okunur. Web belge kimliği video/müzik/özgün oynatıcı geçişinde korunur. [0.19 sözleşmesi](milestone-19.md).

**207/207 backend/native test, atlanan 0; 7/7 ses zamanlayıcı testi** ve frontend build geçti. Gerçek Kestrel normal restart ve süreç çökmesinden sonra sınırları/hesabı korudu; eski stream adresleri/komutları reddedildi. Üç imzalı paket ve **0.18.0/şema 22 → 0.19.0/şema 23 → eski sürüm/veri** update/rollback başarılı. Kanıtlar `artifacts/test-results/home-0.19.0-full.trx`, `artifacts/update-roundtrip-0.18.0-0.19.0.json`, `artifacts/live-runtime-v019.json`.

Canlı **http://localhost:5080**, PID **14540**, etkin release `.data/preview/updates/releases/308eb5e34b4148cf9915aafcd9f4dc2d`; veri `.data/preview`. Sahip oturumu imzalı güncellemede korundu. Canlı API/DB'de 0 açık oynatma, 0 indirme ve 0 etkin sentetik kütüphane; VAD CPU'da kullanılabilir. Varsayılanlar sunucu 16, hesap 3 oynatma/3 etkin cihaz, cihaz 2. Medya oynatma/indirme veya analiz işi doğrulama tarafından başlatılmadı.

Paketler `artifacts/packages/home-0.19.0-{win-x64,linux-x64,linux-arm64}.zip`; canlı geri dönüş 0.18.0 + `f997607f18fe47da976b487dc93339b8.zip` yedeği. Kullanıcının cihaz kabulü, kişi bazlı edinme bütçeleri, diğer ürün işleri ve Linux/ARM64/Docker çalıştırma kapıları [panoda](product/execution.md) sürer.

## 0.18 güncel yayın

Dosya bazlı altyazı profili, puan/cutoff yükseltmesi, sürüm geçmişi/geri alma, korumalı geçici indirme bileti ve kota/lease revizyonları eklendi. PGS/DVD resim altyazısı ve ASS stili H.264/HLS görüntüye işlenebilir; delay ve cue içinden seek gerçek pikselle doğrulandı. Silero VAD 6.2.3 + ONNX Runtime 1.31.0 ile yerel konuşma etkinliği, offset/fps önerisi ve yeni WebVTT sürümü uygulanmıştır; ASR/metin üretimi açık kapsamdır. [0.18](milestone-18.md).

**197/197 backend/native test, 0 atlanan; 5/5 ses zamanlayıcı testi** ve frontend build başarılı. Üç imzalı platform paketi üretildi. Gerçek Windows sürecinde **0.17.0/şema 20 → 0.18.0/şema 22 → önceki sürüm/veri** roundtrip geçti. Canlı önizleme **http://localhost:5080**, PID **6240**, etkin paket `.data/preview/updates/releases/bb96d9455e494a849e0807f757b64730`; veri `.data/preview`. Başlangıçta kapalı olan önizleme önce kayıtlı 0.17 sürümüyle açıldı, sonra imzalı uygulama içi güncelleme uygulandı. Sahip oturumu korundu.

Canlı VAD modeli **CPU'da kullanılabilir**, DB şema 22. OpenSubtitles bağlantısı yapılandırılmamış; otomasyon kapalı. **0 medya indirmesi, 0 sağlayıcı indirme denemesi, 0 sync işi, 0 etkin sentetik kütüphane**. Doğrulama kullanıcı medyası/altyazısı indirmedi ve medya analiz işi başlatmadı. Kanıtlar `artifacts/test-results/home-0.18.0-full.trx`, `artifacts/update-roundtrip-0.17.0-0.18.0.json`, `artifacts/live-runtime-v018.json`.

Paketler `artifacts/packages/home-0.18.0-{win-x64,linux-x64,linux-arm64}.zip`; model, MIT lisansları ve ORT third-party notices dahildir. Canlı geri dönüş 0.17.0 + `e2fc5c08df694bda98fb5a2931ce1ef5.zip` yedeğidir. Linux/ARM64/Docker çalıştırma, gerçek hesap/cihaz ve kalan ürün kapsamı [panoda](product/execution.md) açıktır.

## 0.17 güncel yayın

Kanonik sağlayıcı alias/birleştirme ve alternatif adlar, çok bölümlü dosya/zaman üyeliği, sıralı film parçaları ve tek film konumu, günlük yayın tarihi ve açık mutlak/anime haritası eklendi. Kaynak arama/import ve otomasyon kapsamları yeni üyeliklere bağlıdır; geç arama, eski oynatıcı ve eski sağlayıcı altyazısı ilişki revizyonuyla engellenir. **185/185 backend/native, 0 atlanan; 5/5 ses zamanlayıcı testi**. [0.17 sözleşmesi](milestone-17.md).

Frontend ve üç imzalı platform paketi üretildi. Gerçek süreçte **0.16.0/şema 19 → 0.17.0/şema 20 → eski sürüm/veri** roundtrip geçti. Canlı önizleme **http://localhost:5080**, PID **7984**, etkin paket `.data/preview/updates/releases/8e0b0c597c7647728da7bf1d582e15bf`; veri `.data/preview`. Geri dönüş 0.16.0 + `a19cdea1e8424832a44d57e3b642d21c.zip` yedeği. Sahip oturumu yükseltmede korundu.

Canlı TMDB **Başlangıç 27205 / tt1375666** ve TVmaze **Sherlock 335 / tt1475582 / TVDB 176941** doğrulandı. Canlı DB şema 20, **0 transfer, 0 takip/katalog alias kaydı, 0 etkin sentetik kütüphane**; doğrulama kataloğa test yapımı eklemedi. Birleştirme/parça/numara akışları backend/native kanıtına sahiptir; kullanıcının gerçek kayıt/cihaz ve TMDB grup importu kabulü açıktır. Kanıtlar: `artifacts/test-results/home-0.17.0-full.trx`, `artifacts/update-roundtrip-0.16.0-0.17.0.json`, `artifacts/live-runtime-v017.json`.

Paketler `artifacts/packages/home-0.17.0-{win-x64,linux-x64,linux-arm64}.zip`. Linux/ARM64 çalıştırma, kaynak hesapları, dağıtım ve diğer ürün alanları [çalışma panosunda](product/execution.md) sürer.

## 0.16 güncel yayın

Sanatçı/yeni-albüm metadata profili ve istisnaları, opt-in otomatik arama/edinme/cutoff/yükseltme; 48 kHz PCM kesintisiz müzik oynatıcısı, ReplayGain, favori ve gerçek LUFS/FFT tabanlı öneri; yönetilen FLAC/MP3/M4A etiket/kapak/adlandırma ve birebir geri alma eklendi. **165/165 backend/native test, 0 atlanan; 5/5 saf ses zamanlayıcı testi**, frontend build ve üç imzalı platform paketi başarılı. [0.16 sözleşmesi](milestone-16.md).

Gerçek süreç **0.15.0/şema 17 → 0.16.0/şema 19 → önceki sürüm/veri** roundtrip geçti. Canlı Home **http://localhost:5080**, PID 2224; veri `.data/preview`, etkin paket `.data/preview/updates/releases/b3d11dd167124a18abb85433d4afe639`. Canlı Radiohead kataloğu **107 albüm grubu / ilk sayfa 50**; gerçek `Live`/`Remix` secondary türleri doğrulandı. Canlı DB ve API'de **0 transfer, 0 müzik otomasyonu, 0 müzik işi, 0 etkin sentetik kütüphane**. Kanıtlar `artifacts/live-runtime-v016.json`, `artifacts/test-results/home-0.16.0-full.trx`, `artifacts/update-roundtrip-0.15.0-0.16.0.json`.

Paketler `artifacts/packages/home-0.16.0-{win-x64,linux-x64,linux-arm64}.zip`. Canlı geri dönüş 0.15.0 + `a99f12b6c46242b7be79078b1266d83e.zip` yedeğidir. M4A freeform metadata için ayrı TagLibSharp 2.3.0 DLL/lisansı eklenmiştir. Browser/cihaz dinleme, gerçek kaynak/transfer, Linux/ARM64/Docker çalıştırma ve kamu lisans/imza kapıları açık. Tam kapsam [ürün panosunda](product/execution.md) sürer.

## 0.15 güncel yayın

MusicBrainz release-group/release/recording ayrımı, albüm baskı eşleştirmesi, CAA kapak proxy'si, albüm listesi ve kaynak seçimiyle doğrulanmış müzik edinme/import eklendi. **151/151 test, atlanan 0**; canlı OK Computer yayın/parça kataloğu ve şema 15→17 update/rollback doğrulandı. [0.15](milestone-15.md), `artifacts/live-runtime-v015.json`. Paketler `artifacts/packages/home-0.15.0-{win-x64,linux-x64,linux-arm64}.zip`; çalışan adres `http://localhost:5080`. Tam kapsam hâlâ [panodaki](product/execution.md) kod işleri ve dış kabul kapılarıyla sürer.

## 0.14 güncel yayın

Kütüphane klasör gezgini, arşiv/rebind, dışlamalar, periyodik/debounce tarama, opt-in NFO/yerel afiş/otomatik kimlik eşleme ve Windows rename kimliği eklendi. **145/145 test, atlanan 0**. [0.14 kanıtı](milestone-14.md), canlı durum `artifacts/live-runtime-v014.json`; DB şema 15. İmzalı uygulama içi güncelleme ve önceki sürüm yedeği korunuyor.

## 0.13 güncel yayın

İş/dosya öncelikleri, BitTorrent dosya seçimi, ortak/per-job hızlar, kaynak seed kriterleri, kalıcı sayaçlar, staging temizliği ve geçmiş arşivi eklendi. Kalite kuralları regex/codec/HDR/protokol custom-formatları, retention ve ayrıca açılan aynı çözünürlükte yükseltmeleri kapsar. **139/139 test, 0 atlanan**; gerçek loopback seçili dosya aktarımı ve temizlik sonrası kaynak/yayın byte'ları doğrulandı. [0.13 kanıtı](milestone-13.md), canlı `artifacts/live-runtime-v013.json`; DB şema 14. Tam kapsam panodaki açık gereksinimlerle sürer.

## 0.12 güncel yayın

Ebeveyn erişim kuralları, sunucu tarafı yaş/dosya engeli, güvenilen cihazda PIN'li profil geçişi, kişisel favori/puan/koleksiyonlar ve yenilemede korunan yerel metadata eklendi. **132/132 test, 0 atlanan**; şema 12→13 güncelleme ve geri dönüş gerçek süreçte doğrulandı. Canlı TMDB sınıflandırması, sahip profilinin korunması ve sıfır transfer durumu `artifacts/live-runtime-v012.json` içinde. Ayrıntılar [0.12](milestone-12.md); uygulama içi imzalı güncellemeyle çalışan release veri kökünün `updates/releases/` alanına alındı. Başlangıç paketi launcher'ı son etkin imzalı sürüme yönlenir.

## 0.11.1 güncel yayın

İmzalı paketler ve update/rollback, Windows kurucu, Linux x64/ARM64 build çıktıları, Docker tarifi, HTTPS/PFX, güvenilen proxy'ler ve tam alt-yol/PWA desteği eklendi. **126/126 test, 0 atlanan**; ayrıca gerçek Windows upgrade→rollback ve kurulum doğrulaması geçti. Ayrıntılar [0.11](milestone-11.md), [kurulum](../deploy/README.md). Çalışan paket `artifacts/packages/publish-0.11.1-win-x64`, adres `http://localhost:5080`, DB şema 12. Kaynak/veri ve cihaz kapıları [ürün panosunda](product/execution.md) açık tutulur.

## 0.10 güncel durum

- Hane hesapları, kütüphane izinleri, tek kullanımlık davetler, oturum iptali, kişisel geçmiş, istek onayı ve kotaları ([0.5](milestone-05.md)).
- Opt-in RSS/arama/edinme, gecikme/cutoff, çözünürlük yükseltmesi, blocklist ve bildirimler ([0.6](milestone-06.md)).
- Online uygulama yedeği, hash doğrulamalı atomik state restore, uygulama restart'ı, bu NVIDIA cihazında gerçek NVENC ve HDR10/HLG→SDR çıktısı ([0.7](milestone-07.md)).
- Gerçek müzik etiketleri ve MusicBrainz kataloğu, disc/track, kişisel kuyruk/liste, LRC sözler ([0.8](milestone-08.md)).
- OpenSubtitles bağlantısı, hash/kimlik/dil puanı, quota ve opt-in altyazı işleri ([0.9](milestone-09.md)); canlı hesap kabulü açık.
- Dahili NNTP TLS/STARTTLS, NZB/yEnc/CRC/parça resume, kaynak fallback, PAR2 onarımı ve arşiv açımı ([0.10](milestone-10.md)).
- **115/115 backend/native test, 0 atlanan**, üretim frontend ve Windows x64 yayını başarılı. Çalışan adres `http://localhost:5080`, paket `artifacts/server-v0.10`, DB şema 12; kanıt `artifacts/live-runtime-v010.json`.

Tam ürün kapsamı ve gelişmiş koşullar [ürün panosunda](product/execution.md) izlenir. Gerçek indexer/NNTP hesabı, browser/cihaz kabulü, Linux/ARM64 çalıştırma ve dağıtım lisansı kapıları açık. Ortamda Docker CLI var fakat daemon çalışmıyor; WSL kurulmamış. Bunlar için çalıştırma doğrulaması iddia edilmez.

Aşağıdaki sürüm bölümleri tarihsel kayıttır.

## 0.4.1 güncel durum

- Gerçek ses/altyazı/chapter envanteri, yan ve gömülü metin altyazıları, kullanıcı altyazısı yükleme, WebVTT ve zaman kaydırma.
- Direct/HLS remux/AAC ses/H.264 yazılımsal SDR uyarlaması; ses/kalite/hız/sahne/PiP kontrolleri ve konumu koruyan mini oynatıcı.
- Sahibe bağlı, revizyon korumalı ve sınırlı cache'li oynatma oturumları; kaynak dosyalar korunur; iptal/kapatma/toparlanma kod yolları doğrulandı.
- Doğrulamada duran tamamlanmış edinme için kullanıcı seçimiyle ayrı import profili ve yeniden doğrulama.
- **77/77 backend test, 0 atlanan**; gerçek FFmpeg codec/altyazı, HTTP segment-range ve lifecycle kontrolleri dahil. Frontend/PWA build ve Windows x64 publish başarılı.
- Yayın: `artifacts/server-v0.4.1`; `http://localhost:5080`. Canlı kanıt: `artifacts/live-runtime-v041.json`.
- Ayrıntılar ve açık cihaz/HDR/GPU kapıları: [milestone-04.md](milestone-04.md). Gerçek indexer bağlantısı, çoklu kullanıcı, otomatik edinme, müzik yönetimi ve diğer ürün alanları [çalışma panosunda](product/execution.md) açık.

## 0.3 ile eklenenler

Gerçek TMDB/TVmaze keşfi, dosyasız takip/istek, bölüm takvimi/eksikler, Torznab/Newznab araması, açıklanabilir kalite kuralları ve kullanıcı seçimiyle kimliğe bağlı edinme eklendi. Ayrıntılı API, sınırlar ve doğrulama: [milestone-03.md](milestone-03.md). Ürün kapsamı ve açık kabul kapıları: [product/](product/README.md).

- TMDB kullanıcı bağlantısı canlı doğrulandı; Başlangıç (27205), IMDb `tt1375666`; trend listesinde 20 gerçek film. TVmaze gerçek Sherlock araması başarılı.
- Demo kütüphaneleri arşivlendi. Normal ekranlarda sentetik katalog yok; fiziksel dosyalar ve eski kayıtlar korunuyor.
- 60 testlik backend paketi geçti; ardından üç yeni grab testi içeren 10 testlik kaynak grubu geçti (63 ayrı doğrulanmış vaka). Frontend/PWA derlemesi ve Windows x64 yayını başarılı.
- Kullanıcının indexer hesabı henüz yok. Gerçek indexer ve browser/cihaz kabulü açık.
- Çalışan paket `artifacts/server-v0.3`; veri `.data/preview`. Yükseltme yedeği `.data/backups/preview-v02-20261012-223359`.

Aşağıdaki 0.1/0.2 bölümleri önceki doğrulamaların tarihsel kaydıdır.

## 0.2 ile eklenenler

| Alan | Durum |
| --- | --- |
| İçerik kimliği | Film/dizi, bölüm ve fiziksel dosya bağlantıları; dosya sürümleri; taramada korunan eşleştirme |
| TVmaze | Anahtarsız dizi arama, kimlik/bölüm doğrulama ve kaynak atfı; canlı API ile doğrulandı |
| TMDB | Sunucu tarafı Bearer token, film/dizi/bölüm adaptörü ve bağlantı ayarları; HTTP fixture testleri geçti, yetkili canlı token testi bekliyor |
| Görseller | İzinli sağlayıcı alanlarından süre/boyut sınırlı indirme ve yerel önbellek |
| İndirme | Uygulama içine gömülü MonoTorrent 3.0.2, magnet çözümleme, torrent yükleme, kuyruk, duraklat/devam/iptal ve paylaşım |
| Kalıcılık | Niyet revizyonları ile eski durum güncellemelerinin engellenmesi, restart/fast-resume, v1/v2 hash takma adları |
| Kütüphaneye alma | Doğrulanan payload, hardlink/kopya hazırlığı, atomik klasör yayını, manifest ile idempotent toparlanma ve tarama bağlantısı |
| Tek yazıcı uygulama | Aynı veri klasörünün iki Home sürecince açılmasını engelleyen uygulama kilidi |
| Yeni arayüz | İndirmeler ekranı, metadata bağlantısı, eşleşme seçimi, bölüm/sürüm alanları ve uzun diyaloglarda sabit kapatma kontrolü |

Ayrıntılı model ve sağlayıcı kaynakları: [milestone-02.md](milestone-02.md).

## Tamamlanan ilk dilim

| Alan | Durum |
| --- | --- |
| Yerel hesap ve oturum | Tek sahip hesabı, parola hashleme, kalıcı cookie, CSRF doğrulaması, giriş hız sınırı |
| Kütüphane bağlama | Kullanıcının açıkça seçtiği mutlak klasör yolu; aynı kökün tekrar eklenmesi engellenir |
| Dosya envanteri | Tekrarlanabilir tarama, dosya kimliği korunarak güncelleme, Türkçe normalize arama, sayfalama |
| Kalıcı işler | SQLite kuyruğu, tek tarama işleyicisi, iptal, tekrar deneme ve yeniden başlatma sonrası toparlanma |
| Dosya sınırları | Katalog ID'si üzerinden oynatma; traversal ve reparse-point atlama girişimleri engellenir |
| Medya bilgileri | ffprobe ile süre, boyut ve codec; FFmpeg ile JPEG önizleme |
| Oynatma | Özgün dosyanın HTTP Range ile sunumu; mevcut tarayıcı biçim desteği kullanılır |
| İzleme geçmişi | Konum kaydı, tamamlanma durumu ve kaldığın yerden devam etme |
| Oynatıcı | Aynı video elementi korunarak mini/tam görünüm; oynatma, duraklatma, sarma, sessize alma, tam ekran |
| Arayüz | HeroUI v3, Motion, cam gezinme katmanı, masaüstü/mobil düzen, kart/liste görünümü, gerçek aktivite durumları |
| Eski örnek içerik | 0.1/0.2'de kullanılan 6 sentetik video; 0.3'te normal kullanımdan arşivlendi |
| Yayın | Derlenmiş frontend'i kendi HTTP sunucusundan sunan Windows x64 self-contained çıktı |

Kaynak medya dosyaları bu dilimde yalnızca okunur. Bir tarama başarısızsa veya iptal edilirse önceki erişilebilirlik bilgileri korunur. Başarılı taramada kayıp görülen dosyaların kayıtları silinmez; erişilemez işaretlenir. Dosya geri dönerse kimliği ve izleme geçmişi korunur.

### Modelin sınırı

`media` fiziksel dosya envanteridir; 0.2'de `content`, `episodes` ve `media_matches` buna eklendi. Aynı sağlayıcı/tür/kimliğe bağlanan dosyalar aynı içeriği paylaşır. Arayüz sürümleri listeler ve yüklenen kartları içerik/bölüm kimliğine göre toplar. Farklı sağlayıcı kimliklerinin otomatik birleştirilmesi, çok bölümlü tek dosya ve ayrıntılı ses/altyazı izi modeli sonraki işlerdir. Eşleştirilmemiş başlıklar dosya adından gelir.

## Doğrulama sonuçları

- `dotnet test HomeServer.slnx`: **37/37 geçti**.
  - Aynı dosyanın tekrar taramada kimliğinin korunması.
  - Türkçe ve literal joker karakter araması.
  - Bağlantısı kesilmiş kökte katalog bilgilerinin korunması.
  - Kayıp dosyanın kaydı/izleme geçmişiyle birlikte geri gelmesi.
  - Tamamlama işleminden önceki iptalin atomik kontrolü.
  - Yeniden başlatılan işte yeni tarama token'ı ve deneme sayısı.
  - Eşzamanlı tarama isteklerinin tek işe indirgenmesi.
  - Oynatma konumunun doğrulanması ve süreyle sınırlandırılması.
  - Traversal ve Windows junction üzerinden sınır aşımı.
  - İlk kurulum, tekrar kurulum engeli, kimlik doğrulama, CSRF ve HTTP 206 byte-range yanıtı.
  - v1 veritabanından yükseltmede hesap, dosya kimliği ve izleme konumunun korunması.
  - Ortak içerik kimliği, ayrı bölüm kimlikleri, sürümler ve eşleştirme kaldırma.
  - Token'ın korumalı saklanması, TMDB Bearer isteği, sağlayıcı hata durumunda eski eşleştirmenin korunması.
  - Görsel kaynağı allowlist'i, dosya yolu ve bencode sınırları.
  - İndirme niyet revizyonları, yinelenen hash'lerin önlenmesi ve hız durumunun restart'ta sıfırlanması.
  - Hem `.torrent` hem magnet için gerçek MonoTorrent loopback aktarımı; byte doğrulaması, fast-resume dosyası, idempotent yayın ve katalog taraması.
- `npm run build`: **geçti**; TypeScript kontrolü ve Vite üretim derlemesi.
- `dotnet publish ... -r win-x64 --self-contained true`: **geçti**.
- Playwright: **4 senaryo doğrulandı**, derlenmiş Windows uygulamasının sunduğu **http://127.0.0.1:5082** ve ayrı `.data/e2e-v02` üzerinde. Son ayar-formu düzeltmesi ilgili senaryo hedeflenerek tekrar doğrulandı.
  - Hesap/giriş, gerçek örnek klasör taraması, metadata/önizleme, arama, liste görünümü, aktivite ve çıkış/giriş.
  - Gerçek H.264/AAC dosyasının oynatılması ve sarılması; mini/tam geçişte aynı video elementi ve ilerleyen süre.
  - Kapatma ve sayfa yenileme sonrasında izleme konumunun korunması.
  - 390×844 mobil viewport, azaltılmış hareket, modal kapatma, filtreler ve yatay taşma kontrolü.
  - Metadata eşleştirme/ayar kontrolleri, eksik TMDB token durumu, dil değişikliğinin kalıcılığı, uzun panelde kapatma erişimi.
  - Geçersiz magnet reddi, gerçek `.torrent` HTTP yüklemesi ve sayfa yenilemede korunan duraklat/devam/iptal durumu. Bu UI testinde sahte medya içeren özel torrent, loopback-only motorda kullanıldı.

`tools/verify-live-metadata.mjs` ile **gerçek TVmaze API** doğrulaması yapıldı: Sherlock (TVmaze 335), S01E01, “A Study in Pink”, afiş proxy yanıtı HTTP 200 / 100993 byte. Geçici eşleştirme ayrı test alanındaki örnek dosyada yapıldı ve ardından kaldırıldı. Sonuç `artifacts/live-metadata.json`, görünüm `artifacts/home-live-metadata.png` içinde. TMDB için canlı kullanıcı token'ı kullanılmadı.

Ortam: Windows x64, .NET SDK 10.0.401, Node.js 24.21.0, FFmpeg 9.0.2, MonoTorrent 3.0.2, Playwright Chromium 156. Windows uygulama kontrolü kaynaklı bir çalıştırma engeli kullanıcı tarafından giderildikten sonra aynı backend derlemesi yeniden doğrulandı. Görünüm ekran görüntüleri `artifacts/home-*.png` altında tutulur.

## Yayın ve erişim

- Çalışan derlenmiş önizleme: **http://localhost:5080**.
- Windows uygulaması: `artifacts/server-v0.4.1/HomeServer.Api.exe` (önceki yayınlar ayrıca korunur).
- Önizleme verileri: `.data/preview`; örnek dosyalar: `.data/demo-media`.
- Normal geliştirme verileri: `tools/dev.ps1 -Mode Server` ile `.data/app`.
- Yükseltme öncesi önceki önizleme veritabanı ve anahtarları `.data/backups/preview-v01-20261012-203731` altına yedeklendi.
- Tarayıcı testlerinin oluşturduğu **önizleme hesabı**: `Home` / `HomePreview2026!`.
- Medya araçları bu ortamda yapılandırılmış yollarından kullanılır. Bir dağıtımda uygulamanın yanına konabilir veya `Home__FfmpegPath` / `Home__FfprobePath` ile seçilebilir.

Dockerfile/Compose, HTTP 5080 ve BitTorrent TCP/UDP 55123 portlarını tanımlar. Docker imajının çalıştırılması, Linux/ARM64, fiziksel mobil cihazlar, Safari ve GPU davranışı henüz doğrulanmadı. BitTorrent doğrulaması kontrol edilen loopback eşleriyle yapıldı; özel internet tracker'larının tamamı için uyumluluk iddiası değildir.

## Sonraki işlevsel dilimler

1. Gerçek kaynak kabulü; RSS, otomatik edinme/yükseltme ve gelişmiş kalite kuralları.
2. İnce kapsamlı sezon/bölüm istekleri ve sağlayıcı alias'ları.
3. Otomatik altyazı sağlayıcıları, HDR/GPU profilleri ve cihaz kabulü; temel iz/HLS/SDR katmanı 0.4'te eklendi.
4. İndirme geçmişi/payload temizleme arayüzü ve depolama politikaları.
5. Çoklu kullanıcı, desteklenen cihaz kurulumları, yedekleme/güncelleme ve TV istemcileri.

Bu işlevler mevcut sürümde yapılmış veya canlı entegrasyonla doğrulanmış olarak sunulmaz.

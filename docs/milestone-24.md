# 0.24 — Hesap bazlı edinme bütçesi ve kullanım görünürlüğü

Tarih: **2026-10-15**, **0.24.0 yayımlandı**. Gereksinimler: **ACCOUNT-BUDGETS-01 / USR-03**, ilişkili **USR-01/02, ACQ-01/02, DL-02/03/04/05, OPS-01/03/08**. Sahiplik, bütçe, yürütme ve kullanıcı yüzeyi tek teslimat grubu olarak tamamlandı.

## Kullanıcı akışı

- **Hane ve hesaplar → hesabı düzenle → Hesap indirme bütçesi**: edinmeyi aç/kapat, aylık yeni edinme sayısı, aylık paket boyutu, tek paket boyutu ve eşzamanlı indirme sınırı.
- Boş aylık/adet/boyut alanı sınırsızdır. Aylık yeni edinme sayısında 0 yeni iş kabulünü kapatır; boyut bütçesinde 0 payload başlatmayı kapatır. Eşzamanlı 0 yeni aktarım/doğrulama slotu açmaz.
- **İşlemler** ekranında hesap, bütçeye yazılan boyut, bekleme nedeni ve kişisel kullanım geçmişi görünür. Yönetici hesap filtresiyle hane aktarımlarını izler; üyeler kendi aktarımlarını görür/yönetir.
- **Parola ve oturumlar** bölümünde kişisel bütçe ve geçmiş okunabilir. Edinme izni kaldırılmış bir hesap da giriş yapabildiği sürece kendi kullanımını görebilir.
- Yeni manuel magnet/torrent/NZB ve kaynak sonucu indirmeleri komutu veren hesaba aittir. Video otomasyonu kuralı kaydeden hesabı, müzik otomasyonu monitor sahibini kullanır. Video kuralında sorumlu hesap gösterilir.
- Kota doluluğu, kötü release olarak engel listesine yazılmaz. Otomasyon işi kalıcı olarak bekler; ilgili bütçe düzenlenince bekleyen işler yeniden değerlendirilir.

## Bütçenin ölçüsü

**Aylık boyut bütçesi, paket edinme iznidir.** Torrent için seçili dosyaların doğrulanmış metadata toplamı; NZB için descriptor içindeki parça boyutu beyanlarının toplamı kullanılır. NZB'de kabul edilen yEnc parçası beyan edilen boyutu aşamaz. Sıkıştırılmış NZB paketinin açılmış medya boyutu farklı olabilir.

- Yeni benzersiz iş kabulü aylık edinme adedine tek kez yazılır. Metadata isteği başarısızsa iş oluşmaz. Kabul edilmiş işi iptal etmek/arsivlemek bu adet kaydını silmez.
- Payload başlamadan önce byte kapasitesi transaction içinde ayrılır. Aynı hesabın iki hazırlığı kalan kapasiteyi birlikte aşamaz.
- `StartAsync` öncesinde bütçe, kullanıcı yetkisi, iş/politika revizyonu ve paket nesli yeniden doğrulanır; byte tutarı aynı transaction ile kalıcı kullanıma geçirilir. Bu andan sonra başarısız/iptal edilen işin başlatılmış paket kotası korunur.
- Aynı payload'ın tekrar başlatılması aynı tutarı yeniden yazmaz. Dosya seçimi büyürse önceki en yüksek izinli boyutun farkı yazılır; küçülme geçmiş kullanımı geri almaz.
- Staging temizliği paket neslini ilerletir. Sonraki gerçek yeniden edinme yeni kullanım gerektirir. Arşivden çıkarma veya hash ile tekrar ekleme önceki kaydı/kotayı sıfırlamaz.
- Dönem **UTC takvim ayı**dır. Metadata hazırlığı ay sınırını aşarsa rezervasyon, payload'ın başladığı ayda tekrar kontrol edilir. Başlamış payload'ın mevcut izni ve geçmiş borcu ay değişince korunur.
- İndirme/parça protokolü metadata'sı, yeniden iletimler ve ağ başlıkları paket boyutu kotasının ölçüsü değildir. Ayrı **alınan/gönderilen medya verisi** sayacı motorun raporladığı veriyi gösterir; ISP trafik sayacı veya kusursuz wire-byte kotası iddiası yoktur.

## Yetki, tekilleştirme ve eski kayıtlar

- Yeni üretim edinme yolları `DownloadPrincipal` taşır. Manuel komutun kullanıcı sürümü/giriş oturumu metadata bekleyişinden sonra da doğrulanır. `X-Home-User` / `X-Home-Session`, profil değişirken yola çıkmış web komutunu reddeder; cookie ve CSRF ayrıca gereklidir.
- İndirme liste/ayrıntı/dosya/politika, kontrol, arşiv/parola, staging temizliği ve import/link incelemeleri sahiplik sınırını uygular. Bir üye başka hesabın aktarımını hash ile tekrar ekleyerek sahiplenemez veya ayrıntısını alamaz. Yönetici hane aktarımlarını yönetebilir; var olan payload için ikinci edinme borcu yaratılmaz.
- Kullanıcı çıkışı, önceden kabul edilmiş dayanıklı indirmeyi sonlandırmaz. Hesabı devre dışı bırakmak/edinme yetkisini kaldırmak aktarımı bekletir. Politika değişimi eski hazırlık/yürütme revizyonunu iptal eder.
- Tarihsel müzik otomasyonu ve aynı kural revizyonuyla kanıtlanabilen video otomasyonunun sahibi migration'da geri bulunur. Diğer eski işlerde sahip **bilinmiyor** olarak kalır; eski trafik için yeni hesaba veya aya kullanım uydurulmaz.
- Sahibi bilinmeyen eski işler yöneticiye görünür ve mevcut sunucu sınırlarıyla çalışır. Yönetici duraklatılmış, etkin yürütmesi kalmamış bir eski kayda açıkça hesap atayabilir; atama o ayın bir yeni edinmesidir. Hesabı belirlenmiş işi başka hesaba aktararak bütçe dolaştırma yolu yoktur.

## Yürütme ve sayaç güvenilirliği

- Her worker yürütmesi kalıcı bir bütçe lease'i alır. Hesap başına slot atomiktir; transfer/hash doğrulama/import tamamlanınca seeding aşaması download slotunu bırakır. Yeniden açılacak eski payload da ilk doğrulama slotundan geçer.
- Torrent metadata'sı çözüldükten sonra kapasite kontrolü yapılır; payload yöneticisinin `StartAsync` çağrısı borç kaydından sonra gelir. NZB de NNTP article istekleri başlamadan önce aynı kapıdan geçer.
- Kota bekleme nedeni ve tekrar zamanı indirme satırındadır. Eşzamanlı slot doluluğu kısa aralıkla, aylık kota ay başında; hesap/tek-paket ayarı bekleyişi sınırlı aralıklarla veya kullanıcı/politika değişiminde değerlendirilir.
- Politika/izin değişikliği worker'ın yürütme token'ını iptal eder. Staging ve yayımlanmış kütüphane dosyaları korunur; byte rezervasyonu gerçek kapanıştan sonra serbest bırakılır.
- Son motor sayacı `StopAsync` sonrasında da kaydedilir. Kayıtlı run/observation receipt'i, pause/cancel sonrasındaki gerçek son byte farkını kabul eder; eski durum gözlemi yeni kullanıcı niyetini değiştiremez. Aynı/azalan sayaç ve kapanmış run tekrarları ek kullanım yaratmaz.
- Yeniden başlatma açık lease/rezervasyonları kapatır, kalıcı kabul/borç/geçmiş korunur. Bellek içi slot sayısı veritabanı yerine doğruluk kaynağı olarak kullanılmaz.
- Bütçe lease'i 90 saniye, heartbeat mevcut worker'ın 500 ms döngüsünde; kapanmış run/receipt iki gün, ayrıntılı bütçe olayları 90 gün / global 50.000 satır, aylık özetler 36 ay tutulur. Mevcut global 200 etkin indirme, bounded transfer/seeding ve parser limitleri birlikte uygulanır.

## Şema ve kod

Şema **27 → 28**, `028_download_budgets.sql`:

- İndirme sahibi/kaynak türü/paket nesli/borç ve bütçe bekleme alanları.
- `user_download_budgets`, `download_budget_periods`, `download_budget_runs`, `download_budget_events`, `download_counter_receipts`.
- Video ve müzik otomasyonunda kalıcı bütçe bekleme nedeni.

`MediaStore.DownloadBudgets`, `DownloadAccess`, `DownloadBudgetEndpoints`; ortak `DownloadWorker`, `TorrentEngine`, `NzbEngine` startup/iptal kapıları; manuel ve otomatik kaynak edinme bağlamları; `DownloadBudgets.tsx` kullanıcı yüzeyi.

API: `/account/download-usage`, `/account/download-history`; yönetici `/users/{id}/download-usage`, `/users/{id}/download-history`, `PUT /users/{id}/download-budget`, `PUT /downloads/{id}/owner`.

## Doğrulama

Odaklı kontroller: atomik kabul, hash tekilleştirme/sahiplik, byte rezervasyonu ve iptali, seçim büyümesi, UTC ay sınırı, politika revizyonu, staging nesli, kapanış sayaçları, restart/slot toparlanması, çıkış/izin değişimi ve HTTP hesap/CSRF sınırı. Gerçek loopback torrent/magnet ve NNTP'de kota kapısının payload öncesinde çalışması, yEnc eksik boyut beyanının reddi ve kaynak hash korunumu doğrulanır.

- **277/277 backend/native, atlanan 0**, `artifacts/test-results/home-0.24.0-full.trx`; **38/38 Node** kontrolü, frontend/worker üretim build ve üretilmiş worker sözdizimi başarılı.
- Gerçek loopback torrent/magnet ve NNTP'de payload öncesi bütçe, kaynak byte/hash korunumu; gerçek Kestrel süreç çökmesi sonrasında kabul/borç/geçmiş ve kota bekleyişi doğrulandı. Migration eski sahibi bilinmeyen aktarımı ve sayaçlarını korudu; bugünkü hesaba/aya geçmiş borç yazmadı.
- Genel koşuda hızlı tamamlanan FFmpeg işinin terminal durumunu geç gelen `ready` gözleminin ezebildiği yarış tespit edildi. **PLY-03 / `PlaybackLifecycle`** atomik ve terminal durumu koruyan geçişle düzeltildi. Dört durum/sıralama testi ve üç native PGS/DVD/ASS kontrolü geçti (`home-0.24.0-encoder-lifecycle.trx`); ardından yukarıdaki genel 277 test geçti. İlk bulgu `home-0.24.0-pre-lifecycle.trx` içinde korunur.
- Windows x64, Linux x64, Linux ARM64 için imzalı **0.24.0** paketleri üretildi. **0.23.1/şema 27 → 0.24.0/şema 28 → önceki veri/sürüm** güncelleme/geri dönüş geçti; hesap korundu, restore önceki oturumları iptal etti (`artifacts/update-roundtrip-0.23.1-0.24.0.json`).
- Başlangıçta kapalı canlı önizleme kayıtlı 0.23.1 yayınıyla açıldı; imzalı paketle 0.24.0'a yükseldi. Sahip oturumu korundu. Canlı **şema 28**, **0 indirme/oda/oynatma, 0 bütçe override/aktif bütçe run'ı/dönem kaydı, 0 etkin sentetik kütüphane ve 0 FK ihlali**; VAD CPU ve önceki offline worker çalışır (`artifacts/live-runtime-v024.json`). Doğrulama medya edinme veya oynatma başlatmadı.
- `tools/update-live.ps1` ve `tools/verify-live.mjs`, sürüm/hesap/veri kontrollerini tekrar kullanılabilir yayın araçlarına toplar. Gerçek kaynak ve cihaz kabulü **ACCEPT-24**, Linux/Docker ve dağıtım kapıları ilgili mevcut kartlarda kullanıcı sonucunu bekler.

## Kaynaklar — 2026-10-15

- Context7 `/alanmcgovern/monotorrent`: `DownloadMetadataAsync`, `AddAsync(Torrent)` / `StartAsync` sınırı. Kurulu **MonoTorrent 3.0.2** XML tipleri ve mevcut native loopback testleri doğruluk için kullanıldı; master doküman örnekleri doğrudan sürüm sözleşmesi sayılmadı.
- Home'un mevcut NNTP/yEnc parser ve CRC sözleşmesi: 16 MiB parça, bounded body, metadata ve decoded-parça uzunluğu ilişkisi; önceki NNTP protokol kaynakları `milestone-10.md` içinde.
- Kurulu React/Query/HeroUI tipleri, mevcut Context7 hook/query belgeleri ve üretim TypeScript build. Yeni paket veya veri kümesi benimsenmedi.

## Kullanıcı kabulü — ACCEPT-24

1. Hane hesabında küçük bir aylık bütçe ve bir eşzamanlı aktarım seç; sahibi doğru görünen gerçek indirmeyi kendi kaynaklarınla başlat.
2. Yetersiz boyut kotasında bekleme nedenini gör; bütçeyi artır veya seçili torrent dosyalarını azalt. Doğrulanmış parçalar korunarak devam etmeli.
3. Aynı paketi tekrar ekle/duraklat/devam ettir; ikinci edinme/byte borcu oluşmamalı. Staging temizliği sonrası yeniden edinme yeni boyut kullanımı olmalı.
4. İki farklı üye kendi aktarım/geçmişini görmeli; yönetici hane toplamını yönetebilmeli. Otomasyonun hangi hesabı borçlandırdığını kontrol et.

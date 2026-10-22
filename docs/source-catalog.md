# SOURCE-02 — Hazır site kataloğu ve kaynak kurulumu

Güncelleme: **2026-10-17**. Gereksinimler: **IDX-01/02/03/07/08/09, OPS-02, EXT-04**. **12 hazır profil ve rehberli kurulum tamamlandı**, şema **39**. Frontend/worker/PWA ve .NET derlemeleri, 42 odaklı backend kontrolü ve aşağıdaki salt-okunur sağlayıcı doğrulamaları geçti. **http://localhost:5080** artık bu akışı 0.27.0-dev.1 önizlemesinde sunar; runtime sürümü **0.27.0**. [Önizleme yayını ve mevcut isteğin korunumu](preview-027.md).

Kullanıcı akışı: **Kaynak ekle → site seç → yalnız gereken bilgileri gir → bağlantıyı test et → kaynağı ekle**. Genel Torznab/Newznab bağlantısı ve adaptör yükleme gelişmiş işlemlerdir. Test edilen geçici bağlantı onaylanıncaya kadar normal kaynak listesi/otomasyon dışında kalır. Kaynak bağlama bir medya indirme komutu değildir.

## Kaynak, lisans ve protokol araştırması

Hazır profiller Home tarafından yazılır; adres/yetenek bilgileri aşağıdaki sağlayıcı uç noktalarından okunur. Prowlarr/Jackett tanım paketi veya uygulama kodu kopyalanmaz. Yeni bir üçüncü taraf kod/veri paketi benimsenmez. Sağlayıcı yanıtları ve medya erişimi kendi hizmet koşullarına tabidir; aşağıdaki protokol bağlantıları içerik dağıtım lisansı sağlamaz.

| Hazır profil | Bağlantı / başvuru | 2026-10-16 araştırma sonucu |
|---|---|---|
| YTS | https://yts.gg/api ; https://movies-api.accel.li/api/v2/list_movies.json | Güncel operatör API belgesi ve IMDb `tt0063350` için JSON okundu; iç içe torrent sürümleri/hash/boyut/tarih. Eski yts.lt/yts.bz adresleri yts.gg'ye, API belgesi yeni API alanına işaret ediyor. Anahtar gerekmiyor |
| EZTV | https://eztvx.to/api/ ; https://eztvx.to/api/get-torrents?limit=1&page=1 | Resmi belge ve JSON okundu; dizi IMDb kimliği, torrent hash'i, boyut/tarih ve sayfa. Anahtar gerekmiyor |
| The Pirate Bay / API Bay | https://apibay.org/q.php?q=big%20buck%20bunny&cat=0 | Gerçek JSON arama yanıtı, hash/boyut/seed/Unix tarih ve IMDb alanı okundu. Anahtar gerekmiyor; sayfalama bildirmeyen sınırlı sonuç listesi |
| Torrents.csv | https://codeberg.org/heretic/torrents-csv-server ; https://torrents-csv.com/service/search?q=big%20buck%20bunny | Güncel README ve sunucunun HTTP sözleşmesi okundu: `q` + `after`, 25 kayıt ve `next` cursor'u. Eski `page`/`size` belgeleri güncel sözleşme değildir. Kod/veri deposu ürüne alınmaz; Home bağımsız HTTP istemcisi kullanır |
| Internet Archive | https://archive.org/developers/ ; mevcut 0.27 adaptörü | JSON arşiv araması; torrent bağlantılı kayıtlar. Anahtar gerekmiyor |
| Nyaa RSS | https://nyaa.si/ ; mevcut 0.27 adaptörü | Anime/ilgili yayınlar için RSS araması. Anahtar gerekmiyor |
| NZBgeek | https://api.nzbgeek.info/api?t=caps | Gerçek Newznab caps XML'i okundu; arama için kullanıcının API anahtarı gerekli |
| DrunkenSlug | https://drunkenslug.com/api?t=caps | Gerçek Newznab caps XML'i okundu; api.drunkenslug.com HTTP 301 verdiği için doğrudan kanonik adres kullanılır. Kullanıcının hesabı/API anahtarı gerekli |
| NZB Finder | https://nzbfinder.ws/api?t=caps | Gerçek Newznab caps XML'i ve resmi giriş sayfası okundu; API anahtarı gerekli |
| NZBPlanet | https://api.nzbplanet.net/api?t=caps | Gerçek Newznab caps XML'i okundu; API anahtarı gerekli |
| DOGnzb | https://api.dognzb.cr/api?t=caps | Gerçek Newznab caps XML'i okundu; API anahtarı gerekli |
| NZB.life / NZB.su | https://api.nzb.life/api?t=caps | Eski api.nzb.su uç noktası emeklilik mesajı döndürdü; yeni api.nzb.life gerçek caps XML'iyle doğrulandı. API anahtarı gerekli |

Context7, YTS/EZTV/Torrents.csv için uygun kütüphane kaydı döndürmedi; operatör belgeleri ve gerçek salt-okunur HTTP yanıtları kullanıldı. Kaynak araştırmasında `.torrent`, `.nzb` veya medya payload'ı alınmadı; magnet bağlantıları açılmadı. Üyelikli sağlayıcıların arama/hesap kabulü kullanıcı anahtarına bağlıdır.

## Uygulanan akış

- **Kaynaklar → Site seçerek kaynak ekle:** site adı, Torrent/Usenet ve içerik türü filtreleri. Altı doğrudan torrent profili ile altı üyelikli Usenet profili bulunur. Desteklenmeyen siteler çalışır seçenek olarak gösterilmez; bu liste Prowlarr'ın bütün site tanımlarının kopyası değildir.
- Site seçimi adresi/protokolü/adaptörü otomatik belirler. Üyelikli bağlantıda yalnız sitenin API anahtarı istenir. Ad, öncelik, adres, proxy ve isteğe bağlı cookie gelişmiş tercihlerdedir. Genel Torznab/Newznab ve JSON/RSS adaptör yükleme **Gelişmiş** sekmesindedir. Kurulan adaptörler kaynak listesine de girer.
- Test bağlantısı korumalı anahtar/ayarlarla, `setup_pending=1` ve devre dışı olarak tek işlemde oluşturulur. Normal kaynak listesi ve otomasyon bunu kullanamaz. Kullanıcının **Kaynağı ekle** komutu doğrulanmış ayarları atomik etkinleştirir.
- Newznab testi caps okumasına ek olarak gerçek arama isteğiyle anahtarı doğrular; herkese açık caps'in başarılı olması tek başına hesap doğrulaması sayılmaz. Usenet aktarımı için ayrıca gerekli sunucu hesabı görünürdür.
- Düzenleme, mevcut bağlantıyı test sırasında değiştirmez; başarılı test uygulanırken aynı kaynak kimliği/geçmişi korunur. Arada değişmiş kaynak revizyonu reddedilir. Anahtar/cookie/başlıklar arayüze geri verilmez; başka origin'e geçişte eski kimlik bilgileri otomatik taşınmaz.
- Kurulum yönetici/giriş oturumu/sürümü, komut kimliği, istek hash'i, tanım hash'i ve revizyonla bağlıdır. İptal ve geç yanıt etkin kaynak yayımlayamaz. Test süresi 45 saniye, eşzamanlı test 2, bekleyen kurulum kişi başına 8/toplam 64; test geçerliliği 10 dakikadır. Tamamlanmamış geçici kayıtlar ve bunların süreç içi bağlantı/cursor önbellekleri temizlenir. Sunucu restart'ında yarım test yeniden test edilmelidir.

## Sağlayıcı davranışları

- YTS, her filmdeki torrent sürümlerini ayrı hash/çözünürlük/boyut/tarih ve IMDb kimliğiyle normalize eder; film araması sağlar.
- EZTV, dizi IMDb kimliği üzerinden arar. Metin/film arama desteği uydurulmaz. Sayfa sınırı sağlayıcı sözleşmesine bağlıdır.
- API Bay, tek sağlayıcı sonuç listesini kullanır. Nyaa RSS ve API Bay için başka kaynakların sonraki sayfaları okunurken ilk sayfa tekrar alınmaz.
- Torrents.csv, `after`/`next` cursor'unu Home sayfalarına bağlar. Cursor önbelleği kaynak revizyonu/sorguyla ayrılır, 15 dakika ve 2048 kayıtla sınırlıdır; restart/sona ermede baştan arama istenir. Desteklemediği genel RSS akışına alınmaz.
- HTML giriş/doğrulama sayfası boş RSS başarısı olarak kabul edilmez. Boyut, hash, tarih, XML/JSON ve URL sınırları aynı edinme hattına uygulanır.

## Doğrulama

- `artifacts/test-results/source-catalog-v027-complete.trx`: **42/42**, 0 atlanan. Kurulum izolasyonu, kimlik/komut tekrarı, anahtar reddi, hesaplar arası erişim, iptal/logout, düzenleme revizyonu/kimlik korunumu, gerçek cursor ilerlemesi, yeni kaynak ayrıştırıcıları ve migration dahil. Önceki 30 entegrasyon kontrolü bu sayıya dahildir; tüm ürün regresyonu değildir.
- `artifacts/test-results/source-catalog-v027.trx`: ilk tur 40/41; test fixture'ındaki eksik `after` alanının okunması düzeltildi. Sonrasında RSS giriş sayfası sınırı da kapsanıp 42 kontrol geçti.
- `artifacts/source-catalog-readonly-v027.json`: Home'un kendi sorgu oluşturucu/ayrıştırıcısıyla YTS **3**, EZTV **100**, API Bay **22**, Torrents.csv **1**, Nyaa **75**, Internet Archive **50** gerçek sonuç normalize edildi; beş üyelikli kaynağın caps'i geçti. DrunkenSlug alias'ı HTTP 301 verdi.
- `artifacts/source-catalog-readonly-v027-drunkenslug.json`: kanonik adres düzeltmesinden sonra **HTTP 200**, Home caps ayrıştırıcısıyla doğrulandı. Böylece **6 doğrudan metadata/RSS + 6 caps** okuması doğrulandı. Üyelikli kaynakların hesap araması ve tüm medya aktarımı/kullanıcı kabulü açıktır.
- Kaynak okuma aracı: `dotnet run --project tools/Home.SourceProbe -- <kanıt.json> [kaynak-id ...]`. Uygulamayı/veritabanını başlatmaz, kullanıcı anahtarı okumaz ve torrent/NZB/medya indirmez; yalnız HTTPS metadata/caps okur. Çıktıdaki `version`, `BuildInfo` sayısal bileşenidir; çalışmanın kaynak sürümü 0.27.0-dev.1'dir.

Ana dosyalar: `server/HomeServer.Api/Sources/SourceCatalog.cs`, `SourceSetupService.cs`, `SourceCatalogEndpoints.cs`, `SourceAdapterRegistry.Native.cs`, `Infrastructure/MediaStore.SourceSetups.cs`, `Infrastructure/Migrations/039_source_catalog.sql`, `web/src/components/SourceCatalog.tsx`, `SourceDialogs.tsx`.

# 0.17 — Katalog kimliği, bölüm kapsamı ve film parçaları

Tarih: **2026-10-13**, **0.17.0 yayımlandı**. Gereksinimler: **CAT-04/06/07, CAT-05/11, MON-01/02, ACQ-01/02, IMP-02/03, PLY-07/09**.

## Kimlikler ve alternatif adlar

- Kanonik yapım kaydı ve TMDB/TVmaze sağlayıcı kimlikleri ayrı tutulur. Birleştirme ortak IMDb veya TVDB kimliği gerektirir; bildirilen başka bir dış kimlik çelişiyorsa işlem reddedilir. Aynı sağlayıcının farklı yapım ID'leri otomatik veya manuel olarak bu komutla birleştirilmez.
- Önizleme dosya/istek sayısını, ortak kimliği, sürüm revizyonlarını ve bölüm karşılıklarını gösterir. Aynı sezon/bölüm, eşit başlık ve uyumlu tarih otomatik karşılık olabilir; diğer bölümler için kullanıcı tekil hedef seçer. Eşlenmemiş veya çoktan-bire bölüm karşılığı uygulanmaz.
- Dosyalar, bölüm üyelikleri, son kişisel konum, favori/puan, koleksiyon, istek/olay geçmişi, bildirim ve edinme bağlantıları tek DB işlemiyle kanonik ID'ye bağlanır. Kaynak katalog satırı tombstone olarak saklanır; eski içerik ID'si kanoniğe çözülür. İki aktif aynı kullanıcı/kapsam isteği birleştirilirken eski istek ve olayları saklanır.
- Yerel başlık/açıklamada hedef tercih edilir; eksik alan kaynak yerel değerinden alınır. Yaş sınırı daha sıkı olanı, erişim engeli iki kaydın birleşimini kullanır. Favori OR ile birleştirilir; puan çakışmasında hedef değer kalır. Eski kaynak satırları ve merge snapshot'ı izlenebilir durumdadır.
- Birleştirme etkin/henüz bağlanmamış transfer varken uygulanmaz. Değişen bölüm numaralarıyla çakışan aktif dar istek/edinme kapsamı önce düzenlenmelidir. Otomasyon kuralları birleştirmede durdurulur; arama sonucu ve katalog revizyonu hem grab öncesinde hem DB transfer komutunda kontrol edilir. Geç biten eski arama yeni edinme başlatamaz.
- TMDB alternative titles ve TVmaze AKA adları, ayrıca kullanıcı alternatif adları kütüphane aramasına ve release kimlik değerlendirmesine katılır. Kullanıcının yerel adları sağlayıcı yenilemesinde korunur. Sağlayıcı alias yenilemesi ana sağlayıcının metadata alanlarını ezmez.

## Bir dosyada birden fazla bölüm

- Dosya → bölüm üyeliği `media_episode_segments` ile **1–20 bölüm** içerir. `S01E01E02`, `S01E01-E03`, `1x01-03` açık kapsamları ayrıştırılır; tekrar, ters/geniş aralık, çok sezonlu belirsiz ad ve geçersiz tarih otomatik eşleştirme/edinmede reddedilir.
- Dosyanın kapsadığı her bölüm eksik/takvim/istek/otomasyon hesabına katılır. Birden fazla bölümlü kaynak sürümü hedef bölümü gerçekten içeriyorsa değerlendirilebilir. Aktif edinmenin bütün bölüm kapsamı diğer otomasyon işlerini bastırır.
- İsteğe bağlı başlangıç/bitiş saniyeleri çakışmasız ve gerçek dosya süresi içinde olmalıdır. Zamanlar biliniyorsa her bölüm konumu ayrı kaydedilir; henüz başlanmamış sonraki bölüm izlenmiş sayılmaz. Zamanlar yoksa dosyanın tamamlanması bütün kapsama uygulanır.
- Bölümden oynat düğmesi doğru dosya ofsetine gider. Oynatıcıda kayıtlı bölüm sınırlarıyla gezinme ve başka dosya sürümüne bölüm bazlı devam desteklenir.
- Medyanın ilişki revizyonu, eşleştirme kaldırılıp yeniden eklense de monoton artar. Eski oynatıcı konumu ve eski sağlayıcı altyazı sonucu yeni eşleştirmeye yazılamaz. Çok bölümlü/parçalı dosyada sağlayıcı altyazısı için dosya-hash eşleşmesi aranır.

## Çok parçalı filmler

- Aynı filme eşlenmiş, süresi okunmuş **2–20 dosya** bir sürümün sıralı parçaları olarak saklanır. Kullanıcı sıra ve sürüm adını düzenler; metadata eşleştirmesi kaynak dosyanın byte'larını değiştirmez.
- Film durumu bütün parçaların katalogda erişilebilir olmasını ve profil kütüphane/medya izinlerini gerektirir. Oynatma başlangıcında fiziksel dosya erişimi de kontrol edilir. Eksik veya profile kapalı parça varken bütün film oynatılabilir sayılmaz.
- İzleme konumu film düzeyinde tek zaman çizgisidir; her fiziksel oynatıcıya yerel ofset çevrilir. Devam doğru parçadan başlar; açık parça seçimi korunur. Film parça kuyruğu sıralıdır; önceki müzik shuffle/repeat tercihi bu sırayı değiştirmez.
- Doğrulanmış edinmede `CD1/CD2` veya `Part1/Part2` gibi kesintisiz, tekil sıralar aynı import işlemine bağlanır. Yalnız bir CD/part veya sırası eksik/tekrarlı paket otomatik yayımlanmaz. Yeniden import/link aynı kimlik ve parça grubunu korur.

## Günlük ve mutlak numaralar

- Kanonik sezon/bölüm kimliği sabit kalır; günlük dosya adı tam yayın tarihiyle eşlenir. Aynı günde birden fazla bölüm varsa kullanıcı sezon/bölüm karşılığını seçer. TVmaze `episodesbydate` kullanılır; TMDB için bölüm kataloğu filtrelenir. Günlük sağlayıcı işi iki dakikayla sınırlıdır.
- Anime/mutlak sıra açık numara haritasıdır: `mutlak=sezon:bölüm`. Kullanıcı haritası veya TMDB'nin **type 2 Absolute** episode-group sırası kullanılabilir. Tarihe bakıp mutlak sayı uydurulmaz.
- TMDB import, grubun seçilen diziye ait olduğunu, her episode `show_id`/ID'sini, sıfır tabanlı kesintisiz order alanını, kayıt sayısını ve tekrarsızlığı doğrular. Sonuç en fazla 20.000 bölümdür; kanonik TMDB dizi kaydı ve yenilenmiş bölüm kataloğu gerekir.
- Kaynak araması seçilen günlük/mutlak etiketi `q` yeteneği üzerinden gönderir; desteklemeyen kaynak açık hata verir. Edinmede numara profili snapshot'ı saklanır: daha sonra kullanıcı numaralandırmayı değiştirirse başlamış transfer eski doğrulanmış haritasını kullanır.

## Şema ve uygulama yüzeyi

Şema **19 → 20**, `020_catalog_identity.sql`; yayımlanmış önceki migration'lar korunur. Kaynak medya üzerinde yazma/silme yapılmaz.

- `Infrastructure/MediaStore.CatalogIdentity.cs`: alias/tombstone, önizleme, transaction, kişisel veriler ve geçmiş.
- `Infrastructure/MediaStore.Composition.cs`: bölüm üyelikleri, film parça grupları, erişim/oynatma ve konum dönüşümü.
- `Infrastructure/MediaStore.Numbering.cs`: açık sıra, source snapshot, geç arama/grab revizyon kontrolü.
- `Metadata/MetadataProvider.Identity.cs`, `CatalogIdentityEndpoints.cs`, `FilenameHints.cs`: gerçek sağlayıcı sözleşmesi, API ve ad ayrıştırma.
- `Downloads/AcquisitionValidator.cs`, `MediaStore.Acquisition.cs`, `Sources/`: gerçek probe/import, çok bölüm/film parçası ve tekrar.
- `web/src/components/CatalogControls.tsx`, metadata/ayrıntı/oynatıcı: gerçek API'ye bağlı kimlik, sıra, bölüm zamanı ve parça kontrolleri.

Başlıca API: `/content/{id}/identity`, `/identity/refresh`, `/identity/names`, `/identity/preview`, `/identity/merge`; `/content/{id}/numbering`, `/numbering/groups`, `/numbering/import`; `/metadata/episode-selection`; `/media/{id}/episodes`, `/sequence`; `/content/{id}/movie-editions`.

## Kaynaklar — 2026-10-13

- [TVmaze API](https://www.tvmaze.com/api): AKA, episodebynumber, episodesbydate, özel bölümler, rate limiting ve CC BY-SA atfı.
- Context7 `/websites/developer_themoviedb`: [TV episode groups](https://developer.themoviedb.org/reference/tv-series-episode-groups), [group details](https://developer.themoviedb.org/reference/tv-episode-group-details), [film alternatif adları](https://developer.themoviedb.org/reference/movie-alternative-titles), [dizi alternatif adları](https://developer.themoviedb.org/reference/tv-series-alternative-titles).
- Mevcut React/HeroUI v3 bileşen kalıpları ve kurulu TypeScript tipleri kullanıldı; yeni kod bağımlılığı eklenmedi. Veri lisansları `docs/dependencies.md` içinde.

## Doğrulama

- **185/185 backend/native test, 0 atlanan**; `artifacts/test-results/home-0.17.0-full.trx`. Native FFmpeg/ffprobe, PAR2, NVENC, gerçek published-server HTTPS ve mevcut müzik testleri dahil. Saf ses-zamanlayıcı kontrolleri **5/5**; frontend build başarılı. Browser otomasyonu çalıştırılmadı.
- Kimlik çelişkisi, eski preview, açık bölüm karşılığı, kişisel konum/favori/koleksiyon/istek korunumu, profile göre parça erişimi, monoton ilişki revizyonu, numara snapshot'ı ve geç arama/grab yarışı doğrulandı.
- Gerçek FFmpeg videolarıyla iki parçalı film ve tek dosyada iki bölüm, atomik import/tarama/kimlik bağlantısı ve yeniden import/link doğrulandı; kaynak hash'leri aynı kaldı. Kısa dosyada bilinmeyen bölüm sınırının erken izlenmiş işaretlenmemesi de test edildi.
- TMDB absolute-order parent/episode/order, yanlış yapımın alternatif adını reddetme ve TVmaze günlük tarih/AKA sözleşmeleri HTTP fixture ile geçti.
- Windows x64/Linux x64/Linux ARM64 paketleri aynı güvenilen anahtarla üretildi: `artifacts/packages/home-0.17.0-{win-x64,linux-x64,linux-arm64}.zip`. Linux çalıştırma kabulü açık.
- İzole gerçek süreç **0.16.0/şema 19 → 0.17.0/şema 20 → önceki sürüm/veri** geçti; hesap korundu, restore oturumları iptal etti (`artifacts/update-roundtrip-0.16.0-0.17.0.json`).
- Canlı `http://localhost:5080` 0.17.0'a geçti; sahip oturumu korundu, DB read-only sorgusu şema 20 döndürdü. Gerçek **Başlangıç 27205** ve **Sherlock 335** metadata'sı doğrulandı. Canlı takip/katalog kaydı boş olduğundan alias birleştirme/grup importu canlı kullanıcı kabulü açık tutuldu. **0 indirme, 0 etkin sentetik kütüphane**; doğrulama katalog mutasyonu ve internet medya edinmesi başlatmadı (`artifacts/live-runtime-v017.json`, `tools/verify-live-catalog.mjs`).

## Kabul — ACCEPT-17

1. Gerçek bir dizi ayrıntısında **Katalog kimlikleri ve alternatif adlar** bölümünü aç; bölüm/ad yenilemesini ve aynı yapımın diğer sağlayıcı kaydı önizlemesini kontrol et.
2. Gerçek çok bölümlü dosyada **Dosyanın bölüm kapsamı ve zamanları** ile üyelikleri/başlangıçları kaydet. Bölüm listesi, oynatıcı bölüm seçimi ve ayrı konumları dene.
3. Gerçek film parçalarında **Çok parçalı film sürümleri** ile sırayı kaydet; ikinci parçadan devam ve açık parça seçimini kontrol et.
4. Günlük veya anime dizisinde **Bölüm numaralandırması** ayarını seç; kullanılan kaynak ve kanonik karşılıklar görünmeli.

Birden-çoğa sağlayıcı bölüm dönüşümü, çakışan upstream sezon yeniden numaralandırmasının onarımı, DVD/digital alternatif sıralar ve otomatik bölüm zaman segmentasyonu daha ileri katalog kapsamıdır. Gerçek indexer/transfer, browser/cihaz ve platform dağıtım kabulü açık kapılardır; bütün Home kapsamı tamamlanmış sayılmaz.

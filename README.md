# Home

Kendi sunucunda çalışan, kendi mimarisine sahip bütünleşik medya uygulaması.

**C# / ASP.NET Core (.NET 10 LTS) + SQLite** çekirdeği; **React + HeroUI v3 + Motion** arayüzü.

## Geliştirme sürümü: 0.43.0 / şema43

Music/Games/Live TV arayüzü, kişisel kütüphane, IPTV ve sunucuda ROM yürütme desteği. Home'u kendi sunucuna kurar, ev ağındaki cihazlardan sunucunun adresiyle kullanırsın.

**CasaOS / Intel-AMD x64:** [Compose dosyasını](https://raw.githubusercontent.com/lumax90/homeOS/main/deploy/casaos/docker-compose.yml) CasaOS **Custom Install → Import** ekranına aktar. **`ghcr.io/lumax90/homeos:0.43.0`** imajı otomatik indirilir ve veri klasörü hazırlanır. Kurulumdan sonra kendi sunucunun adresiyle **`http://SUNUCU-IP:5080`** üzerinden bağlan. [Kurulum kılavuzu](deploy/casaos/README.md).

**109 backend +68 Node**, yedi özgün sistemle gerçek LAN WebRTC görüntü/ses/kumanda ve kayıt, read-only medya range/HLS, container kapanış/yeniden oluşturma ve mevcut verinin korunumu doğrulandı. [Kanıt ve kapsam](docs/headless-lan-043.md). **Public imajın hesapsız indirme ve temiz kurulum kontrolleri geçti.** Fiziksel cihaz/gerçek oyun kabulü, ARM64 native, Umbrel mağaza paketi ve nihai dağıtım lisansı açık konulardır. Gamepad/PWA gibi tarayıcı özellikleri bazı cihazlarda güvenilir HTTPS ister.

## 0.27 geliştirme birleşimi — tarihsel kayıt

Kalan yedi alanın backend/arayüz kodu eklendi: kişi/listeden koleksiyon, ASR/ileri senkron, Cast/AirPlay/DLNA/cihaz kodu, atlama aralıkları, ileri operasyonlar, adaptör SDK'sı ve canlı TV/DVR/kitap/fotoğraf/podcast.

Tür keşfi ve 12 hazır kaynakla rehberli kurulum da eklendi. 0.27 döneminde runtime sürümü 0.27.0, şema 39 idi. Derlemeler, 42 odaklı backend kontrolü, ayrı 8 metadata/API kontrolü ve Windows güncelleme/geri dönüş doğrulaması geçti. **Geniş test ve cihaz/kaynak kabulü açıktır.** [Önizleme yayını](docs/preview-027.md), [kapsam ve giriş noktaları](docs/development-completion.md).

## Son tam regresyonu geçen yayın: 0.26.0

- Gerçek TMDB/TVmaze/MusicBrainz keşfi, hane hesapları/PIN/yaş-kütüphane izinleri, kişisel geçmiş ve koleksiyonlar.
- Torznab/Newznab, opt-in otomasyon, custom-format/kalite kararları; dahili BitTorrent ve NNTP/NZB, yEnc/PAR2/arşiv doğrulaması.
- Gerçek ses/altyazı izleri, HLS/remux/transcode; mevcut NVIDIA cihazında NVENC ve HDR10/HLG→SDR doğrulaması.
- OpenSubtitles adaptörü; MusicBrainz yayın/recording ve kapaklar, kullanıcı seçimiyle doğrulanmış albüm edinme/import.
- Çoklu sanatçı/konuk katkıları, koleksiyon/listeden toplu takip ve opt-in kalite yükseltmesi; PCM/gapless/ReplayGain, sonic öneri, ELRC/seek ve müzik videosu/ilişkili albümler.
- Yönetilen FLAC/MP3/M4A/Ogg/Opus/WAV/AIFF etiket/kapak/tarih ve geri alma; kaynak ses, metadata önizlemesi ve publish hash'leriyle doğrulama.
- Kanonik sağlayıcı kimlikleri ve alternatif adlar; çok bölümlü dosyalar, günlük/anime numaraları; sıralı film parçaları ve doğru parçadan devam.
- Dosya bazlı altyazı tercihleri, yükseltme/geçmiş; PGS/DVD/ASS görüntüye işleme ve yerel VAD ile altyazı zaman eşleme.
- Direct/HLS/PCM için ortak hesap/cihaz oynatma sınırları; açık oynatmaları yönetme, iptal ve oturum geçmişi.
- Olay filtreli webhook/HMAC ve SMTP/TLS bildirimleri; kalıcı gönderim kuyruğu, saatlik deneme bütçesi ve teslim geçmişi.
- Web/PWA push; VAPID/AES128GCM, kullanıcı/cihaz aboneliği, profil geçişinde iptal ve genel kilit ekranı bildirimi.
- Şifreli çevrimdışı cihaz kütüphanesi; MP4/AAC hazırlığı, parça aktarımı/devam, kota, yerel seek ve çakışma korumalı konum eşitleme.
- Hesap davetli birlikte izleme/dinleme; ortak konum/hız, hazır bariyeri ve açık web oynatıcısına kişisel uzak kumanda.
- Hesap bazlı indirme sahipliği, aylık edinme/paket boyutu ve eşzamanlı aktarım bütçesi; kullanım geçmişi ve otomasyonda kota sonrası devam.
- Kimlik korumalı bölüm sırası onarımı, birden-çoğa sağlayıcı eşlemesi, DVD/dijital kaynak sıraları ve dosyadan bölüm sınırı önerileri.
- Kütüphane izleme/dışlama/arşiv/rebind, yerel NFO, tutarlı yedek/state restore, imzalı update/rollback, HTTPS/proxy/path-base ve platform paketleri.

**326 backend/native + 40 Node kontrolü geçti, atlanan 0.** Önizleme: **http://localhost:5080**. [Kurulum/paketler](deploy/README.md), [güncel doğrulamalar](docs/implementation.md), [tam kapsam ve açık işler](docs/product/execution.md). Tam ürün kapsamı henüz tamamlanmadı; gerçek kaynak, cihaz, Linux/Docker ve dağıtım kapıları ayrıca izlenir.

Aşağıdaki ilk sürüm başlıkları tarihsel gelişim özetidir.

## İlk çalışan dilim

- Yerel hesap kurulumu, parola hashleme, cookie oturumu ve CSRF doğrulaması.
- Klasör bağlama; kalıcı, iptal edilebilir ve yeniden başlatmada toparlanan tarama işleri.
- Kaynak dosyalarını yalnızca okuyarak katalog oluşturma.
- ffprobe ile süre/codec bilgileri, FFmpeg ile önizlemeler.
- Türkçe karakterlere duyarlı normalize arama, kart/liste görünümleri, içerik ayrıntıları.
- HTTP Range destekli doğrudan oynatma ve kaydedilen izleme konumu.
- Oynatma kesilmeden küçülen/büyüyen oynatıcı.
- Gerçek sunucu durumuna bağlı aktivite ekranı, cam navigasyon ve azaltılmış hareket desteği.

## 0.2: içerik bilgileri ve indirmeler

- Film/dizi/bölüm kimliğini fiziksel dosyadan ayıran kalıcı içerik modeli.
- TVmaze üzerinden anahtarsız dizi/bölüm arama ve eşleştirme.
- TMDB üzerinden film/dizi eşleştirme; okuma token'ı **Ayarlar → İçerik bilgileri** bölümünden bağlanır.
- Aynı içerik/bölüm için dosya sürümleri ve yeniden taramada korunan eşleştirme.
- Kaynak atıflı özet, tür ve sunucuda önbelleklenen afiş/görseller.
- Uygulama içinde **MonoTorrent 3.0.2**: magnet, torrent dosyası, duraklat/devam/iptal, kalıcı niyet ve fast-resume.
- Doğrulanan medyanın uygulamanın yönettiği **İndirilenler** kütüphanesine idempotent aktarılması.

## 0.3: gerçek keşif ve takip

- TMDB film/dizi keşfi ve TVmaze gerçek dizi kataloğu; sayfalı arama ve sağlayıcı ayrıntıları.
- Dosya gerektirmeyen takip listesi, kalite tercihi, bölüm yenilemesi, eksikler ve yayın takvimi.
- Torznab/Newznab kaynak ayarları, yetenek testi, arama ve açıklanabilir kalite/ret nedenleri.
- Kaydedilmiş sürüm seçimiyle dahili torrent edinmesi; gerçek dosya doğrulaması ve kanonik içeriğe kontrollü bağlantı.
- PWA manifesti ve çevrimdışı uygulama kabuğu; gerçek kullanıcı verisi API üzerinden gelir.

## 0.4.1: web oynatma uyumluluğu

- Gerçek ses/altyazı/chapter envanteri; yerel ve gömülü metin altyazısı, yükleme ve senkron gecikmesi.
- Direct, HLS remux, AAC ses ve H.264 yazılımsal SDR dönüşümü; ses/kalite/hız/sahne seçimi.
- İptal edilebilir seek, korunan mini oynatıcı, sıralı konum kaydı ve sınırlı geçici dosya/süreç yaşam döngüsü.
- Kalite doğrulamasında duran tamamlanmış edinmeyi, seçtiğin yeni import profiliyle tekrar değerlendirme.

Tam ürün planı [docs/product/](docs/product/README.md); bütün sürümlerin doğrulama kaydı [implementation.md](docs/implementation.md) ve `docs/milestone-*.md` belgelerindedir.

## Geliştirme

Gereksinimler: .NET 10 SDK (10.0.4xx), Node.js 24 LTS. Medya analizi ve önizlemeler için `ffmpeg` ve `ffprobe`.

```sh
npm ci
```

Windows'ta iki terminal:

```powershell
powershell -ExecutionPolicy Bypass -File tools/dev.ps1 -Mode Server
```

```powershell
powershell -ExecutionPolicy Bypass -File tools/dev.ps1 -Mode Web
```

- Arayüz: http://localhost:5173
- API: http://localhost:5080/api/health
- İlk açılışta kendi hesabını oluştur; ardından sunucudaki medya klasörünün tam yolunu ekle.
- Windows geliştirme betiği verileri `.data/app` altında tutar. `-Preview` seçeneği ayrı `.data/preview` kullanır.
- Betik sistemdeki araçları, ardından bu geliştirme ortamının geçici araç önbelleğini arar.

Diğer ortamlarda sunucuyu şu değişkenlerle çalıştırabilirsin:

```sh
Home__DataPath=/absolute/path/to/home-data dotnet run --project server/HomeServer.Api --no-launch-profile
```

Arayüz için ayrı terminalde `npm run dev` çalıştır. `Home__FfmpegPath` ve `Home__FfprobePath` verilmezse araçlar PATH üzerinde aranır.

## Gerçek veriler

TMDB token'ını **Ayarlar → İçerik bilgileri**, kaynak API bilgilerini **Kaynaklar ve kalite** bölümünden ekle. TVmaze dizi araması anahtarsızdır. Medya klasörünü kütüphaneye bağlayabilirsin. Katalog/arama işlemleri indirme başlatmaz.

Önceki sentetik kütüphane 0.3'te arşivlenir. Eski `create-demo.mjs` ve [kaynak kaydı](docs/demo-assets.md) yalnızca tarihsel/izole test malzemesidir.

## Kontroller

```sh
npm run build
npm run test:audio
npm run test:push
npm run test:offline
npm run test:rooms
npm run test:lyrics
dotnet test HomeServer.slnx
```

Native kontroller için `HOME_TEST_FFMPEG`, `HOME_TEST_FFPROBE`, `HOME_TEST_PAR2`, güncel yayımlanmış sunucu için `HOME_TEST_SERVER` ve mevcut NVIDIA test cihazında `HOME_TEST_NVENC=1` kullanılır. Değişken verilmediğinde ilgili native test atlanır; 0.26 doğrulamasında **326/326, atlanan 0**. Yerel konuşma fixture'ı Windows/SAPI, SMTP testleri yerel TLS sunucusu kullanır. `test:audio`, `test:push`, `test:offline`, `test:rooms` ve `test:lyrics`, tarayıcı otomasyonu olmadan ses/worker/şifreli depo/oda/söz gezinmesi durumlarını doğrular. Fixture'lar izole test alanındadır; internet medya transferi yapılmaz. Frontend build ile .NET build/test/publish sırayla çalıştırılır.

Browser/cihaz kabulü kullanıcıya aittir. Yeni dilimlerde otomatik browser suite çalıştırılmaz. Aşağıdaki eski 0.2 e2e betiği tarihsel olarak korunur; yeni katalog akışının kabul kanıtı değildir. Açıkça istenirse ayrı test ortamında güncellenebilir:

```powershell
npm run build
powershell -ExecutionPolicy Bypass -File tools/dev.ps1 -Mode Server -Test -Port 5082
```

Başka bir terminalde:

```powershell
$env:HOME_E2E_URL = 'http://127.0.0.1:5082'
npm run test:e2e
```

Test sunucusu `.data/e2e-v02` kullanır ve BitTorrent bağlantılarını loopback ile sınırlar. Testler `Home` / `HomePreview2026!` hesabını oluşturur, örnek kütüphaneyi içeri aktarır ve sentetik bir torrentin kontrollerini dener. Başka test hesabı için `HOME_E2E_NAME` / `HOME_E2E_PASSWORD` kullanılabilir. Normal veriler `.data/app`, kullanıcıya gösterilen önizleme `.data/preview` altındadır.

İlk kez çalıştırırken `npx playwright install chromium` gerekebilir. API testleri her test için ayrı geçici veri klasörü oluşturur.

## Tek sunucudan yayınlama

```sh
npm run build
dotnet publish server/HomeServer.Api -c Release -o artifacts/server
```

Derlenmiş frontend `wwwroot` altında sunucuya eklenir. Yayınlanan sunucu API'yi ve arayüzü aynı adresten sunar.

`Dockerfile` ve `deploy/compose.yaml` tek uygulama paketini tarif eder. Docker/Linux/ARM64 doğrulama durumu [implementation.md](docs/implementation.md) içinde tutulur. Kütüphane veritabanı yerel veri diskinde, medya mount'u ayrı ve salt okunurdur. İmzalı paket üretimi `tools/package.ps1`; kurulum ve güncelleme [deploy/README.md](deploy/README.md) içindedir.

## Belgeler

- [Mimari karar ve kaynaklar](docs/architecture.md)
- [Uygulama durumu ve doğrulamalar](docs/implementation.md)
- [HTTP API ve oturum sözleşmesi](docs/api.md)
- [0.2 içerik/indirme mimarisi ve kaynaklar](docs/milestone-02.md)
- [0.3 keşif/takip/kaynak dilimi](docs/milestone-03.md)
- [0.4 iz/altyazı/HLS dilimi](docs/milestone-04.md)
- [Bağımlılık ve veri lisansları](docs/dependencies.md)
- [Ürün planı ve kabul kapıları](docs/product/README.md)
- [Demo varlıkları](docs/demo-assets.md)

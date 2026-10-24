# Genel tarayıcı oynatma verimliliği — PLAY-04

Tarih: **2026-10-23**. Gereksinimler **PLY-01/02/03/05/06, CLI-01-A, OPS-08**. Durum: **2026-10-24'te 0.45.0 / stable ile yayımlandı; gerçek cihaz/kaynak kabulü bekliyor**. [Yayın kaydı](playback-streaming-batch-045.md).

## Yönlendirme ve kaynak

Kullanıcı, film oynatmanın genel Home/tarayıcı akışında iyileştirilmesini istedi. Kararlar cihaz markası/modeli veya TV beyaz listesi ile verilmez. Jellyfin'in görüntü/ses/kapsayıcı kararlarının ayrılması, görüntü kopyalamanın önceliklendirilmesi ve üretim/tüketim konumuna göre cache yönetimi [kaynak kodundan incelendi](playback-efficiency-research.md).

## Uygulanan değişiklikler

- **Gerçek kaynak profili:** ffprobe analizi codec yapılandırma kaydından AVC/HEVC kimliği, profil/level, bit derinliği ve fps bilgisini çıkarır. Önceki analiz JSON'ları dosya parmak izi korunarak ihtiyaç halinde yenilenir; şema değişikliği yoktur.
- **Tarayıcı yetenek raporu:** seçili görüntü/ses için direct dosya, fMP4-HLS ve TS-HLS yolları ayrı sorgulanır. `canPlayType`, `MediaSource.isTypeSupported` ve varsa `MediaCapabilities.decodingInfo` birlikte kullanılır. Destek bildirimi dosya parmak izi ve seçili iz kimliklerine bağlıdır; başka dosya/iz için tekrar kullanımı reddedilir. Eski/eksik API'lerde biçime özel ipuçları ve sınırlı oynatma fallback'i vardır.
- **Dönüşüm sırası:** direct play → görüntü/ses kopyası → görüntü kopyası + yalnız ses dönüşümü → gerekli görüntü dönüşümü. HEVC fMP4 remux/ses uyarlaması özgün çözünürlük ve desteklenen HDR bilgisini korur. Native HLS, raporlanan kaynak desteği gerektiriyorsa seçilir; mevcut masaüstü MSE önceliği marka kontrolü olmadan korunur.
- **Ses desteği:** kaynağın çok kanallı sesi gerçekten destekleniyorsa otomatik stereo zorlaması yapılmaz. Bu dilimde HLS ses kopyası AAC/MP3/AC-3/E-AC-3 ile sınırlıdır; diğer ses biçimleri gerekiyorsa AAC stereo olur. Video kopyası bağımsız seçilir.
- **Gerekli dönüşümler:** kullanıcının kalite/bitrate sınırı, desteklenmeyen görüntü veya altyazı yakma video dönüşümünü sürdürebilir. HDR tone mapping filtresi yalnız gerçekten gerekiyorsa kontrol edilir; özgün HDR kopyasında NVENC/tone-map başlatma denemesi yapılmaz. Dolby Vision'ın dinamik metadata remux yolu bu dilimde doğrulanmadığından o yol açılmaz; doğrulanmış doğrudan kaynak desteği ayrı değerlendirilir.
- **Sınırlı üretim:** oynatmanın geçici alan rezervasyonu kalan filmin tamamı yerine yaklaşık bir üretim penceresine dayanır. Dosya büyüklüğü tek başına video dönüşümünü tetiklemez. Varsayılan `Home:Playback:AheadSeconds=90` (12–120 saniye), mevcut `CacheGiBPerSession` üst sınırı ile birlikte kullanılır. FFmpeg gerekli pencereyi üretince bekletilir, tüketim ilerledikçe devam eder. Bildirimli kontrolün yanında periyodik kontrol ve gerçek byte üst sınırı vardır.
- **Segment yaşam döngüsü:** ilk manifest, keyframe hizalı başlangıcın sıfır zamanını korur; istenen konumun en az sekiz saniye ilerisine kadar hazırlanır (kısa tamamlanmış dosyalar hariç). İlk gerçek ilerleme öncesinde başlangıç parçaları listeden çıkarılmaz. Sonrasında eski parçalar manifestten düşer ve eski manifest süresi + parça süresi boyunca erişilebilir kalır. Temizlenen aralığa seek, aynı içerik konumundan yeni oturum oluşturur. hls.js, üreticinin izleyiciyi beklemesini yayın kesintisi/canlıya atlama olarak değerlendirmez.
- **İşlem kontrolü:** yalnız Home'un başlattığı FFmpeg child process'i bekletilir. Linux'ta aynı kullanıcıya ait süreç sinyalleri, Windows'ta süreç bekletme/devam arayüzü kullanılır. Askıdayken kapatma ve normal lease cleanup çalışır; orijinal dosyaya yazılmaz. Standart FFmpeg 6.1.1 ve 9.0.2 ile uyumlu seçenekler kullanılır.
- **Görünür karar:** oynatma tercihleri direct/remux/yalnız ses/video dönüşümünü, kaynak/hedef codec'leri, tarayıcı tamponunu, geçici alanı, üretim bekleme durumunu ve FFmpeg'in üretim hızını gösterir. Bu hız, ham CPU benchmark'ı değildir; kontrollü üretim/beklemeyi de içerir.
- **Mevcut sözleşmeler:** init.mp4 ve .m4s parçaları da oturum/hesap iznine ve HTTP Range/HEAD davranışına bağlıdır. Tam dosya üreten offline hazırlığın boyut bütçesi ayrı kalır. Ağ hatası tek başına codec uyumsuzluğu kabul edilmez; direct decode hatasında önce yeniden paketleme denenir.

## Doğrulama

- Playback/lease/range, offline, müzik analizi, oda ve yaşam döngüsü kapsamıyla **77 kontrol, sıfır atlanan** geçti. Son başlangıç/seek zaman tabanı düzeltmesinden sonra **32 ilgili kontrol, sıfır atlanan** geçti: toplam **78 farklı backend/native kontrolü**.
- **26 Node kontrolü:** kaynak bazlı HEVC, MSE/file farkı, gerçek çözünürlük/fps ile MediaCapabilities sorgusu, çok kanallı ses, eski API fallback'i, iptal, tampon aralıkları, ortak transport, Video.js zaman çizelgesi ve yerelleştirme.
- **Gerçek Linux non-root konteyner / FFmpeg 6.1.1:** yerel üretilen 3840×2160 HEVC Main10/HDR fixture'ı fMP4'e taşındı; görüntü codec/çözünürlük/piksel/transfer bilgisi korundu ve AC-3 5.1 ses AAC stereo oldu. Init/segment Range=206, anonim ret, üretim beklet/devam/askıdayken kapat, kaynak byte'ları ve oturum korunumu geçti. Yedi kontrol: `tools/verification/verify-playback-efficiency.py`.
- Windows gerçek FFmpeg 9.0.2 testleri ses dönüşümü, HDR, askı/devam, kapatma ve HTTP izin/range çıktılarını doğrular. Geçici sentetik kaynaklar yalnız izole test dizinlerindedir.
- Son kaynak durumuyla frontend/worker/PWA ve Linux image build geçti; yedi gerçek non-root konteyner kontrolü son görüntü üzerinde yeniden geçti.
- Workflow'daki Node yetenek/transport ve gerçek konteyner 4K/üretim kontrolleri **0.45.0 hosted yayını sırasında da geçti**. Aynı izole Node konteyner komutunun 10 testi ve actionlint ilk dilimde doğrulanmıştı.

Yerel kanıtlar: `artifacts/playback-efficiency/`. Kullanıcının gerçek filmleriyle sürekli oynatma, uzun duraklatma, ileri/geri sarma ve farklı tarayıcılar **ACCEPT-PLAY-04** kapsamındadır. İnternet medya indirmesi veya otomatik browser suite çalıştırılmadı.

## Ek kaynaklar

2026-10-23 tarihinde güncel belgeler ve kurulu hls.js 1.7.3 kaynak/tipleri kontrol edildi:

- [FFmpeg streamcopy, HLS/fMP4 ve progress seçenekleri](https://ffmpeg.org/ffmpeg-all.html)
- [MediaCapabilities.decodingInfo](https://developer.mozilla.org/en-US/docs/Web/API/MediaCapabilities/decodingInfo)
- [hls.js API](https://github.com/video-dev/hls.js/blob/master/docs/API.md), kurulu `base-playlist-controller.ts`, `error-controller.ts`, `config.ts` ve `hls.d.ts`
- [RFC 8216 §6.2.2 — segment retention](https://www.rfc-editor.org/rfc/rfc8216#section-6.2.2)
- [RFC 6381 — codec identifiers](https://www.rfc-editor.org/rfc/rfc6381)

Bu değişiklik yeni medya yönetim ürünü/servisi veya codec bağımlılığı benimsemez; Home'un mevcut FFmpeg ve hls.js entegrasyonunu iyileştirir.

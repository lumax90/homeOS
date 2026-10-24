# Canlı TV akıcılığı — LIVE-STABILITY-01

Tarih: **2026-10-23**. Gereksinimler **EXT-01/02, PLY-02/03/06, USR-03, OPS-08**. Durum: **0.44.0 tabanı üzerinde yerel kaynakta hazır; toplu yayın ve gerçek kanal/cihaz kabulü bekliyor**.

## Neden ve yaklaşım

Önceki Live TV yolu her görüntülü kanalı yazılımla H.264'e kodluyordu. İstemci ayrıca her heartbeat hatasında oynatmayı kapatıyor, fatal HLS hatalarında kontrollü toparlanma uygulamıyordu. Film tarafındaki [kaynak bazlı tarayıcı yetenekleri](playback-efficiency.md) canlı akışa uyarlandı; marka/model listesi kullanılmaz.

## Uygulama

- Kullanıcının **Oyna** komutu önce mevcut hesap/giriş/cihaz/tuner sınırları içinde bir canlı oturum ayırır. Kaynak, özel loopback relay üzerinden süre/boyut sınırlı ffprobe analiziyle okunur. Kaynak ekleme, kanal gezme ve rehber işlemleri medya bağlantısı başlatmaz.
- Hazırlık yanıtındaki gerçek izler için tarayıcı destek raporu üretilir. Ayrı `sessions/{id}/start` komutu raporu oturum/iz kimliklerine bağlar. Eşdeğer yapılandırma tekrarları yeni üretici başlatmaz; kapatılmış veya başka girişe ait oturum yapılandırılamaz.
- Uyumlu görüntü ve ses **kopyalanır**; gerekiyorsa yalnız ses AAC'ye çevrilir. Gerçek görüntü uyumsuzluğunda H.264 uyarlaması kullanılır. fMP4/TS ve native-HLS/hls.js seçimi tarayıcının kaynak için bildirdiği desteğe bağlıdır.
- Canlı üreticiden gereksiz `-re` kısıtlaması kaldırıldı; kaynağın mevcut HLS parçaları başlangıç tamponuna hızlı aktarılabilir. İki fazlı hazırlık için başlangıç bütçeleri ve kullanıcı iptali vardır. En az sekiz saniyelik hazır içerik veya tamamlanmış kısa akış beklenir.
- MPEG-TS içindeki AAC'nin fMP4'e kayıpsız aktarımında `aac_adtstoasc` kullanılır. `init.mp4`, oturumun çalışma dizinine yazılır; paralel oturumların init dosyaları birbirine karışmaz.
- Relay, **408/429/500/502/503/504** veya geçici bağlantı hatalarını çıktı başlamadan önce en fazla iki kez tekrarlar. Her denemede erişim tekrar doğrulanır; Range ve izinli sağlayıcı başlıkları korunur. FFmpeg'in sınırlı HTTP reconnect seçenekleri de kullanılır. `RemoteMediaHttp` adres/redirect/başlık sınırları geçerlidir.
- İstemci düşük gecikme yarışını kapatır; başlangıç canlı geriliği **12 saniye**, donmalarda kademeli hedef üst sınırı **24 saniye** olur. “Canlıya dön” de bu tampon payını korur. Daha sağlam tamponun karşılığı canlıya göre ek gecikmedir.
- Tek geçici heartbeat hatası çalışan medyayı kapatmaz. Medya hatasında bir HLS kurtarma denemesi; kaynak kopması/ilerlememede **500/1500/3000/6000 ms** aralıklı, en fazla dört yeniden bağlantı vardır. Kısa bir oynatma olayı deneme bütçesini sıfırlamaz; en az 60 saniyelik sağlıklı dönem gerekir. Yetki/kaynak iptali terminaldir.
- Kanal değişimi, kapanış ve eski asenkron yanıtlar epoch/lease sahipliğiyle korunur. Geç hazırlık yanıtı yalnız kendi oturumunu bırakır. Duraklatılan oynatma için otomatik bağlantı bekler; devam komutunda yenilenir. Decode hatasında uyumluluk fallback'i vardır; ağ hatası görüntü codec'ini değiştirme nedeni sayılmaz.
- Başlangıç cache toparlaması sunucu kabulünden önce tamamlanır. Eski bilinen sahipli dizinler temizlenir; bilinmeyen işaretçiler korunur. Oturum cache üst sınırı varsayılan **8 GiB**, yayın bitrate'ine göre hesaplanan pencere en fazla **30 dakika**dır. Kaynak/tuner ve DVR ortak sınırları sürer.
- Oynatıcı “özgün yayın”, “yalnız ses dönüşümü” veya “görüntü dönüşümü” durumunu gösterir. Tamponlama ve yeniden bağlantı mesajları English/Türkçe kapsamındadır.

## Doğrulama

- **71 backend/native kontrolü, sıfır atlanan:** canlı protokol/planlama, kaynak ve giriş sınırları, tekrar/idempotence, geç probe iptali, DVR/lifecycle, film iyileştirmesi ve playback kotası.
- Gerçek Windows FFmpeg **9.0.2** ile H.264/AAC remux ve H.264/AC-3 → yalnız AAC dönüşümü geçti. İlk 48 kare hash'i kaynakla eşleşti; kaynak dosya byte'ları korundu.
- **28 Node kontrolü:** canlı tampon hedefi, sınırlı retry, sağlıklı dönem şartı, iptal/eski yanıt sahipliği, genel tarayıcı yetenekleri/transport ve yerelleştirme.
- Frontend/worker/PWA build ve actionlint geçti.
- **Linux non-root, read-only-rootfs konteyner / FFmpeg 6.1.1:** sekiz gerçek HTTP kontrolü geçti — remux, yalnız ses dönüşümü, özel sentetik HLS kaynağının 503 hatasından toparlanma, actor/CSRF, idempotent start, fMP4 Range=206/anonim ret, kaynak iptali, kaynak byte'ları ve kullanıcı oturumu korunumu.
- Gelecek image yayınına Node ve gerçek canlı protokol kontrolü eklendi. Hosted yayın kullanıcı toplu push istediğinde çalışacaktır.

Kanıtlar `artifacts/live-stability/` altında yereldir. Araçlar `tools/verification/verify-live-stability.py` ve `live-fixture-origin.mjs`; kaynak medya yerel üretilir, origin özel test ağındadır. İnternet medya edinimi veya otomatik tarayıcı süiti çalıştırılmadı. Gerçek kanal kaynağının uzun süreli erişilebilirliği ve kullanıcı tarayıcısındaki akıcılık **ACCEPT-LIVE-STABILITY-01** kapsamındadır.

## Kaynaklar ve uygulama sınırı

2026-10-23 tarihinde Context7 ve kurulu hls.js **1.7.3** tipleri ile kontrol edildi:

- [FFmpeg HTTP reconnect, rw_timeout ve HLS seçenekleri](https://ffmpeg.org/ffmpeg-all.html)
- [FFmpeg aac_adtstoasc](https://ffmpeg.org/ffmpeg-bitstream-filters.html#aac_005fadtstoasc)
- [hls.js API — liveSyncDuration, lowLatencyMode, load policies ve recoverMediaError](https://github.com/video-dev/hls.js/blob/master/docs/API.md)
- [Plex/Jellyfin kaynak incelemesi](playback-efficiency-research.md)

Kaynağın uzun süre veri göndermemesi veya istemcinin gerçekten desteklemediği codec, tamponla ortadan kaldırılamaz. Kaynak hatası kontrollü ve açıklanabilir kalır; sınırsız yeniden bağlantı yapılmaz. Yeni ürün, sağlayıcı, codec veya dataset bağımlılığı benimsenmedi.

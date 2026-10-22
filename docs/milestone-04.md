# 0.4 — Medya izleri ve web oynatma uyumluluğu

Doğrulama: **2026-10-12**. Çalışan yayın: **0.4.1**, `artifacts/server-v0.4.1`, `http://localhost:5080`. Backend/build kontrolleri geçti; browser/cihaz kabulü kullanıcıda.

## Dilim ve kabul ölçütleri

- SUB-01 / CAT-10: ffprobe ile gerçek ses/video/altyazı izleri, dil/default/forced/HI bilgileri ve chapter envanteri. Dosya fingerprint'i değiştiğinde önbellek yenilenir.
- SUB-05/06: Aynı klasördeki yerel SRT/VTT/ASS altyazıları salt okunur; istemcide kullanılacak WebVTT uygulamanın cache'ine çıkarılır. Kullanıcının yüklediği yan altyazı yalnız yönetilen uygulama alanına yazılır.
- PLY-02/03/06: Tarayıcı codec/container yeteneklerine göre direct, remux, ses dönüştürme veya yazılımsal SDR video dönüştürme. Ses seçimi, seek ve oturum cleanup gerçek FFmpeg çıktısıyla doğrulanır.
- PLY-07 / CLI-01: Aynı video elementiyle mini/tam görünüm, hız, ses, altyazı ve chapter seçimi; konum dönüşümü ve iptal edilebilir seek.

## İş ve dosya kuralları

Oynatma yalnız katalog ID'sinden çözülen dosyayı okur. İstemci FFmpeg argümanı, filtre veya dosya yolu gönderemez. İçerik/muxer/protokol seçimleri sunucudaki allowlist'lerden gelir.

HLS işleri sınırlı sayıdadır; session başına ve toplam cache bütçesi vardır. Başlangıç/seek için özgün zaman ile session zamanı ayrı tutulur. Durma, timeout, hata, server kapanışı ve sonraki açılış cache toparlanması açık kod yollarıdır. Session dosyaları sahibine bağlı API üzerinden sunulur.

Kaynak medya ve yan altyazı dosyaları değiştirilmez. Cache silme yalnız Home'un oluşturduğu iş dizinlerinde ve bilinen kimliklerde yapılır.

HDR tone mapping, PGS/VobSub burn-in, GPU ve cihazlarda görsel renk doğruluğu bu dilim için ayrı açık kapılardır; test edilmeden destek etiketi verilmez.

## Uygulanan davranış

- `005_media_playback.sql`: dosya fingerprint'ine bağlı kalıcı probe envanteri, yönetilen altyazı kayıtları ve sıralı oynatma konumu olayları. Geç gelen eski konum aynı istemcinin daha yeni seek'ini geri alamaz.
- `MediaAnalysisService` aynı dosyaya gelen paralel analizleri birleştirir. İzler/chapter'lar gerçek ffprobe çıktısıdır. ffprobe bulunmadığında özgün dosyada doğrudan oynatma denemesi korunur; iz bilgisi uydurulmaz.
- `SubtitleService`: gömülü metin izleri, aynı kökteki SRT/VTT/ASS/SSA yan dosyalar, uygulama alanına yükleme/kaldırma, UTF-8/UTF-16 okuma ve WebVTT zaman kaydırması. Kaynak yan dosyaları yazılmaz/silinmez. Metin dönüşümünde ASS'nin bütün görsel stilleri korunmaz.
- `PlaybackPlanner`: istemci codec/kapsayıcı bilgisi, ses izi, çözünürlük ve bit hızı tercihlerinden açıklanabilir karar üretir. Direct, HLS remux, AAC stereo ses dönüşümü, H.264 yazılımsal SDR video dönüşümü uygulanmıştır.
- `PlaybackManager`: varsayılan iki HLS oturumu, oturum başına 8 GiB cache bütçesi, boş alan rezervasyonu ve iki saniyelik boyut gözetimi. Limit aşımı gerçek hata olarak gösterilir. `Home:Playback:MaximumSessions` ve `CacheGiBPerSession` ile yapılandırılır.
- Stream-copy seek için önceki keyframe bulunur; özgün zaman, başlangıç keyframe'i ve istemcinin istediği konum ayrı taşınır. Video dönüşümünde yeni seek pozisyonundan iş oluşturulur.
- İstemci kimliği/revizyonu eski başlangıç/kapatma komutlarının yeni akışı bozmasını engeller. Cevabı kaybolan aynı istemci oturumu yeni komutta temizlenir. İki dakika hareketsiz kalan oturumlar kapatılır.
- Windows'ta medya süreçleri `KILL_ON_JOB_CLOSE` job object'e alınır. Normal iptal/kapatmada süreç ağacı sonlandırılır; yeniden açılışta kayıtlı PID + başlangıç zamanı + executable birlikte kontrol edilir. Yalnız Home işaretli cache dizinleri temizlenir; yabancı klasörler korunur.
- Aynı video elementi mini/tam görünümde tutulur. Ses/altyazı/kalite, hız, ses seviyesi, sahne seçimi, altyazı gecikmesi/yükleme ve uygun tarayıcıda PiP kontrolleri eklendi. hls.js yalnız oynatma açıldığında yüklenir.

### 0.4.1 edinme düzeltmesi

`006_import_review.sql` ile doğrulamada duran, yüzde 100 inmiş payload için açık kullanıcı **kalite profiliyle yeniden doğrula** komutu eklendi. Yeni import profil snapshot'ı ayrı tutulur; ilk release/profil kanıtı değiştirilmez. Komut yeni revizyon üretir. Yanlış yapım/bölüm kontrollerini kaldırmaz. Yayımlanmış dosyada uygulanamaz.

## API

Tüm yollar `/api` altında kimlik doğrulamalıdır; mutasyonlar CSRF gerektirir.

| Yöntem | Yol | İşlev |
|---|---|---|
| GET | `/media/{id}/tracks` | Gerçek iz/chapter/altyazı envanteri |
| GET | `/media/{id}/subtitles/{track}?start=0&delay=0` | İstemci zamanına göre WebVTT |
| POST | `/media/{id}/subtitles?language=tr&title=...&extension=srt` | En fazla 2 MiB ham altyazı; yönetilen alana yazılır |
| DELETE | `/media/{id}/subtitles/managed/{subtitleId}` | Yalnız yüklenmiş uygulama altyazısını kaldır |
| POST | `/playback` | `mediaId`, `client`, `startSeconds`, ses/kalite ve istemci revizyonuyla oynatma başlat |
| GET | `/playback/{id}` | Oturum durumu |
| POST | `/playback/{id}/heartbeat` | Konum ve oturum canlılığı |
| GET | `/playback/{id}/{file}` | Sahibe bağlı manifest/segment; HTTP Range |
| DELETE | `/playback/{id}` | İş/oturum/cache kapat |
| POST | `/playback/clients/{clientId}/close` | `{revision}` ve önceki istemci oturumlarını kapat |
| POST | `/downloads/{id}/import-profile` | `{qualityProfileId}` ile kullanıcı kaynaklı tekrar doğrulama |

`PUT /media/{id}/progress` ayrıca `{clientId,sequence}` kabul eder; eski istemciler için iki önceki alan da geçerlidir.

## Doğrulama — 0.4.1

- **77/77 backend test geçti, 0 atlanan.** Native kontrollerde `HOME_TEST_FFMPEG` ve `HOME_TEST_FFPROBE` kurulu 9.0.2 araçlarına verildi.
- Gerçek FFmpeg çıktısı: MKV H.264/AAC → HLS remux; H.264/AC3 → H.264/AAC stereo; MPEG-4 Part 2 → H.264; uyumluluk modunda yeniden kodlama; 1280×720 → 854×480 ölçekleme.
- Türkçe SRT yan dosyası ve gömülü SRT → WebVTT; kullanıcı altyazısı ekleme/kaldırma; kaynak dosyanın SHA-256 değerinin korunması.
- Native işlem iptali; slot sınırı; kaybolan cevap sonrası istemci oturumunun değiştirilmesi; geç kalan close/start/progress reddi; bilinen cache toparlanması ve yabancı klasörün korunması.
- Kimliksiz iz/segment erişimi ve CSRF'siz başlangıç reddi; HLS segmentinde gerçek HTTP 206; kapatılan oturumda 410.
- Web/PWA üretim build ve Windows x64 self-contained yayın başarılı. hls.js ayrı, isteğe bağlı chunk'tır (yaklaşık 179 kB gzip); Vite'ın 500 kB ham chunk uyarısı bu modülden gelir.
- Canlı 0.4.1 sağlık/oturum, FFmpeg/ffprobe durumu, PWA manifest/worker/PNG erişimi ve TMDB Başlangıç (27205) okuması başarılı. `artifacts/live-runtime-v041.json`.
- 0.3 → 0.4 yedeği `.data/backups/preview-v03-20261012-232457`; 0.4 → 0.4.1 yedeği `.data/backups/preview-v04-20261012-233123`. DB/WAL ve Data Protection anahtarları birlikte tutulur.

## Açık kabul

Browser ve fiziksel cihazlarda oynatma, ses geçişi, seek, altyazı senkronu, PiP ve PWA kurulumu kullanıcı tarafından henüz kabul edilmedi. Native test videoları birkaç saniyelik izole codec fixture'larıdır; normal katalogda yer almazlar. Uzun süreli/4K performans ve HDR/GPU uyumluluğu bu kanıtlarla varsayılmaz. Altyazı sağlayıcısı hesabı/otomatik arama bu dilimde eklenmedi.

## Kaynaklar

- https://ffmpeg.org/ffmpeg-formats.html — HLS muxer, `temp_file`, event/VOD, segment adlandırma ve timestamp davranışı; kurulu 9.0.2 `-h muxer=hls` çıktısı okundu.
- https://ffmpeg.org/ffprobe.html — stream/chapter/format envanteri.
- https://raw.githubusercontent.com/video-dev/hls.js/master/docs/API.md — MSE/native HLS seçimi, media attach/destroy, buffer sınırları ve hata yaşam döngüsü.
- hls.js 1.7.3 npm metadata: Apache-2.0; benimsemeden önce `dependencies.md` içine kaydedildi. Kullanılacak API'ler kurulu tiplerden de kontrol edilecek.

Backend testleri izole, birkaç saniyelik üretilmiş codec/altyazı fixture'ları kullanabilir. Ürün kataloğuna test medyası eklenmez. Browser/cihaz kabulü kullanıcıdadır.

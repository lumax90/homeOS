# Chromium HLS oynatma düzeltmesi — 0.27.2-dev

Tarih: **2026-10-18**. Kart **PLAY-02 / PLY-02/03, CLI-01, EXT-01**. Şema **39**, backend HLS üretim sözleşmesi değişmedi.

## Teşhis

Kullanıcı, orijinal ve uyumlu 1080p/720p seçimlerinde birkaç saniye sonra aynı oynatma hatasını bildirdi. Mevcut kullanıcı oturumu üzerinde yalnız okuma amaçlı console/network/video durum incelemesi yapıldı; browser suite veya yeni oynatma komutu çalıştırılmadı.

- İndirilen dosya kütüphaneye yayımlanmış: HEVC **Main 10 / yuv420p10le**, AAC **6 kanal**, MP4, 1920×804. İndirme %100; artık paylaşım aşamasında. Dosya yeniden indirilmedi.
- Sunucu NVENC ile **H.264 High / yuv420p / level 4.0**, AAC stereo ve 4 saniyelik HLS `.ts` parçaları üretiyor. İlk mevcut parça ffprobe ile okundu. Backend logunda bu kullanıcı akışına ait FFmpeg çökme kaydı bulunmadı.
- Etkilenen tarayıcı **Electron 44.4.5 / Chromium 152**. `<video>.canPlayType('application/vnd.apple.mpegurl')` **`maybe`** dönüyor; video hatası **code 4 / `PipelineStatus::DEMUXER_ERROR_COULD_NOT_PARSE`**. Kaydedilmiş playlist/parça HTTP istekleri 206 ile tamamlanmış. Native oynatma `.m3u8` URL'sini doğrudan video kaynağı olarak kullanıyor.
- `usePlayback.ts`, `canPlayType` sonucu boş olmadığı için hls.js'i atlıyordu. Bu ipucu bütün HLS biçimlerinin çalıştığı anlamına gelmez. Aynı karar Live TV oynatıcısında da vardı.

## Düzeltme

- `web/src/playbackTransport.ts` tek karar noktası: özgün direct dosyalar korunur; HLS için **Hls.isSupported() → hls.js/MSE**, MSE olmayan native-HLS cihazlarında native fallback; hiçbiri yoksa açık destek hatası.
- Film/dizi ve Live TV oynatıcıları aynı seçimi kullanır. AirPlay alıcısının native oynatması değişmedi; orijinal kaynak dosyası/kayıtlı konumlar değişmez.
- hls.js bağlıyken DOM video hata olayı, hls.js'in mevcut tek denemeli kurtarmasıyla yarışıp ekranda kalıcı eski hata oluşturmaz. Fatal hls.js akış/net hata denetimi, sunucu durum okuması ve generation koruması korunur.

## Doğrulama ve sınırlar

- **8/8 Node**: Chromium `maybe`/`probably` ve MSE seçimi; direct/native-only/unsupported yolları; iki gerçek controller'ın ortak seçici/kurtarma sahibiyle bağlanması; beş önceki yerelleştirme kontrolü. Browser otomasyonu yok.
- **17/17 backend/native, 0 atlanan**: HLS remux/ses/video dönüşümü, gerçek codec/çözünürlük, HTTP segment range/yetki, iptal, stale-komut/slot/cache ve lifecycle kontrolleri. `artifacts/test-results/playback-hls-v0272.trx`.
- Frontend/worker tip kontrolü ve üretim derlemesi geçti; npm audit 0 açık. İstemci `/assets/index-D6oXmH4G.js`, PWA build `846db06ce716846a`.
- Güncelleme, devam eden payload veya aktif oynatma varken uygulanmaz. Yalnız %100/import-doğrulanmış, indirme hızı 0 olan paylaşımın doğal restart recovery'sine izin verilir; bu admission için beş izole PowerShell kontrolü geçti. Sürüm yükleme sonrası oynatma/transferler tekrar denetlenir. Pause/resume/edinme komutu gönderilmez.
- Kullanıcı tarayıcısında 1080p/720p devamlı oynatma ve seek kabulü yeniden denenmelidir. Bu kayıt, düzeltme sonrası gerçek browser oynatmasının geçtiği iddiası değildir; **VERIFY-27 / ACCEPT-04** açık.

## Önizleme yayını

2026-10-18 **17:25** yerel saatte imzalı **0.27.1 → 0.27.2** güncellemesi tamamlandı; sahip oturumu korundu. PID **12652**, release `e580ce85f057484eb8d0a1316c40e89c`, veri `.data/preview`. HTTP kontrolü yeni MSE-first istemcinin ve PWA worker'ın sunulduğunu doğruladı; şema 39, FK ihlali 0. Tamamlanan indirme/medya kimliği/sahibi ve kütüphane dosyası korundu; seed oturumu doğal restart doğrulamasından devam ediyor. Yeni oynatma veya edinme komutu gönderilmedi.

Kanıtlar: `artifacts/update-live-0.27.2-preview.json`, `artifacts/live-hls-v0272.json`. Windows paket `artifacts/packages/preview-v0272/home-0.27.2-dev.1-win-x64.zip`, SHA-256 `10fef23c7c07d0b3dbdf868017355dbfe6ab720cad76b878b0dfae76d59794f4`. Geri dönüş **0.27.1**, yedek `4614302d15f44abdbfdd5b282bfb608a`; önceki release/yedekler silinmedi. Kullanıcı **Ctrl+F5** sonrası aynı dosyanın uyumlu oynatmasını dener.

## Kaynak

2026-10-18 Context7 üzerinden güncel [hls.js README](https://github.com/video-dev/hls.js/blob/master/README.md), [basic usage](https://github.com/video-dev/hls.js/blob/master/demo/basic-usage.html) ve [fatal media recovery API](https://github.com/video-dev/hls.js/blob/master/docs/API.md) okundu. Resmi README Chromium'un `maybe` bildirip bazı akışlarda başarısız olabildiğini belirtir ve MSE-first yaklaşımını önerir. Kurulu paket **hls.js 1.7.3**; bağımlılık/lisans değişmedi.

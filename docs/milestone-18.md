# 0.18 — Altyazı sürümleri, görüntüye işleme ve yerel VAD senkronu

Tarih: **2026-10-14**, **0.18.0 yayımlandı**. Gereksinimler: **SUB-02/04/05/06/07, PLY-05/06, OPS-01/03**.

## Medya tercihi ve altyazı yükseltmesi

- Genel dil profili yanında dosya bazlı profil: diller, forced/HI, ses-dili dışlaması, minimum puan, cutoff, gerekli puan artışı ve arama aralığı. Genel `Enabled` anahtarı bütün otomasyon için üst sınırdır; dosya profili açıkça seçilmiş indirme/yükseltme tercihi taşıyabilir. Dosya günlük bütçesi genel bütçeyi aşamaz.
- Global profil, medya override ve sağlayıcı bağlantısı ayrı monoton revizyonlardır. Override kaldırılması revizyonu sıfırlamaz. Kalıcı iş claim'i lease token'ıyla tutulur; değişen profil/bağlantı eski sonucun yayımlanmasını engeller.
- Mevcut sağlayıcı altyazısı cutoff altındaysa daha yüksek puanlı farklı `file_id` aranabilir. Kaynak klasöründeki veya kullanıcının elle yüklediği altyazılar otomatik değiştirilmez. Resim altyazısı oynatılabilir burn-in profiline sahipse dil kapsamını karşılayabilir.
- Yayın, kaynak medya fingerprint'i/ilişki revizyonu ve supersedes koşulunu aynı DB yazma kilidinde doğrular. Yeni altyazı doğrulandıktan sonra önceki sürüm atomik olarak geçmişe alınır. Önceki dosya byte'ları korunur.
- Sürüm geçmişinden geri alma, aktif sürümün yerine eski sürümü getirir; o dosyada otomatik yükseltme duraklatılır. Kullanıcı genel tercihe dönerek veya özel profili yeniden kaydederek bunu kaldırabilir.
- Sınırlar: medya başına 16 etkin/128 toplam yönetilen altyazı; 20.000 arama kaydı. Kaynak medya ve yan altyazı dosyaları salt okunurdur; kullanıcı yalnız Home'un yönettiği sürümleri silebilir.

## Sağlayıcı izni, kota ve tekrar

OpenSubtitles akışı iki aşamalıdır: `POST /download` ile geçici dosya bağlantısı, sonra bu bağlantıya kimlik bilgileri taşımayan GET. Geçici bağlantı Data Protection ile şifrelenerek saklanır; Home'un yeniden kullanım penceresi 10 dakikadır. Dosya aktarımı kesilir ve bilet geçerliyse tekrar `POST /download` yapılmaz.

Otomatik edinme bütçesi sağlayıcı izin isteğinden önce kalıcı rezerve edilir. Belirsiz ağ hatasında harcanmış olabilecek sağlayıcı hakkı yok sayılmaz. Bağlantı değişirken gelen eski kota/oturum yanıtı yeni bağlantıyı ezemez. Manuel ve otomatik akışlar aynı yayın doğrulamasını kullanır; gerçek hesap kotası ayrıca sağlayıcı tarafından uygulanır. Canlı OpenSubtitles hesabı/transfer kabulü açıktır.

## PGS/DVD ve ASS görüntüye işleme

- Gömülü **PGS** (`hdmv_pgs_subtitle`) ve **DVD/VobSub** (`dvd_subtitle`) resim izleri, seçilince H.264/HLS görüntüye işleme kararını tetikler. Altyazı iz kimliği sunucunun gerçek envanterinden çözülür.
- Bitmap overlay, özgün video boyutunda uygulanıp çıkış boyutuna ölçeklenir. Altyazı gecikmesi timestamp dönüşümüdür. Kaynak dizin/dosya adları filtergraph metnine eklenmez.
- Metin altyazısı normalde WebVTT ile oynar. Kullanıcı isterse ASS stilini görüntüye işler; güvenli çalışma dizinindeki `captions.ass` kullanılır. Renk/konum ve seek sonrası özgün cue zamanı native testle doğrulandı.
- Altyazıyı kapatmak veya tür değiştirmek mevcut direct/remux/transcode kararını yeniden değerlendirir; medya konumu korunur. Yeni caption istemci komutları aynı playback client-revision sınırını kullanır.
- Mevcut HDR/Dolby Vision ve codec karar sınırları uygulanır; desteklenmeyen dönüşüm başlatılmaz. Doğrulanan resim profili PGS/DVD + SDR yazılım encode'dur. HDR/bitmap/GPU birleşik cihaz matrisi ve diğer resim kodekleri ayrıca kabul gerektirir. Decode/overlay CPU'dadır.

FFmpeg'in bitmap-subtitle → video köprüsü upstream belgede geçici/experimental olarak nitelenmektedir; bu sürümde kurulu **FFmpeg 9.0.2** ile gerçek çıktı, delay ve seek doğrulanmıştır.

## Yerel konuşma etkinliğiyle senkron

- **Silero VAD v6.2.3**, 16 kHz ONNX modeli, **ONNX Runtime 1.31.0 CPU**. Model, `5cd7945676eb32225748052e2e6a0580e4686a08` revizyonundan alınan `silero_vad_16k_op15.onnx` dosyasıdır. SHA-256: `7ed98ddbad84ccac4cd0aeb3099049280713df825c610a8ed34543318f1b2c49`. Yüklemede hash denetlenir; ONNX telemetrisi kapalıdır.
- FFmpeg seçilen ses izini 16 kHz mono PCM olarak pipe üzerinden çözer. 512 örnek + 64 örnek bağlam ve recurrent state ile ses etkinliği hesaplanır. Ses dışarı gönderilmez; başka medya yöneticisi veya inference servisi çalıştırılmaz.
- Konuşma etkinliğiyle metin cue maskeleri 100 ms ızgarada karşılaştırılır. ±1–120 saniye offset ve istenirse 23,976/24/25 fps oranları denenir. Bu bir ses/zaman örtüşme puanıdır; olasılık veya konuşma-metni doğruluğu iddiası değildir.
- En az üç cue, 2,5 saniye konuşma, 0,50 örtüşme ve ikinci anlamlı offset'e 0,025 fark gerekir. Sınırdaki/belirsiz sonuç otomatik uygulanmaz. Kullanıcı öneriyi uygular veya açık `AutoApply` seçimini kullanır.
- Sonuç yeni bir **WebVTT** sürümüdür; eski altyazı korunur. Sync işi, kaynak altyazı fingerprint'i, medya fingerprint'i ve ilişki revizyonuna bağlıdır. İptal edilen/eski iş commit yapamaz; managed yayın ile sync tamamlanması aynı DB işlemi içindedir.
- Kalıcı kuyruk: 100 etkin iş, tek işleyici; medya en fazla dört saat, iş bütçesi 20 dakika, 50.000 cue sınırı. Çözülen sesin tamamı RAM/diskte tutulmaz; frame olasılıkları ve bit maskeleri tutulur. Restart'ta kesilen analiz yeniden başlar, yarım yayın idempotent toparlanır.

Bu katman **VAD zaman eşleme** yapar. ASR ile diyalog/altyazı üretimi, konuşmacı ayrımı ve parça parça doğrusal olmayan retiming henüz uygulanmadı; kapsam panosunda açık tutulur. Synced WebVTT, ASS'nin görsel stilini taşıdığı iddiasıyla sunulmaz; özgün ASS sürümü korunur.

## Şema ve API

Şema **20 → 21 → 22**: `021_subtitle_versions.sql`, `022_subtitle_sync.sql`. Önceki yayımlanmış migration'lar değiştirilmedi. Yedekleme bütün yönetilen altyazı sürümlerini kapsar; model uygulama paketinin parçasıdır.

- `/api/subtitles/media/{id}/policy`, `/refresh`, `/versions`, `/versions/{version}/restore`.
- `/api/subtitles/sync/capabilities`, `/sync/jobs`, `/media/{id}/sync`, `/sync/jobs/{id}/apply`, `/cancel`.
- Playback başlangıcında `subtitleId`, `burnSubtitle`, `subtitleDelaySeconds`; session yanıtında seçilen/burned iz bilgisi.
- `SubtitleCoordinator`, `MediaStore.SubtitlePolicies`, `SubtitleSyncService`, `SileroVad`, `SubtitleAligner`, `SubtitleTiming`; frontend `SubtitleTools.tsx` ve oynatıcı bağlantıları.

## Doğrulama

- **197/197 backend/native test, 0 atlanan**, `artifacts/test-results/home-0.18.0-full.trx`. FFmpeg, ffprobe, PAR2, NVENC ve yeni self-contained sunucuyla HTTPS dahil. Frontend üretim build ve **5/5 saf ses zamanlayıcı testi** geçti.
- PGS/DVD için izolasyonda üretilen bitmap display-set, gerçek FFmpeg mux/decode/overlay/HLS; görünür piksel, clear frame, +1 sn delay, cue içinden seek ve session cleanup doğrulandı.
- ASS'nin kırmızı yazı/raster konumu ve seek sonrası aktif cue, gerçek çıktı pikseliyle denetlendi.
- Windows'un kurulu offline SAPI sesiyle izole konuşma fixture'ı üretildi. Gerçek Silero/ONNX inference, offset önerisi, WebVTT yayını, idempotent apply ve kaynak hash'lerinin korunması geçti. Bu fixture Windows/SAPI gerektirir; diğer işletim sistemlerinde çalıştırıldığı iddia edilmez.
- Yükseltme/retire/restore, cutoff, kaynak ve elle yüklenmiş altyazı korunumu, bağlantı/override iptali, eski lease, ortak dil işi bütçesi ve başarısız GET sonrasında şifreli biletle tek sağlayıcı izni doğrulandı.
- Browser/cihaz otomasyonu çalıştırılmadı; kullanıcı kabulü açıktır.
- Windows x64/Linux x64/Linux ARM64 imzalı paketleri, pinned model ve lisanslarla üretildi; Linux çalıştırma kabulü açık. İzole gerçek süreçte **0.17.0/şema 20 → 0.18.0/şema 22 → eski sürüm/veri** geçti; restore oturumları iptal etti (`artifacts/update-roundtrip-0.17.0-0.18.0.json`).
- Canlı önizleme 0.18.0, şema 22; yerel VAD CPU başlangıcı başarılı. OpenSubtitles henüz bağlı değil; dil otomasyonu kapalı. API/DB'de 0 medya indirmesi, 0 sağlayıcı izin denemesi, 0 sync işi ve 0 etkin sentetik kütüphane var (`artifacts/live-runtime-v018.json`). Sunucu başlangıçta kapalıydı; kayıtlı 0.17 sürümüyle yeniden açılıp imzalı güncelleme uygulandı. Kaynak medya indirmesi/analizi doğrulama için başlatılmadı.

## Kaynaklar — 2026-10-14

1. Context7 `/websites/ffmpeg_documentation`: bitmap sub2video/overlay, `setpts`, ASS/subtitles; kurulu FFmpeg tipi ve native çıktı.
2. [ONNX C# OrtValue API](https://onnxruntime.ai/docs/tutorials/csharp/basic_csharp.html), Context7 `/websites/onnxruntime_ai`; kurulu 1.31.0 tipleri. [NuGet listed sürümü](https://api.nuget.org/v3/registration5-gz-semver2/microsoft.ml.onnxruntime/1.31.0.json), kaynak `402979ac1bef89dcd55add41eaa7277c407599eb`.
3. [Silero pinned kaynak/model](https://github.com/snakers4/silero-vad/tree/5cd7945676eb32225748052e2e6a0580e4686a08), `utils_vad.py` frame/context/state sözleşmesi ve MIT lisansı. Model/ORT lisansları ve ThirdPartyNotices paketle taşınır.
4. [OpenSubtitles Getting Started](https://opensubtitles.stoplight.io/docs/opensubtitles-api/e3750fd63a100-getting-started), [2026 resmi API/file-rate açıklaması](https://forum.opensubtitles.com/t/download-rate-limit/6415). Stoplight kök yolu 404, detay sayfası JS kabuğu verdi; iki aşamalı link/GET ayrımı resmi forum ve mevcut OpenAPI sözleşmesiyle karşılaştırıldı. Dosya-sunucu limitleri API limitlerinden ayrıdır.

## Kullanıcı kabulü — ACCEPT-18

1. Gerçek bir videoda **Bu dosyanın altyazı tercihleri** bölümünden dil/forced/HI ve yükseltme politikasını kaydet; genel tercihe dönmeyi kontrol et.
2. Sağlayıcı bağlı olduğunda daha iyi sürüm ekle, önceki sürümü geçmişten geri al; eski sürüm ve yükseltme duraklatması görünmeli.
3. PGS/DVD izi seç; görüntüdeki altyazıyı, hızlı seek ve gecikmeyi dene. ASS stilini görüntüye işleme seçeneğini kontrol et.
4. Metin altyazısında **Sesle altyazı zamanını eşleştir** ile doğru ses izini seç; öneriyi incele/uygula, sonuç sürümünü dinleyerek/izleyerek değerlendir.

Gerçek kaynak/hesap ve cihaz kabulü, Linux/ARM64 çalıştırma, kamu dağıtım lisansı/imza kimliği ile kalan Home kapsamı açık kapılardır.

# 0.16 — Sanatçı takibi ve gelişmiş müzik

Tarih: **2026-10-13**, **0.16.0 yayımlandı**. Gereksinimler: **MON-03, MUS-04/05/06/08, ACQ-02, QUA-02/04, IMP-02/03, OPS-01**.

## Takip ve edinme

- Sanatçı veya albüm için kişisel, revizyonlu takip. Metadata yenileme, kaynak araması ve otomatik edinme üç ayrı açık tercihtir; başlangıçta kapalıdır. Kaynak araması/edinme yöneticilere aittir.
- Birincil/ikincil yayın türleri, geçmiş/gelecek kapsamı, baskı durumu, ülke ve biçim profili. Albüm bazlı dahil et/hariç tut istisnaları yenilemede korunur. Eksik/başarısız sağlayıcı sayfası önceki albümleri silmez.
- MusicBrainz sayfaları dönen kayıt sayısıyla ilerler; cursor kalıcıdır. Kesilen arama işleri toparlanır. Kural ve hesap revizyonu ile lease, gerçek transfer komutunun aynı DB yazma kilidinde yeniden doğrulanır.
- Lossy → lossless → yüksek örnekleme/bit derinliği kararları gerçek yerel probe verisini kullanır. Kayıpsız yükseltmede bit derinliği veya örnekleme hızından biri düşürülemez. Çevrimdışı/belirsiz yerel dosyalar otomatik yeniden edinmeyi bekletir.
- Cutoff, aday gecikmesi, hesabın bugünkü edinimlerini sayan kural bütçesi, çifte-albüm engeli, bozuk sürüm blocklist'i ve kullanıcı iptalinden sonra bastırma. Aktif bir transfer yeni niyete rağmen yeniden başlatılmaz.
- Otomatik transferde ilan edilen codec/bitrate/bit-depth/sample-rate import profiline sıkılaştırılır; mevcut kalite tabanı da kaydedilip gerçek dosyayla karşılaştırılır. İçe aktarılmış dosyalar ve önceki iyi sürümler korunur.

Sınırlar: hesap başına 100 takip, takip başına 2000 albüm, 2000 etkin otomasyon işi; metadata yenileme 6–168 saat, kaynak araması en az 30 dakika. Eksik tarih hassasiyetinde yayınlanmış sayılmak için bilinen yıl/ayın sonu beklenir. Public MusicBrainz çağrıları ortak 1,1 saniye aralığı/backoff ile yapılır.

## Kesintisiz müzik ve ses analizi

- FFmpeg tam ses dosyasını sınırlı, yerel **48 kHz stereo float32 PCM** önbelleğine çözer. Parça sınırı gerçek çözülen örnek sayısından gelir; müzikal sessizlik kesilmez ve crossfade uygulanmaz.
- İstemci 6 saniyelik aralıkları aynı Web Audio saatine, birbirinin tam son örneğinden sonra planlar. İleri hazırlık yaklaşık 24–30 saniyeyle sınırlıdır. Seek/sonraki parça/oynatma hızı yeni kuşak başlatır; geç gelen önceki PCM yanıtı ses kuyruğuna giremez.
- PCM hazırlama ve her aralık erişimi medya/hane izinlerinden geçer. Kaynak fingerprint değişirse eski PCM reddedilir. Önceki/sonraki, shuffle/repeat, mini/tam görünüm, favori, kişisel dinleme konumu, Media Session eylemleri ve sözler yeni müzik oynatıcısına bağlıdır.
- ReplayGain parça/albüm etiketi, ön kazanç ve peak ile taşma önleme. Etiket yoksa yerel EBU R128 loudness ölçümü kullanılır; peak bilinmiyorsa korumalı mod pozitif yükseltme yapmaz.
- Sonic analiz açık yönetici komutuyla, kalıcı/iptal edilebilir yerel işte yürür. FFmpeg `loudnorm` ile LUFS/true-peak, Home FFT ile 16 logaritmik enerji bandı, spektral ağırlık merkezi ve sıfır geçişi ölçülür. Favori/son dinlenen tohumlara göre yalnız erişilebilir yerel dosyalar önerilir. Bu deterministik ses rengi benzerliğidir; anlamsal/öğrenilmiş müzik modeli iddiası yoktur.

Sınırlar: PCM için parça başına **30 dakika**, iki eşzamanlı hazırlayıcı ve **4 GiB** tamamlanmış cache; aralık yanıtı en fazla 10 saniye. Uzun dosya veya Web Audio desteği olmayan cihazda özgün oynatıcı kullanılabilir. Ağ yetişmediğinde yeniden tamponlama açık gösterilir. 48 kHz uyarlaması bit-perfect/hi-res çıkış iddiası değildir. Gerçek browser/cihazda kesintisiz dinleme kabulü kullanıcıya aittir.

## Yönetilen etiket yazımı

- Yalnız `downloads/music-library` içindeki **FLAC, MP3, M4A** ve açık MusicBrainz eşleşmesi. Salt-okunur kaynak kökleri reddedilir.
- Önizleme/fingerprint → hazırlanmış bağımsız kopya → codec-copy ses payload SHA-256 ve yazılan etiket/kapak kontrolü → original SHA-256/yedek → kalıcı yayın günlüğü → dosya girdisi değişimi → aynı medya ID'siyle DB güncellemesi.
- Hardlink'in byte'ları yerinde değiştirilmez; inode/dosya girdisi değiştirilir. Dosya adı çakışması ve hazırlık sırasında dış değişiklikte mevcut dosyaya yazılmaz. Yayın aşaması tarama claim'lerini engeller; süreç kesilirse aynı hash günlüğü idempotent tamamlanır.
- Disc/parça/başlık adlandırma, isteğe bağlı CAA kapağı, birebir eski dosyayı geri alma. Kuyruk/liste/dinleme kimliği aynı kalır. Import manifestinin özgün provenance'ı korunur; tekrar import/link işlemi doğrulanmış etiket günlüğü üzerinden güncel dosyayı bulur.
- Geri alma kopyaları `downloads/imports/music-tags/{job}/` alanında en fazla **20 GiB** tutulur. Kullanıcı terminal işin kopyasını temizleyebilir; aktif geri alma işi kullandığı kopyanın silinmesini engeller. Kütüphane/staging kaynakları bu temizlikte silinmez. Uygulama yedekleri medya payload'ları gibi bu büyük geri alma kopyalarını içermez.
- Native kontrolde FFmpeg'in M4A `use_metadata_tags` seçeneği ile `covr` kapağının birlikte korunmadığı görüldü. Çözüm: FFmpeg klasik iTunes kapağını yazar; **TagLibSharp 2.3.0** yalnız geçici M4A kopyasına freeform MusicBrainz kimliklerini ekler. Ardından FFprobe/elementary ses hash'i tekrar doğrulanır. LGPL-2.1-only lisansı ve kaynak referansı paketle taşınır.

## Şema, API ve dosyalar

Şema **17 → 18 → 19**: `018_music_automation.sql`, `019_music_audio.sql`. Yayınlanmış önceki migration'lar değiştirilmedi.

- `/api/music/monitors`, `/monitors/{kind}/{mbid}`, `/monitors/{id}/albums`, `/refresh`, `/automation/jobs`, `/automation/blocked`.
- `/api/music/media/{id}/prepare`, `/pcm`, `/favorite`, `/analysis`, `/analyze`, `/similar`; `/music/playback-preferences`, `/music/recommendations`.
- `/api/music/media/{id}/tags/preview`, `/tags`; `/api/music/tasks`, `/{id}/cancel`, `/{id}/restore-tags`, `DELETE /{id}/tag-history`.
- Backend: `Music/`, `Infrastructure/MediaStore.MusicAutomation.cs`, `MediaStore.MusicAudio.cs`, `MediaStore.MusicTags.cs`.
- İstemci: `web/src/audio/`, `MusicPlayer.tsx`, `MusicAutomation.tsx`, `MusicTools.tsx`.

## Kaynaklar ve doğrulama

2026-10-13 kaynak kontrolü:

1. MusicBrainz resmi [API](https://musicbrainz.org/doc/MusicBrainz_API): browse/offset, type/status ve rate limiting; metadata yalnız core data.
2. Context7 `/llmstxt/heroui_react_llms_txt`, kurulu HeroUI 3.2.6 `switch.d.ts`: v3 compound Switch/Label.
3. Context7 `/websites/ffmpeg_documentation`: sample sayma, stream copy, `loudnorm`, attached-pic ve MOV `use_metadata_tags`.
4. MDN [AudioBufferSourceNode.start](https://developer.mozilla.org/en-US/docs/Web/API/AudioBufferSourceNode/start): ortak AudioContext saatinde örnek buffer planlama.
5. TagLibSharp [NuGet manifesti](https://api.nuget.org/v3-flatcontainer/taglibsharp/2.3.0/taglibsharp.nuspec), kurulu `netstandard2.0/TaglibSharp.xml`, [2.3.0.0 kaynağı](https://github.com/mono/taglib-sharp/tree/b5ae84f2e84087bf160bb0471420200dd2b5d809). Context7 sonucu başka bir fork'a aitti; upstream belge/kurulu tipler esas alındı.

### Doğrulama kaydı

- Native müzik kontrolleri: exact PCM frame/aralık, ReplayGain, LUFS/spektrum, kişisel öneri/ACL; FLAC/MP3/M4A etiket+kapak, hardlink kaynağı korunumu ve birebir undo; yayın ortası süreç kesintisi ve yeniden import/link.
- Saf ses-zamanlayıcı kontrolleri: **5/5**, Node `node:test`; gerçek tarayıcı kullanılmadan sınırda kesintisiz zamanlama, geç yanıt/seek, pause/repeat, yeniden başlama ve clipping hesabı doğrulandı.
- **165/165 backend/native test, 0 atlanan**: `artifacts/test-results/home-0.16.0-full.trx`. FFmpeg, ffprobe, PAR2, NVENC ve yeni self-contained sunucu üzerinden HTTPS testleri dahil. Frontend üretim build geçti.
- Windows x64, Linux x64, Linux ARM64 paketleri aynı güvenilen RSA-PSS anahtarıyla üretildi; `artifacts/packages/home-0.16.0-{win-x64,linux-x64,linux-arm64}.zip`. Linux/ARM64 çalıştırma kabulü henüz yapılmadı.
- İzole gerçek süreçte **0.15.0/şema 17 → 0.16.0/şema 19 → eski sürüm + eski hesap/veri** geçti; restore oturumları iptal etti. Kanıt: `artifacts/update-roundtrip-0.15.0-0.16.0.json`.
- Canlı Home `http://localhost:5080` imzalı uygulama içi güncellemeyle 0.16.0'a geçti. Sahip oturumu korundu. DB read-only sorgusu şema 19; API/DB **0 indirme, 0 müzik otomasyonu, 0 müzik işi**, etkin sentetik kütüphane 0.
- Canlı MusicBrainz **Radiohead** `a74b1b7f-71a5-4011-9441-d0b5e4122711`: **107 release-group**, ilk sayfa 50; `Live`, `Remix`, EP bilgileri gerçek yanıtta doğrulandı. Kanıt: `artifacts/live-runtime-v016.json`. `tools/verify-live-music.mjs` yalnız metadata/listeleri okur; parola ortam değişkeninden alınır, çıktıya/sır dosyasına yazılmaz.

### Kullanıcı kabulü — ACCEPT-16

1. Müzik → sanatçı/albüm → **Takip ve otomasyon**. Yalnız metadata takibini aç; kapsam ve albüm istisnasını yenilemeden sonra kontrol et.
2. Gerçek yerel bir albümü çal. Ardışık iki parça, mini/tam, hızlı seek/sonraki, shuffle/repeat ve telefonda arka plan davranışını dinle.
3. Müzik oynatma ayarındaki parça/albüm ReplayGain'i karşılaştır. Albümde **Ses benzerliğini analiz et**, favori ekle ve **Sana yakın** ekranını aç.
4. Kullanıcı tarafından edinilmiş yönetilen bir dosyada etiket önizlemesini aç; yazım, yeni ad/kapak ve **Müzik işleri → Önceki dosyayı geri al** akışını kontrol et.

Gerçek müzik kaynağı hesabı/transferi, browser/cihaz kabulü, Linux/ARM64/Docker çalıştırma ve kamu lisans/imza kapıları açık kalır. Bu yayın tüm Home kapsamının tamamlandığı anlamına gelmez.

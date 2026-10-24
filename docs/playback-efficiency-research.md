# Film oynatma verimliliği — Plex / Jellyfin karşılaştırması

İnceleme: **2026-10-23**. Home kaynak sürümü **0.44.0 / şema43**. İlgili gereksinimler: **PLY-01/02/03/04/05/06, CLI-01-A, OPS-08**.

Durum: **kaynak/belge araştırması ve uygulama önerisi**. Kullanıcının CasaOS film oturumundan codec, oynatma kararı, işlemci ve ağ ölçümü henüz alınmadı; bildirilen donmanın kesin nedeni belirlenmiş sayılmaz. İncelemenin kapsamı film oynatmadır.

**Uygulama devamı:** Kullanıcı genel tarayıcı desteğini esas alan düzeltmeye başlanmasını istedi; marka/model listesi uygulanmayacak. Kaynak bazlı yetenek, HEVC koruma ve sınırlı üretimin yerel uygulama/kanıt durumu: [PLAY-04](playback-efficiency.md).

## Referans kimlikleri

- Plex: resmi Direct Play / Direct Stream ve hardware acceleration belgeleri [P1, P2]. Kapalı sunucu kaynak kodu hakkında çıkarım yapılmadı.
- Jellyfin Server **v12.3**, commit `06de749d435e2d0be0c5119a41f2300fdd7143e5`; Jellyfin Web **v12.3**, commit `c06cb17b274c10f9015412305edb0afdc010ae7a`. GitHub latest-release/tag kimlikleri ve aşağıdaki dosyalar bu tarihte okundu. Kaynak projelerin lisansı **GPL-2.0**; bu araştırma bir bağımlılık veya kod benimsemesi değildir.
- Context7 belgeleri, resmi sayfalar ve sürüme sabitlenmiş kaynak birlikte değerlendirildi. Belge tablosu, her TV modelinde ölçülmüş destek anlamına gelmez.

## 1. Referans karar modeli

Plex'in Direct Stream tanımı, uyumlu görüntü/sesin başka bir kapsayıcıya yeniden paketlenmesini ve gerektiğinde yalnızca sesin dönüştürülmesini içerir [P1]. Jellyfin belgeleri Direct Play, Remux, Direct Stream (ses dönüşümü), Video Transcode ayrımını yapar [J1].

Jellyfin `StreamBuilder` kapsayıcı, video, ses ve altyazı uyumsuzluklarını ayrı nedenler olarak toplar. `GetVideoTranscodeProfile` uygun profilleri önce görüntüyü kopyalayabilme, sonra sesi kopyalayabilme/uyarlayabilme açısından sıralar. `EncodingHelper.CanStreamCopyVideo` codec, profil, HDR aralığı, çözünürlük, bitrate, fps, bit derinliği, rotation ve altyazı yakma koşullarını denetler. `TryStreamCopy` görüntü ve ses çıktılarını ayrı ayrı `copy` seçebilir [J2, J3].

Önemli uygulama ayrıntısı: Jellyfin'in bazı yollarında `PlayMethod.Transcode` veya transcoding endpoint'i kullanılması, görüntünün mutlaka yeniden kodlandığı anlamına gelmez. Nihai video/audio codec kararları ayrıca incelenmelidir. Yerel dosyada gerçek oynatma hatasından sonra video stream-copy'yi kapatan fallback de vardır; bütün hata durumlarında önce remux denediği söylenemez [J3, J7].

**Görüntü dönüşümü gerçekten gerekli olabilir:** kaynak codec/profil/bit derinliği istemcide desteklenmiyorsa, çözünürlük/bitrate azaltılacaksa, desteklenmeyen HDR için tone mapping gerekiyorsa veya altyazı görüntüye yakılacaksa. Kapsayıcı/ses uyumsuzluğu tek başına böyle bir zorunluluk oluşturmaz; seçilen hedef kapsayıcı/protokol de özgün görüntüyü desteklemelidir.

## 2. Home'da doğrulanan farklar

| Alan | Home kaynağında mevcut davranış | Referanstan çıkan iyileştirme |
|---|---|---|
| HEVC taşıma | `PlaybackPlanner.cs:51`: HLS uyarlamasında video kopyalama yalnızca H.264 için açık. HEVC direct-play koşulları sağlanmazsa, istemci HEVC bildirse bile video dönüşümü seçiliyor. | İstemci/protokol/kapsayıcı bazında HEVC remux ve HEVC + yalnız ses dönüşümü yolu. |
| Ses | `usePlayback.ts:14-22`: codec listeleri genel MIME ipuçlarından üretiliyor; kanal sınırı her cihazda 2. `PlaybackPlanner.cs:52`: HLS'de ses kopyalama AAC ve en fazla iki kanal ile sınırlı. | Video ve sesin kapsayıcıya özel destek koşulları; gerçek kanal/çıkış tercihleri. |
| Büyük film / cache | `PlaybackPlanner.cs:54-58`: kalan filmin tahmini toplam kopya boyutu bütçeyi aşarsa `copyVideo=false`. `PlaybackManager.cs:35-36,68-76`: varsayılan 8 GiB/oturum bütçesi ve kalan çıktı için rezervasyon. | Oynatma konumuna göre sınırlı segment yaşam döngüsü ve üretim hızı yönetimi. Depolama sınırını codec uyumsuzluğu gibi ele almamak. |
| HLS taşıma | `PlaybackManager.cs:310-315`: yalnız MPEG-TS `.ts` segmentleri. | Doğrulanmış profillere göre TS veya fMP4; HEVC'nin hedef istemcide doğru codec etiketleri/manifest ile taşınması. |
| TV oynatıcı seçimi | `web/src/playbackTransport.ts`: tüm MSE destekli cihazlarda hls.js öncelikli. | Jellyfin LG/webOS ve Tizen'de native HLS'yi seçiyor [J5]. Cihaza özel karar gerekir. |
| Uyumluluk bilgisi | `PlaybackClient`: bağımsız codec/container listeleri ve kanal sayısı; HDR/profil/level/fps bazında istemci sınırları yok. | Kaynağı, seçili izleri ve kullanılan oynatma yolunu birlikte değerlendiren açıklanabilir profil. |
| Görünürlük | `Player.tsx:134`: direct olmayan yazılım yolları ortak “H.264 uyumluluk modu” etiketiyle gösteriliyor; yeniden paketleme ve video kodlama ayrımı görünmüyor. | Gerçek video/audio kararları, gerekçe, üretim hızı ve istemci tamponu görünür olmalı. |

Home H.264 remux ve H.264 + yalnız ses dönüşümünü zaten uyguluyor ve bunların odaklı testleri var (`tests/HomeServer.Tests/PlaybackTests.cs`). Sorun bütün dosyaları her zaman dönüştürmesi değil; özellikle HEVC ve büyük dosya uyarlama yollarının dar olmasıdır.

### Açıklayıcı karar örnekleri — gerçek kullanıcı dosyası ölçümü değil

1. **MP4 + HEVC + AAC stereo**, istemcinin tamamını desteklediği durumda: Home da direct play seçebilir.
2. **MKV + HEVC + AAC**, MKV yolu uygun değil ama istemci HEVC'yi uygun HLS/MP4 yolunda destekliyorsa: görüntüyü koruyan remux mümkündür. Home'un mevcut planı video dönüşümüne gider.
3. **HEVC + desteklenmeyen DTS/çok kanallı ses**, HEVC görüntü diğer yönleriyle uyumluysa: görüntü korunup yalnız ses uyarlanabilir. Home'un HLS HEVC-copy eksikliği burada da video dönüşümüne yol açar.
4. **Uyumlu H.264 fakat büyük kopya çıktısı**: Home'un tüm kalan süre üzerinden cache hesabı yeniden kodlamayı tetikleyebilir. Bu, cihazın H.264 çözemediğinin kanıtı değildir.

Remux görüntü kalitesini ve çözünürlüğünü korur; özgün videonun ağ bant genişliği ihtiyacını ortadan kaldırmaz.

## 3. Jellyfin'in LG ve depolama yaklaşımından önemli ayrıntılar

- `browserDeviceProfile.js` MP4, MKV, TS-HLS ve fMP4-HLS için ayrı codec birleşimleri oluşturur; HEVC, ses kanalları, HDR ve bilinen cihaz/sürüm istisnalarını değerlendirir [J4]. Bunlar destek ipuçları ve heuristikler içerir; kusursuz bir donanım ölçümü değildir.
- `htmlMediaHelper.enableHlsJsPlayer` LG/webOS ve Tizen için native oynatıcıyı seçer; Chromium masaüstünde native-HLS ipucuna rağmen hls.js'i tercih eden ayrı kuralı vardır [J5]. Home'un önceki Chromium parse hatası için eklenen MSE-first düzeltmesini bütün platformlarda tersine çevirmek uygun değildir: [önceki bulgu](playback-hls-fix.md).
- Jellyfin `browser.js`, LG'nin yerleşik NetCast tarayıcısında webOS sürümünün UA'dan güvenilir biçimde çıkarılamayabileceğini açıkça belirtir [J8]. TV'deki Plex/Jellyfin uygulaması ile Home'un yerleşik tarayıcıdaki oynatmasını eşdeğer kabul etmemek gerekir.
- `TranscodingThrottler` ayarı etkinse üretilen görüntü izlenen/indirilen konumun yeterince önüne geçince FFmpeg'i duraklatır. `TranscodingSegmentCleaner` ayarı etkinse tüketilmiş eski HLS parçalarını temizler [J6]. Bu mekanizmaların her kurulumda varsayılan olarak açık olduğu iddia edilmiyor.

## 4. Önerilen ilk uygulama kapsamı

1. Film için kapsayıcı/protokol ve codec birleşimlerini ayıran istemci profili; kaynak codec/profil/HDR bilgisiyle eşleştirme.
2. Desteklenen HEVC görüntüyü koruyan remux / yalnız ses dönüşümü ve uygun TS/fMP4 seçimi.
3. Kaynak filmi koruyan, seek/iptal/oturum yetkilerini sürdüren sınırlı segment cache'i ve tüketim konumuna bağlı üretim.
4. Oynatıcıda direct/remux/audio-transcode/video-transcode ayrımı, dönüşüm nedeni ve üretim hızı. Native-HLS tercihi doğrulanmış TV profiline bağlı olmalı.

Intel hızlandırması ayrı bir yetenek olarak ölçülmeli: uygun ikinci nesil Core iGPU'su H.264 hızlandırabilir, fakat bu nesil HEVC donanım çözmesini sağlamaz [P2, I1]. Plex/Jellyfin kullanmak fiziksel codec sınırını kaldırmaz. HEVC görüntüyü doğrudan taşımak ise sunucuda HEVC çözmeyi gerektirmez.

## 5. Açık kabul bilgisi

Donan filmin gerçek video/audio/altyazı özellikleri ve Home'un seçtiği karar; LG model/webOS/tarayıcı bilgisi; sunucu CPU modeli ve bağlantı türü gerekli. Kaynak araştırması Home'da iyileştirilecek yerleri doğrular, kullanıcının donmasının yalnızca bunlardan kaynaklandığını doğrulamaz. Gerçek TV/browser kabulü kullanıcıya aittir.

## Kaynaklar

- **P1:** [Plex — Direct Play / Direct Stream](https://support.plex.tv/articles/200250387-streaming-media-direct-play-and-direct-stream/)
- **P2:** [Plex — Hardware-Accelerated Streaming](https://support.plex.tv/articles/115002178853-using-hardware-accelerated-streaming/)
- **J1:** [Jellyfin codec support](https://jellyfin.org/docs/general/clients/codec-support/), [transcoding](https://jellyfin.org/docs/general/post-install/transcoding/)
- **J2:** [StreamBuilder.cs — profile sıralaması, satır 847–1037](https://github.com/jellyfin/jellyfin/blob/06de749d435e2d0be0c5119a41f2300fdd7143e5/MediaBrowser.Model/Dlna/StreamBuilder.cs#L847-L1037)
- **J3:** [EncodingHelper.cs — CanStreamCopyVideo](https://github.com/jellyfin/jellyfin/blob/06de749d435e2d0be0c5119a41f2300fdd7143e5/MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs#L2426-L2638), [TryStreamCopy](https://github.com/jellyfin/jellyfin/blob/06de749d435e2d0be0c5119a41f2300fdd7143e5/MediaBrowser.Controller/MediaEncoding/EncodingHelper.cs#L7282-L7328)
- **J4:** [Jellyfin Web — browserDeviceProfile.js](https://github.com/jellyfin/jellyfin-web/blob/c06cb17b274c10f9015412305edb0afdc010ae7a/src/scripts/browserDeviceProfile.js)
- **J5:** [Jellyfin Web — native/HLS.js seçimi](https://github.com/jellyfin/jellyfin-web/blob/c06cb17b274c10f9015412305edb0afdc010ae7a/src/components/htmlMediaHelper.js#L31-L75)
- **J6:** [TranscodingThrottler.cs](https://github.com/jellyfin/jellyfin/blob/06de749d435e2d0be0c5119a41f2300fdd7143e5/MediaBrowser.Controller/MediaEncoding/TranscodingThrottler.cs), [TranscodingSegmentCleaner.cs](https://github.com/jellyfin/jellyfin/blob/06de749d435e2d0be0c5119a41f2300fdd7143e5/MediaBrowser.Controller/MediaEncoding/TranscodingSegmentCleaner.cs)
- **J7:** [Jellyfin Web — hata sonrası fallback](https://github.com/jellyfin/jellyfin-web/blob/c06cb17b274c10f9015412305edb0afdc010ae7a/src/components/playback/playbackmanager.js#L3477-L3531)
- **J8:** [Jellyfin Web — webOS sürüm algılama sınırı](https://github.com/jellyfin/jellyfin-web/blob/c06cb17b274c10f9015412305edb0afdc010ae7a/src/scripts/browser.js#L97-L147)
- **I1:** [Intel VA-API sürücüsü — nesil/codec tablosu](https://github.com/intel/intel-vaapi-driver/blob/master/README)

# Oyun gecikmesi: hazır yayın altyapısı karşılaştırması

İnceleme: **2026-10-23**. Kart **GAME-LATENCY-RESEARCH-01**; gereksinimler **GAM-05/10/11/14/19, CLI-01**. Kullanıcı radyo/görsel diliminden sonra gecikme araştırmasını yeniden açtı. **Kaynak ve belge araştırması tamamlandı; yeni yayın altyapısı henüz benimsenmedi veya ölçülmedi.**

**2026-10-24 devamı:** Kullanıcı cihaz bazlı deneysel ikinci yöntemi onayladı ve PC'de gecikmenin TV'den daha az olduğunu belirtti. [SELKIES-01 uygulaması](selkies-streaming.md) bu araştırmanın ardından başladı; aşağıdaki değerlendirme araştırma anının kaydıdır. Varsayılan değişikliği gerçek cihaz sonucuna bağlıdır.

## Sonuç

İlk karşılaştırmalı prototip adayı **Selkies 2.1.0**: Linux üzerinde hazır capture/encode, browser player, gamepad aktarımı ve gecikme/kuyruk yönetimini birlikte sunuyor. Home ekranına gömülebilen istemcisi, kullanıcının ayrı uygulama kurmadan oynama koşuluna uyuyor. Bu bir **deneme seçimi**; kullanıcının CasaOS/browser birleşiminde daha hızlı çalıştığı henüz kanıtlanmış değil.

**GStreamer `webrtcsink` + `gstwebrtc-api`** framework düzeyindeki güçlü ikinci aday. Hazır Selkies yolunun capture/CPU maliyeti uygun çıkmazsa değerlendirilir. Çıplak Pion/libdatachannel üzerine Home'a ait yeni bir WebRTC/oyun-yayın motoru kurmak yerine bu üst seviye bileşenler tercih edilir.

## Bugünkü Home yolu ve ölçümün sınırı

```text
Video/ses: RetroArch + libretro core
           -> FFmpeg H.264/Opus recorder
           -> loopback MPEG-TS
           -> MediaMTX 1.21.2 / WHEP-WebRTC
           -> tarayıcı medya öğesi

Girdi:     tarayıcı gamepad/klavye
           -> Home'a ait WebSocket (HTTP fallback)
           -> olayla uyanan yerel relay
           -> RetroArch input
```

- Emülatör, encoder ve WebRTC zaten upstream bileşenler. Home oturum, kayıt, input sahipliği ve bu bileşenlerin bağlanmasını yönetiyor.
- Güncel `NativeGamePlayer.tsx` gamepad'i animation frame ile örnekliyor; hazır WebSocket'te yapay 20 ms gönderme aralığı yok. `NativeGameManager` relay'i olayla uyanıyor, fallback beklemesi **4 ms**. Eski 100 ms polling/3 saniyelik Windows policy blokajı bugünkü davranış olarak anlatılmamalı.
- Recorder **tek encoder thread, ultrafast/zerolatency H.264, B-frame=0, 2.5 ms Opus** kullanıyor. Home adapter'ının mux flush/readback düzeltmeleri zaten mevcut. Sadece aynı düşük gecikme bayraklarını yeniden eklemek yeni çözüm değil.
- `MediaMtxGameConfiguration` bir encoded MPEG-TS kaynağı bağlıyor. İncelenen MediaMTX kaynaklarında NACK/TWCC desteği var; Home'un dış recorder'ına dinamik bitrate veya anlık keyframe talebi götüren kontrol bağlantısı yok. Sabit encoder ile browser/ağ geri bildirimi arasındaki bu sınır incelenmeli. **Bu bulgu MediaMTX'in tek başına gecikme nedeni olduğunu göstermiyor.**
- WebSocket sıralı TCP aktarımı kullanır; kayıp/congestion halinde eski girdiler bekleyebilir. DataChannel kullanmak da tek başına çözüm değildir: kanalın reliability/ordering, kuyruk yaşı ve bırakılan tuşların toparlanması birlikte ele alınmalıdır.
- Browser tarafında `jitterBufferTarget=0` / destekleniyorsa `playoutDelayHint=0` zaten isteniyor. Bunlar tarayıcının tamponunu kesin sıfırlamaz. Görünen buffer/decode sayaçları toplam tuş-ekran süresi değildir.

Mevcut kanıtların ayrımı:

| Ölçüm | Sonuç | Kapsam |
|---|---|---|
| 0.34.4 eski Windows recorder/decoder deneyi | 78–86 ms | Minimal özgün GBA fixture; MediaMTX/browser yok; bugünkü toplam süre değil |
| 0.37 son Windows GPU tanısı | Yaklaşık 9–38 ms | Input datagramı → tam encoded frame gelişi; gerçek browser ekranı yok |
| 0.43 Linux yedi özgün sistem tanısı | 46–82 ms | Home WebSocket + MediaMTX + non-loopback UDP + aiortc decoded frame; gerçek TV/browser/gamepad yok |
| Kullanıcının bugünkü Mario/CasaOS/browser akışı | Henüz ayrıştırılmadı | Mario adı/sistem, browser istatistikleri, ağ yolu ve host yükü gerekli |

Kaynaklar: [0.34.4](gaming-video-delay.md), [0.37](gaming-native-host-037.md), [0.43](headless-lan-043.md). Bir ölçümdeki decoder beklemesi başka bir ölçümdeki encoder maliyetine eklenmez. Fiziksel input-to-photon için kullanıcı cihazındaki ölçüm gerekir.

## Adaylar

| Aday | Somut yeniden kullanım | Home açısından değerlendirme |
|---|---|---|
| **Selkies 2.1.0** | Linux X11/Wayland capture, `pixelflux` encoder, `pcmflux` Opus, browser core, WebRTC/WebSocket input, userspace gamepad interposer | **İlk prototip.** Kendi yayın motorumuzu yazma ihtiyacını en çok azaltan browser odaklı birleşim. Yeni capture/transport sürümünün olgunluğu Home'daki denemeyle doğrulanacak. |
| **GStreamer `webrtcsink` + `gstwebrtc-api`** | Hazır signaling/client, encoder ayarı, TWCC congestion control, FEC/retransmission ve control data channel | Olgun framework; üst seviye sink çıplak `webrtcbin` kullanımından daha çok işi üstlenir. Frame kaynağı, emulator input ve Home yaşam döngüsü adapter'ı gerekir. |
| **Sunshine + Moonlight Web v3.0.0** | Hazır oyun yayın host'u ve encoded görüntüyü browser WebRTC'ye taşıyan köprü | Sunshine ekosistemi güçlü; browser köprüsü ayrı/yeni. v3 protokol katmanı `moonlight-common-rust` kullanıyor. Native Moonlight'ın bütün olgunluk kanıtı buna aktarılamaz. |
| **Wolf + browser bridge** | Headless Wayland, GStreamer encoder, virtual audio/input ve Moonlight protokolü | Linux/headless için değerli; doğrudan browser client sunmadığından ek köprü gerekir. Mevcut tek-retro-oturum problemi için daha geniş entegrasyon. |
| **CloudRetro / `giongto35/cloud-game`** | Libretro frame callback'inden GStreamer/Pion'a doğrudan giden hazır retro worker; browser/gamepad/save örneği | Küçük retro sistemlere çok uygun mimari referans. RetroArch frontend'ini ve kayıt/ayar sözleşmesini de değiştirdiği için yalnız transport değişikliği değil. |

### Selkies'te doğrulanan ayrıntılar

- GitHub latest stable **2.1.0, 2026-10-21**; incelenen commit `6ee68a7a815956e6745e916741f13335e0b3ff53`.
- Güncel mimari eski GStreamer tabanlı Selkies anlatımıyla aynı değil: Python orchestration, Rust `pixelflux`/`pcmflux`, WebRTC'de vendored aiortc. Varsayılan **WebSocket + WebCodecs**; **WebRTC ayrıca seçilebiliyor**.
- 2.1 sürüm notları iki transportta congestion control, decoder yetişemediğinde frame-rate yönetimi, kuyruk/yeniden bağlanma ve gamepad iyileştirmeleri bildiriyor. Upstream'in benchmark değerleri Home donanımına ait ölçüm olarak kullanılmadı.
- `selkies-web-core` belgelenmiş same-origin `postMessage` arayüzüne sahip: `gamepadControl`, ses, stream ayarları, pipeline durumları ve istatistikler. Home oyun/pause ekranına gömülebilir; menü/gameplay input sahipliğinin aynı kalması entegrasyonun parçasıdır.
- WebCodecs yolu tarayıcının gerçek decoder/API ve secure-context desteğiyle seçilmeli. LAN HTTP/TV için WebCodecs varsayımı yapılmaz; WebRTC/H.264 yolu ayrıca karşılaştırılır. Model adına göre whitelist önerilmiyor.
- CPU H.264 yolu var; discrete GPU zorunlu kabul edilmiyor. İkinci nesil Intel'in olası entegre encoder'ı, sürücü ve container erişimi ölçülmeden donanım hızlandırması kabul edilmiyor.
- **Somut entegrasyon maliyeti:** Home'un `deploy/native/build.sh` tarifi Pulse/ALSA/SDL/udev gibi çıkış/girdi backend'lerini kapatıyor; software core normal ekransız recorder yolunu kullanıyor. Selkies'in ekran + PulseAudio capture yolu mevcut pakete yalnız URL değiştirerek bağlanamaz. Uygun RetroArch audio/input/display build'i veya upstream'in desteklediği başka handoff gerekir. Hazır encoded Home akışını decode edip tekrar capture/encode etmek denemenin temel yolu olmayacak.

### Diğer adaylardaki önemli sınırlar

- `webrtcsink` kendi oluşturduğu encoder'ların bitrate'ini yönetir. Mevcut encoded TS'yi yalnız başka bir WebRTC sink'e geçirmek, dış FFmpeg encoder'ına otomatik feedback sağlamaz. Raw frame/audio kaynağını bu pipeline'a vermek veya dış encoder kontrolü bağlamak gerekir. Genel `GstNavigation` desteği bütün RetroPad/save/input sözleşmesinin hazır olduğu anlamına gelmez.
- Moonlight Web **v3.0.0, 2026-10-22**, commit `0b75afb723d87f0ab0159c3bfce2bcf5bcd2b30e`. Video yolu decode/re-encode gerektirmeden RTP paketleyebiliyor; PLI/FIR ve REMB geri bildirimi işleniyor. İncelenen WebRTC yolundaki `unbounded_channel` kullanımı kuyruk davranışının yük altında ayrıca ölçülmesi için bir neden; gecikme hatasının kanıtı değil. v3'ün yeni Rust protokol uygulaması `Cargo.toml` içinde pinli.
- Sunshine latest stable `v2026.914.233613` (**2026-09-15**). Wolf'un GitHub latest release'i `v2024.07`, default branch'i `beta`; `stable` dokümanı ayrı. Bu release tarihinden bakımın bittiği sonucu çıkmıyor.
- CloudRetro'nun incelenen güncel commit'i `1fab9ef07a02cab0e0cdea169e95b5ec6e2e04ed`; API'de son push **2026-09-05**, latest release **v2.6.1 / 2021-09-06**. Current source v3 modülü kullanıyor; eski release ile aynı build değil. Input kanalında unordered/zero-retransmit ayarı var, fakat varsayılan `skipLateFrames: false`, video VP8 ve ayrı core/save ayarları olduğu da dikkate alınmalı.

## Önerilen sonraki çalışma

1. **Aynı oyunun bugünkü yolunu ölç:** Mario'nun tam adı/sistemi, host emülasyon hızı ve CPU, tarayıcı buffer/decode/frame-drop bilgisi, gerçek ICE UDP/TCP yolu ve input gönderme/uygulama süreleri. PC'de menü gamepad'inin çalışması oyun gecikmesinin PC'de olmadığı anlamına gelmez.
2. **Tek A/B prototip:** A mevcut Home, B Home içinden açılan Selkies. Aynı core/ROM, core'un bildirdiği frame rate, benzer codec/bitrate/çözünürlük. Önce CPU-only düşük çözünürlük; 4K TV viewport'una otomatik 4K capture açılmasın. Kullanıcı gerçek cihaz kabulünü yapar; geliştirme protokol/işlem kontrolleri izole özgün fixture'larla yapılır.
3. **Kararı toplam sonuçla ver:** median/p95 input→görüntü, uzun kullanımda kuyruk artışı, frame pacing, CPU/audio yükü; duraklat/save/relaunch, kopma ve gamepad bırakma davranışı. FPS veya görüntü kalitesini düşürerek elde edilen fark eşdeğer gecikme kazancı sayılmaz.

Ölçüm ekleri gerekiyorsa mevcut Home paneli genişletilebilir: session-scoped input RTT/apply acknowledgement, core frame period/host yükü, selected ICE candidate-pair, RTP kayıp/jitter/decoded/dropped frame sayaçları ve desteklendiğinde `requestVideoFrameCallback` metadata. İstemci ve sunucu saatleri doğrudan çıkarılarak sahte one-way süre üretilmez; RTP/RTCP eşleme veya aynı saatteki olaylar kullanılır. Browser callback ekranın fiziksel ışık değişimi değildir; son fiziksel kabul kullanıcı ölçümüdür.

Home katalog/profil/başlat-kaydet-çık işlerini sahiplenir; Selkies'e oturum kapsamlı player/control adapter'ı bağlanır. Deneme ayrı managed çalışma/kayıt kopyası kullanır. Yeni frontend build'i gerekirse mevcut core/build-bound save ve ayar kimlikleri için doğrulanmış geçiş gerekir; `deploy/native/runtime-lock.json` deneme amacıyla sessizce değiştirilmez.

## Lisans ve kaynaklar

Bu tur yeni runtime bağımlılığı eklenmedi. Selkies **MPL-2.0**, Sunshine/Moonlight Web **GPL-3.0** ailesi, Wolf **MIT**, CloudRetro **Apache-2.0**. GStreamer core **LGPL**, incelenen `gst-plugin-webrtc` manifesti **MPL-2.0**. Encoder/core ve transitive build bileşenlerinin koşulları ayrıca geçerli; Home dağıtım lisansı kararı açık.

Tümü **2026-10-23** kontrol edildi. GitHub repository/release API, sabit kaynak dosyaları, resmi dokümanlar ve Context7 kullanıldı. Bu araştırma yeni latency benchmark'ı, kurulum veya yayın çalıştırmadı.

- **R1 — Selkies:** [2.1.0 sürümü](https://github.com/selkies-project/selkies/releases/tag/2.1.0), [sabit tasarım](https://github.com/selkies-project/selkies/blob/6ee68a7a815956e6745e916741f13335e0b3ff53/docs/design.md), [bileşenler](https://github.com/selkies-project/selkies/blob/6ee68a7a815956e6745e916741f13335e0b3ff53/docs/components/index.md).
- **R2 — Selkies embedding/input:** [core API](https://github.com/selkies-project/selkies/blob/6ee68a7a815956e6745e916741f13335e0b3ff53/addons/selkies-web-core/README.md), [input interposer](https://github.com/selkies-project/selkies/blob/6ee68a7a815956e6745e916741f13335e0b3ff53/addons/input-interposer/README.md).
- **R3 — GStreamer:** [rswebrtc özellikleri/JS client](https://gstreamer.freedesktop.org/documentation/rswebrtc/index.html), [webrtcsink API](https://gstreamer.freedesktop.org/documentation/rswebrtc/webrtcsink.html), [plugin manifesti](https://github.com/GStreamer/gst-plugins-rs/blob/main/net/webrtc/Cargo.toml).
- **R4 — Moonlight Web:** [v3.0.0 sürümü](https://github.com/MrCreativ3001/moonlight-web-stream/releases/tag/v3.0.0), [sabit video/feedback yolu](https://github.com/MrCreativ3001/moonlight-web-stream/blob/0b75afb723d87f0ab0159c3bfce2bcf5bcd2b30e/src/api/stream/webrtc/video.rs), [Rust dependency pin'i](https://github.com/MrCreativ3001/moonlight-web-stream/blob/0b75afb723d87f0ab0159c3bfce2bcf5bcd2b30e/Cargo.toml).
- **R5 — Sunshine/Wolf:** [Sunshine stable](https://github.com/LizardByte/Sunshine/releases/tag/v2026.914.233613), [Wolf stable mimarisi](https://games-on-whales.github.io/wolf/stable/dev/how-it-works.html).
- **R6 — CloudRetro:** [sabit README](https://github.com/giongto35/cloud-game/blob/1fab9ef07a02cab0e0cdea169e95b5ec6e2e04ed/README.md), [WebRTC input](https://github.com/giongto35/cloud-game/blob/1fab9ef07a02cab0e0cdea169e95b5ec6e2e04ed/pkg/network/webrtc/webrtc.go), [encoder/frame ayarları](https://github.com/giongto35/cloud-game/blob/1fab9ef07a02cab0e0cdea169e95b5ec6e2e04ed/pkg/config/config.yaml).
- **R7 — MediaMTX 1.21.2:** [outbound track](https://github.com/bluenviron/mediamtx/blob/914e39535ece709919ac325aaeed98d394ed9093/internal/protocols/webrtc/outbound_track.go), [peer/interceptor](https://github.com/bluenviron/mediamtx/blob/914e39535ece709919ac325aaeed98d394ed9093/internal/protocols/webrtc/peer_connection.go).
- **R8 — Browser ölçümü:** [jitterBufferTarget](https://developer.mozilla.org/en-US/docs/Web/API/RTCRtpReceiver/jitterBufferTarget), [video frame metadata](https://developer.mozilla.org/en-US/docs/Web/API/HTMLVideoElement/requestVideoFrameCallback), [VideoDecoder](https://developer.mozilla.org/en-US/docs/Web/API/VideoDecoder).

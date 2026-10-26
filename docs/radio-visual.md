# Radyo ve görsel mod — RADIO-VISUAL-01

Tarih: **2026-10-23**. Gereksinimler **MUS-11/12, PLY-03/06, USR-03, CLI-01/05, OPS-09**. Durum: **2026-10-24'te 0.45.0 / stable ile yayımlandı; kullanıcı tarayıcı kabulü bekliyor**. [Yayın kaydı](playback-streaming-batch-045.md).

## Kullanıcı geri bildirimi ve tasarım

Kullanıcı, radyo çalarken şömine/başka hareketli sahne seçildiğinde sesin durduğunu; tekrar oynatınca görüntünün siyaha veya plağa döndüğünü bildirdi. Önceki yapı ayrı bir `<audio>` ve ayrı bir otomatik oynayan `<video>` kullanıyordu. Bu mimari eşzamanlı medya kaynakları sınırlı tarayıcılarda çatışabilir. Cihazdaki kesin decoder davranışı uzaktan ölçülmüş sayılmaz.

Radyo görsel modunda **tek medya öğesi ve tek birleşik A/V akışı** kullanılır. Çözüm TV markası/modeli kontrolü içermez. Gamepad menü kontrolü ve oyun gecikmesi kullanıcı tarafından ayrı araştırma için ertelendi.

## Uygulama

- On mevcut lisanslı stok seçeneğinin URL, ID, etiket ve kredi bilgileri `web/src/music/sceneSources.json` ortak kataloğuna taşındı; backend aynı dosyayı embedded resource olarak kullanır. Önceki Git kaynağıyla tüm kayıtların birebir eşitliği doğrulandı.
- Kullanıcı stok görüntüleri açıp bir sahne seçince, açık radyo oturumu ve mevcut giriş/CSRF kapsamında hazırlık istenir. İstemci kaynak URL göndermez; yalnız katalog ID'si kabul edilir.
- Görsel kaynak yalnız uygulamanın managed staging alanına alınır. Kaynak başına 128 MiB, tek eşzamanlı hazırlık ve dört dakikalık bütçe vardır. FFmpeg en fazla 30 saniyelik, en fazla 1280×720/24fps **sessiz H.264** döngü ve JPEG poster hazırlar. Hazırlanan çıktılar boyut/codec/hash ile doğrulanıp managed scene library'ye atomik yayımlanır.
- Hazırlık sırasında mevcut radyo kaynağı çalmayı sürdürebilir. Sahne hazır olduğunda aynı radyo lease'ine bağlı FFmpeg işlemi video akışını **kopyalar**, radyo sesini AAC ile birleştirir. Her kareyi canlı olarak video kodlama yoktur. Kaynak geçişinde kısa bir yükleme olabilir; kesintisiz örnek düzeyinde ses handoff'u iddia edilmez.
- Birleşik çıktı HLS/fMP4'tür. İkinci bir playback/tuner kotası alınmaz. Ses sağlayıcısı yine private loopback relay üzerinden ve radyo girişinin yaşam süresine bağlıdır. Görsel oturumun çalışma alanı 64 MiB ile sınırlıdır.
- Radyo oynatıcısının medya DOM öğesi sabit bir portal host içinde kalır; görsel/fullscreen slotuna aynı öğe taşınır. Sahne değişimi veya modal açılışı ikinci bir medya decoder başlatmaz.
- Görsel hazırlık/akış hatası radyo lease'ini kapatmaz; ses kaynağına geri dönülür ve seçili sahnenin posteri/hata durumu korunur. Duraklat/devam ve hızlı sahne değişimi revision/epoch ile denetlenir. Geç yanıtlar eski sahneyi yeniden etkinleştiremez.
- Görsel modu kapatmak/görseli devre dışı bırakmak yalnız ses akışına döner. Duraklatma radyo kaynaklarını bırakır; seçilen sahne/poster tercihi devam için korunur. İzin iptali veya radyo kapanışı ilişkili mux işlemini iptal eder.
- Önbellek ve native süreç işaretçileri başlangıçta toparlanır. Normalleştirme ve mux işlemleri yalnız managed çalışma dizinlerini kullanır. Hazır poster erişimi kullanıcı oturumu ve stok görsel tercihiyle korunur.
- Kaynak/author/CC lisansı ve uyarlama bildirimi görsel modda görünür. English/Türkçe hazırlık/hata durumları ve reduced-motion davranışı korunur.

## Doğrulama

- Windows FFmpeg 9.0.2 ile **26 radyo/müzik/backend kontrolü** ve ilave **bir HTTP sınır kontrolü** geçti: sessiz çıktı, bir kez hazırlama, hash değişikliğinin reddi, boyut sınırı, tek radyo lease'i/tek A/V çıktı, eski revision reddi, kapanış ve giriş ayrımı; opt-in/CSRF/giriş olmadan kaynak hazırlanmaması.
- **15 Node kontrolü** geçti: sahne/oynatma niyeti, stale yanıt, mevcut sahne kataloğu, tercihler ve yerelleştirme.
- Frontend/worker/PWA build geçti. Gerçek stok video veya internet radyo yayını geliştirme doğrulamasında açılmadı; medya fixture'ları yalnız izole testlerde üretildi.
- Linux image build ve **UID/GID 1654 altında FFmpeg 6.1.1 ile 26 kontrol, sıfır atlanan** geçti. Görsel hazırlama/ses ayırma, tek A/V çıktı ve lease/iptal kapsamı gerçek native araçlarla doğrulandı. Son HTTP opt-in kontrolü Windows tarafındaki toplamı **27** yaptı.
- Radyo native/izin testleri ve Node sahne kontrolleri **0.45.0 hosted yayını sırasında da geçti**. Gerçek fullscreen görünümü, ses/görüntü geçişi ve hedef tarayıcı kabulü kullanıcıya aittir.

Yerel kanıtlar: `artifacts/radio-visual/`; yayın kanıtları `artifacts/release-045/`. Normal kaynak kataloglarında sentetik içerik yoktur.

## 2026-10-25 kullanıcı geri bildirimi

- Kullanıcı güncellenmiş arayüzü TV'de gördüğünü ve PC tarayıcısında önceki hatalarda iyileşme olduğunu bildirdi. TV'de film hata vererek açılmadı; radyo takıldı ve başlatılamadı. Kullanıcı TV kontrollerini tekrarlayacak; hata metni/neden ve cihaz kabulü henüz kesinleşmedi. Bu geri bildirim otomatik güncelleyicinin kurulu/çalışıyor olduğunun kanıtı değildir.
- Kullanıcının istemediği tam ekran hazırlık açıklaması (`Preparing the visual; radio can keep playing.`) **yerel kaynakta kaldırıldı**. Hazırlıkta mevcut poster/plak davranışı ve gerçek hata/retry kontrolleri korunur; yerine başka açıklama/banner eklenmedi. Bu küçük UI değişikliği henüz yayımlanmadı.
- Kontrol: **15 Node/yerelleştirme testi ve frontend/worker/PWA build geçti**. Bu UI kontrolü TV'deki film/radyo açılış sorunlarının çözümü veya cihaz kabulü değildir.

## Kaynak ve lisans kaydı

- Mevcut [stok görsel provenansı](music-refinements-0411.md): CC BY 2.0/3.0, CC BY-SA 3.0/4.0 ve CC0. Yerelde hazırlanan video/poster türevleri kaynak lisansı ve atfıyla ilişkilidir; yeni dataset benimsenmedi.
- [FFmpeg stream_loop, streamcopy, HLS/fMP4](https://ffmpeg.org/ffmpeg-all.html): 2026-10-23 Context7 ve gerçek çıktı ile kontrol edildi.
- [MDN DOM taşıma ve medya durumu](https://developer.mozilla.org/en-US/docs/Web/API/Document/moveBefore): aynı video/audio öğesinin DOM yeniden yerleştirmede durumunu koruması incelendi.
- HeroUI v3 Modal belgeleri ve kurulu 3.2.6 tipleri; mevcut portal/fullscreen sözleşmesi kullanıldı.

# Dil seçimi ve torrent başlangıcı — 0.27.1 geliştirme önizlemesi

Tarih: **2026-10-17**. Kartlar: **LOCALIZE-01 / CLI-05**, **TRANSFER-03 / DL-01/02, IDX-05/07**. Şema **39**.

## English / Türkçe

- Giriş ekranı ve hesap ayarlarındaki **Interface language / Arayüz dili** seçimi anında uygulanır. Varsayılan English; tercih tarayıcıda, Home alt yoluna göre saklanır ve diğer sekmelere bildirilir.
- Yerel i18next sözlükleri, 2.185 girdilik kaynak envanteri ve ek uygulama durum mesajları. Vite/Babel dönüşümü uygulamanın sabit ekran metinlerini, erişilebilir etiketlerini ve tarih/sayı yerelleştirmesini bağlar. Dinamik menü etiketleri dili yeniden okur; dil değişimi oynatıcıyı veya formları yeniden başlatmaz.
- Başlık/`html.lang` ve PWA'nın genel push/bağlantı mesajları seçilen dili kullanır. TMDB metadata ve altyazı tercihlerinin ayrı ayarları vardır. Sağlayıcı başlıkları, kullanıcı adları, dosya yolları, API kimlikleri ve kayıtlı medya verisi çevrilmez.
- Yeni mesajlar `web/src/i18n/extra.json` dosyasına Türkçe → English olarak eklenebilir. Envanterdeki sıra sabittir; `tools/i18n-inventory.mjs` yalnız gözden geçirilecek `artifacts/` çıktısı üretir.

## İki kullanıcı torrentinin teşhisi

- Torrents.csv kaydı `e49bb81e29454f88af1a8affcd5ddd95` kullanıcı tarafından duraklatılmış ve arşivlenmişti.
- YTS kaydı `9aa5a5878e2847ea83deea5824bf93f8` çalışır niyetle metadata aşamasında, **0 eş / 0 byte** bekliyordu. Hesap kotası sınırsız; standart ağ modu etkin.
- Yerleşik JSON kaynaklarının magnet oluşturucusu izleyici adreslerini eklemiyordu. Metadata zaman aşımı da worker tarafından sessizce yutulduğundan aynı iş sürekli yeniden açılıyordu.
- Bu makineden üç açık UDP izleyicisinin bağlantı el sıkışması **100/118/126 ms** ile yanıtlandı. Kullanıcının seçtiği YTS kaydının `.torrent` metadata'sı HTTPS **200 / 21.366 byte** döndü. Bu okumalar medya payload'ı veya yeni edinme başlatmadı.
- Kamuya açık YTS/EZTV/API Bay/Torrents.csv magnetleri izleyicilerle oluşturulur; eski izleyicisiz bağlı kayıtlar açılışta tamamlanır. Mevcut izleyici listeleri korunur; manuel/özel kaynaklara kamu izleyicisi eklenmez.
- Yerleşik YTS kaydı için korumalı kaynak taşıma katmanıyla bounded HTTPS metadata yolu eklendi. Sır/cookie aktarılmaz, yönlendirme izlenmez, 4 MiB sınırı ve seçilen infohash denetimi uygulanır. HTTP başarısızlığı DHT/izleyici çözümüne döner. Duraklat/iptal/eski revizyon metadata'yı yayımlayamaz.
- İki dakikalık metadata zaman aşımı artık açıklamalı **failed** sonucudur; kullanıcının yeniden-dene komutu gerekir.

## Odaklı doğrulama

- **40/40 backend, 0 atlanan:** yeni metadata/sır/hash/revizyon/zaman aşımı sınırları; kaynak normalizasyonu ve edinme; hesap bütçesi; yerel gerçek magnet/torrent byte aktarımı, hash ve idempotent import. `artifacts/test-results/torrent-resolution-v0271.trx`.
- **13/13 Node:** sözlük/placeholder kapsamı, dil değiştirme/saklama/alt yol, taşıma sabitlerinin ve kullanıcı metninin korunması, hook/controlled input yapısı; önceki sekiz push yaşam döngüsü kontrolü.
- Üretim frontend ve worker tip denetimi/derlemesi geçti. Son istemci: `/assets/index-BElOFZy3.js`; PWA: `2e48c73c60f1b64d`. npm audit: 0 açık.
- Windows imzalı paket: `artifacts/packages/preview-v0271/home-0.27.1-dev.1-win-x64.zip`; **648 doğrulanmış dosya / 74.222.657 byte**. SHA-256: `0817207a80f7680a980ec01ba8c62f654c6d12708230d848b58a495b84a74870`.

## Çalışan önizleme sonucu

- **http://localhost:5080**, **0.27.1 / şema 39**, PID **18976**, release `83538433bfb840d39b426300cb07a59a`. İmzalı güncelleme `08:51` yerel saatte uygulandı; sahip oturumu korundu. `artifacts/update-live-0.27.1-preview.json`.
- Güncelleme öncesi iki kaydın durumu `artifacts/preview-before-v0271.json` dosyasına alındı. Staging sırasında yalnız mevcut sıfır-byte metadata işinin çalıştığı yeniden kontrol edildi. Mevcut `running` niyeti normal sunucu kapanış/açılışıyla devam etti; ek medya edinme veya resume komutu gönderilmedi.
- **08:52:16** yerel saatte mevcut YTS kaydı `magnet → torrent → downloading` aşamasına geçti: **10 eş**, **2.558.464 byte/sn**, **55.296.000 byte alınan**, **%1,463 doğrulanmış ilerleme**, toplam **2.786.613.850 byte**. Bu gözlem internetten gerçek veri akışını doğrular; dosyanın tamamlanması/import/oynatma kabulü henüz bu gözlemle kapanmaz.
- Önceki istek, iki indirme kimliği/hash'i/sahibi/niyeti ve arşiv durumu korundu; şema 39 ve FK ihlali 0. Diğer Torrents.csv kaydı `paused / archived` durumundadır. Dil seçimini içeren yeni istemci ve PWA worker HTTP üzerinden sunuluyor. `artifacts/live-preview-fix-v0271.json`.
- Güncel otomatik geri dönüş **0.27.0**, yedek `716f80a29cb74bd68eda9102f37cfbef`. Önceki **0.26.0** release `32af901765ec42dc9d697fd6957910ec` ve yedek `630c38117af54d369728fbb54f58478a` korunuyor; önceki geri dönüş bilgisi güncelleme kanıtında ayrıca kayıtlı.
- Kullanım: **Ctrl+F5**, hesap ayarları → **Interface language / Arayüz dili → English / Türkçe**. Giriş ekranında da aynı seçim vardır.

## Başvurular

2026-10-17: Context7 kotası dolduğundan resmi belgeler ve kurulu tipler kullanıldı: [i18next configuration](https://www.i18next.com/overview/configuration-options), [React useTranslation bağlama yaklaşımı](https://react.i18next.com/latest/usetranslation-hook), [Babel parser](https://babeljs.io/docs/babel-parser), MonoTorrent `release-v3.0.2` ClientEngine kaynağı ve NuGet tipleri; [YTS API](https://yts.gg/api) ile mevcut kullanıcının seçtiği kaydın metadata adresi. i18next **26.4.7 / MIT** ve Babel **8.0.7 / MIT** lisansları [envanterde](dependencies.md).

Bu odaklı dilim **VERIFY-27** geniş regresyonunun veya kullanıcının tarayıcı/cihaz kabulünün yerine geçmez.

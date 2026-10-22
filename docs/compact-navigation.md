# Kompakt ana menü — 0.28.2 geliştirme önizlemesi

Tarih: **2026-10-18**. Kart **NAV-01 / CLI-01/05**. Kullanıcının uzun üst menünün sığmaması geri bildirimi. Şema **39**, protokol/veri modeli değişmedi.

## Yerleşim

- Birincil menü: **Keşfet, Kütüphane, Müzik**. Keşif izni olmayan profilde Keşfet gösterilmez.
- **Home** logosu ana sayfaya götürür; aynı hedef üstte ikinci tab olarak tekrarlanmaz.
- **Daha fazla → Medya:** Canlı TV, Raflar.
- **Daha fazla → Kişisel alan:** Takip listem, Birlikte, Cihazım.
- **Daha fazla → Sunucu:** İndirmeler (edinme izni varsa), Aktivite (yönetici). Boş/izinli olmayan grup gösterilmez.
- İkincil ekran seçiliyken Daha fazla aktif olur; menü içinde seçili hedef işaretlenir. Hedef ID'leri ve mevcut History API URL'leri değişmez. Gezinme, açık küçültülmüş player'ı veya lease'i kapatmaz.

## Uygulama

`navigationModel.ts` izin filtresi ve grupları tek yerden sağlar. `MainNavigation.tsx`, mevcut **HeroUI 3.2.6 Dropdown** / React Aria trigger, popover, menu/section ve selected indicator bileşenlerini kullanır. Menünün klavye/typeahead/Escape/dışarı tıklama/focus dönüşü davranışını yeniden yazmaz. Bir route değiştiğinde açık menü kapanır.

Üst başlık absolute ortalanmış geniş menü yerine kendi üç kolonlu yerleşimindedir. 1300 px altında sunucu durum metni sadeleşir; dar ekranda aynı tek menü dört veya izinlere göre üç hücreli alt dock olur. Diğer hedefler gruplu popover'da, gerekli durumda yukarı açılır/scroll edilir. Türkçe/English ve mevcut reduced-motion kuralları korunur. Browser/device kabulü kullanıcıdadır; screenshot polish veya browser suite çalıştırılmaz.

## Doğrulama

**20/20 Node**, 0 atlanan: primary sayısı/tüm hedeflerin tekilliği, discovery/acquisition/admin filtresi, ikincil aktif seçimi, gerçek grouped Dropdown bağlaması ve static header/dar-ekran CSS sözleşmesi; önceki hero kişisel kontroller, Home media bridge ve dil kontrolleri. Frontend/worker tip kontrolü, üretim build ve .NET publish geçti. İstemci `/assets/index-DcInirdC.js`, PWA `f34489f72c93debe`. İmzalı Windows paket `artifacts/packages/preview-v0282/home-0.28.2-dev.1-win-x64.zip`, **837 dosya / 74.808.616 byte**. Canlı yayın kanıtı aşağıya eklenir; tam ürün regresyonu değildir.

## Çalışan önizleme

2026-10-18 **18:56 yerel saat**: imzalı **0.28.1 → 0.28.2** tamamlandı; sahip oturumu korundu. **http://localhost:5080**, PID **17120**, release `1eb3ba86def9424ab04e73d0f38d3ad8`. HTTP kontrolünde compact-v1 menü istemcisi, aynı yeni build byte/hash'i, Video.js player, PWA ve doğrudan içerik/izleme sayfaları sunuluyor. Şema 39, FK ihlali 0; **7 medya / 2 istek / 2 indirme** ve mevcut kişisel favori/puan/koleksiyon verisi korundu. Oynatma/edinme komutu verilmedi; mevcut %100 seed restart doğrulamasından devam ediyor.

Kanıtlar: `artifacts/personal-before-v0282.json`, `artifacts/update-live-0.28.2-preview.json`, `artifacts/live-navigation-v0282.json`. Paket SHA-256 `15c602ef876eea9a7a00c2669f93cb771e68350dce71eea6275394af48e669f6`. Geri dönüş **0.28.1**, yedek `e7cf02407b224a49b1fdc0383bf396ac`; önceki release/yedekler korunur. Kullanıcı **Ctrl+F5** ile yenileyip menü/dock/klavye yerleşimini kendi cihazında kabul eder.

## 0.28.3 — ekran görüntüsündeki taşma düzeltmesi

Kullanıcının ekran görüntüsü üzerine **NAV-02 / CLI-01**: önceki 0.27 geniş menü hack'i `music-catalog.css` içinde `.navigation { overflow:auto } / nav { min-width:690px }` olarak kalmıştı. 0.28.2 kompakt component'in legacy `navigation` sınıfı bu kuralları miras aldı; capsule 717×69 px, nav min-width 690 px oldu. Yeni HTML/CSS yapısı artık yalnız kendi `home-navigation` sınıfını kullanır; eski iki genişlik/scroll kuralı kaldırıldı, reader/fotoğraf stilleri korunur.

Menü içerik genişliğine sarılır ve toolbar scrolling kullanılmaz. Açılır panel 250 px ve daha sakin tipografiyle More trigger'ına hizalanır. Dar ekran dock geçişi 720 px'e taşındı; ana içerik altında dock için alan ayrılır.

Mevcut kullanıcı tab'ında **1296×890** viewport'ta geçici CSS önizlemesiyle bar **386,44×54 px**, nav **clientWidth=scrollWidth=374**, min-width 0 ve overflow visible olarak ölçüldü. Popover 250 px; trigger ve popover sağ kenar farkı 0,02 px'den küçüktür. Kanıt `artifacts/navigation-layout-diagnosis-v0283.json`. Bu odaklı DOM/geometri incelemesidir; browser suite veya bütün cihaz kabulü değildir. Desktop görünür olmadığı için screenshot capture yapılamadı; screenshot geçtiği iddia edilmez.

**21/21 Node**, frontend/worker tip kontrolü, üretim build ve .NET publish geçti. Legacy sınıf/min-width/overflow çakışmasını engelleyen yeni regression kontrolü eklendi. İstemci `/assets/index-D17Klpaw.js`, PWA `d8ed0ecd2ddd70be`. Windows imzalı paket `artifacts/packages/preview-v0283/home-0.28.3-dev.1-win-x64.zip`, **837 dosya / 74.808.842 byte**. Canlı kayıt aşağıya eklenir.

**19:10 yerel saat:** imzalı **0.28.2 → 0.28.3** yayını tamamlandı; hesap korundu. PID **19768**, release `f46c668354f2405a8f6220f83d930594`. Yeni HTTP/compact-v2 istemci, player ve PWA doğrulandı; **7 medya / 2 istek / 2 indirme**, kişisel veriler ve şema 39/FK 0 korundu. Kanıt `artifacts/update-live-0.28.3-preview.json`, `artifacts/live-navigation-v0283.json`. Paket SHA-256 `c99835c4f95ab486c71783a290cb12f0e99cad639d41316c04aeb2a7220741f6`. Geri dönüş **0.28.2**, yedek `6e732dcba5454a6788c73f2c4e69bf55`.

Yayın sonrası kullanıcı tab'ı yeni istemciyle yeniden açılıp gerçek DOM/geometri kontrolü yapıldı: **1296×890** ekran, seçili Downloads göstergesiyle bar **398,44×54 px**; nav **clientWidth=scrollWidth=386**, overflow visible ve min-width 0. Legacy sınıf ve geçici önizleme stili yok. Popover 250 px ve sağ kenar farkı **0,004 px'den küçük**. Bu kontrol oynatma/edinme veya browser suite başlatmadı; bütün cihaz/görsel kabulünü kullanıcı yapar.

## Başvurular

2026-10-18 Context7 üzerinden [HeroUI v3 Dropdown migration](https://heroui.com/en/docs/react/migration/dropdown) ve kurulu `@heroui/react` dropdown/menu-section/menu-item tipleri/kaynakları okundu. Compound bileşenler, zorunlu Popover, Header/Label ve `selectedKeys`/`onAction` API'si kurulu sürüme göre kullanıldı. Yeni bağımlılık/lisans veya provider verisi eklenmedi.

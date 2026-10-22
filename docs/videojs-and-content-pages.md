# Video.js ve tam içerik sayfaları — 0.28.0 geliştirme önizlemesi

Tarih: **2026-10-18**. Kartlar **PLAY-03 / PLY-02/03/06/07/08, CLI-01/05**, **DETAIL-PAGE-01 / CAT-02/04/05/08, CLI-01**. Şema **39**, migration yok. Son tam ürün regresyonu hâlâ 0.26; bu dilim kapsamlı ürün kabulü değildir.

## Oynatıcı

Kullanıcının seçimiyle **Video.js 10.1.0 React** varsayılan skin, kontrol/menü, zaman çizelgesi, klavye/dokunma, fullscreen ve PiP katmanı kullanılır. Bütün kontroller tekrar Home tarafından yazılmaz; mevcut Home akış motoru ve izin/oturum modeli korunur:

```text
Home playback API → authenticated lease → usePlayback → hls.js / direct file
                                                   ↕
                         Home media/timeline/command facade
                                                   ↕
                                     Video.js store + skin
```

- `homeMediaBridge.ts`, Video.js'e toplam süre ve mutlak konumu verir. HLS penceresinin göreli konumu/duration'ı kullanıcı zaman çizelgesi değildir. Seek, backend'in yeni başlangıç akışını açabilmesine göre bütün süreyi kapsar; buffer/played aralıkları mutlak zamana çevrilir.
- Hazır ses menüsü gerçek Home stream index'lerini; altyazı menüsü gerçek track ID'lerini seçer. Metin altyazıları Home WebVTT kaynağında; bitmap/ASS stil seçimi mevcut burn-in API'sinde kalır. Mevcut dosya izleri dışında dublaj/altyazı uydurulmaz.
- Hazır kalite menüsü Home'un **1080/720/480 maksimum profil** seçimlerini backend'e gönderir. Menüdeki Auto burada **Özgün / otomatik uyum** olarak açıklanır; çoklu eşzamanlı ABR ladder uygulandığı iddia edilmez. Tek sunucu akışı, mevcut lease replacement ve konum korunumu kullanılır.
- Oda/uzak kumanda play/pause/seek/rate/volume komutları aynı Home izin kapısından geçer. Yetkisiz kontrol Video.js üzerinden native elemente ulaşmaz. Native medya kimliği `REGISTERED_MEDIA` ile korunur; fullscreen/PiP kimlik karşılaştırmaları facade yüzünden kırılmaz.
- Kaynak hazırlanırken play/pause niyeti korunur; bitmiş dosyanın tekrar oynatılması eski kapanmış lease'i kullanmaz. Ses/kalite/altyazı değişimi ayrı kullanıcı edinme komutu değildir.
- Video.js modülü ve skin yalnız oynatma gerektiğinde lazy yüklenir. Home kendi HTMLVideoElement ve HLS sahipliğini korur; Video.js'in transitif SPF/native-HLS paketleri yeni Home akış yolu olarak kullanılmaz. Mux analytics/hosted-service eklentisi kurulmadı.
- Altyazı yükleme/senkron/sağlayıcı, gecikme/burn-in, chapter/bölüm seçimi, skip tercihleri, receiver ve room yönetimi ayrı sayfa yan panelindedir. Müzik PCM/gapless oynatıcısı bu kullanıcı talebinin dışında korunur.
- `useMediaAttach` paketçe export edilse de internal olarak işaretlidir. Bu ince bağlama **10.1.0**'a sabitlenmiştir; bağımlılık yükseltirken kurulu tipler/attachment yaşam döngüsü yeniden kontrol edilmelidir. Video.js v10 yeni bir yeniden yazımdır; bu entegrasyonun bütün cihazlarda kabul edildiği iddia edilmez.

## Sayfa akışı

- Kütüphane, keşif, takip/favori kartları film/dizi için **`/title/{provider}/{movie|show}/{id}`** sayfasına gider. Katalogda olmayan/teknik dosya ayrıntısı **`/media/{id}`**; oynatma **`/watch/{id}`** adresindedir.
- Büyük backdrop/afiş, yapım/orijinal ad, özet, tür/süre/puan/sınıflandırma, gerçek oyuncu/ekip portreleri, Oynat/Devam et ve dosya durumu bulunur. Sezon/bölüm ve dosya sürümleri mevcut gerçek endpoint'lerden gelir. Yönetim/edinme alanları ana görünümden ayrı açılır bölümdedir.
- Popup içerik ayrıntısı ve modal video çerçevesi kaldırıldı. İşlem/kaynak/hesap dialogları aynı amaçla korunur.
- History API, adres yenileme/doğrudan bağlantı ve proxy alt yolu açıkça desteklenir. Geri/ileri gezinme scroll/focus kaydını kullanır; arama/filtre ekranı ayrıntı açıldığında unmount edilmez. Kütüphane/keşif kartları gerçek bağlantı taşır; Ctrl/Cmd ile yeni sekme davranışı korunur.
- Küçültme aynı video/lease'i küçük sabit oynatıcıda bırakır; genişletme izleme sayfasına döner. Kapatma normal cleanup ve kişisel progress akışıdır. Sayfa h1 ve document title yönetimi vardır; reduced-motion ve mevcut i18n korunur.
- Ayrıntı GET'i indirme başlatmaz. İzleme adresini kullanıcı açarsa mevcut medya playback API'si kullanılır. Backend account/library/parental/quota sınırları kaldırılmadı.

## Odaklı doğrulama

- **27/27 Node:** sekiz bridge/route kontrolü (kurulu gerçek Video.js feature store dahil), önceki beş dil, üç taşıma ve on bir oda durum kontrolü. Tam süre/offset, gerçek ses ve bitmap track ID'si, server quality seçimi, native kimlik, yetkisiz room komut kapısı, root/path-base ve geçersiz deep link sınırları doğrulandı.
- Nihai frontend/worker tip kontrolü ve üretim build geçti. İstemci `/assets/index-2jSiYA-1.js`, lazy player `/assets/Player-BIeOZbSl.js`, PWA `4d711eb51c8dfe97`. Video.js skin CSS ayrı chunk'tadır. npm audit: 0 açık. Mevcut büyük chunk uyarıları ve önceden derlenmiş React runtime'ın `use no memo` derleyici yönergesi uyarısı bastırılmadı; uygulama React Compiler çalıştırmaz.
- **26/26 backend/native, 0 atlanan:** direct/HLS codec ve gerçek kalite çıktısı, track/segment yetkisi/Range, iptal, eski komut/cache/slot, ortak hesap-cihaz kotası ve yaşam döngüsü. `artifacts/test-results/videojs-pages-v028.trx`. İlk paralel deneme, Vite'ın wwwroot'u yenilemesiyle eski static-assets girdileri çakıştığından build aşamasında durdu; test sonucu olarak sayılmadı. Nihai tur web build bittikten sonra çalıştırıldı.
- İmzalı Windows paketi `artifacts/packages/preview-v028/home-0.28.0-dev.1-win-x64.zip`; **837 dosya / 74.726.664 byte**. Yeni paket, önceki release yerinde değiştirilmeden uygulama update mekanizmasına verildi; canlı doğrulama sonucu aşağıya eklenir.
- Browser suite, yeni internet medya edinmesi veya otomatik kullanıcı oynatma kabulü çalıştırılmadı. Gerçek cihaz, seek/menü/fullscreen/PiP/room ve görsel kabul kullanıcıdadır.

## Çalışan önizleme

- **2026-10-18 18:20** yerel saatte imzalı **0.27.2 → 0.28.0** güncellemesi tamamlandı. **http://localhost:5080**, PID **9856**, release `3e1db09dbeed4a5bbee120f769ce34ec`; veri `.data/preview`. Sahip oturumu korundu.
- HTTP üzerinden yeni istemci/lazy Video.js player ve PWA worker aynı build byte/hash'leriyle doğrulandı. `/title/tmdb/movie/438631`, `/media/11ee851cbd6a40ae8c63f09131e365e8` ve `/watch/11ee851cbd6a40ae8c63f09131e365e8` doğrudan açılış shell'i sunuyor.
- Gerçek metadata **Dune: Çöl Gezegeni**; mevcut dosyanın API envanteri **1 ses / 0 altyazı / 18 chapter**. Yeni dublaj veya altyazı kaydı uydurulmadı. İzleme URL kontrolü yalnız HTML okudu; browser/oynatma başlatmadı.
- Güncelleme öncesindeki **7 medya / 2 istek / 2 indirme** kimliği, dosya boyutu/yolu, request owner ve download owner/hash/arşiv durumu korundu. Şema 39, FK ihlali 0. Tamamlanan mevcut torrent %100 seed durumundan devam ediyor; yeni edinme veya resume komutu verilmedi.
- Kanıtlar: `artifacts/preview-before-v028.json`, `artifacts/update-live-0.28.0-preview.json`, `artifacts/live-videojs-v028.json`. Paket SHA-256: `fc229a94cb33e3885e17882d96f142447abcb2bc0147f0c2e1ceca9ed70fbf52`.
- Geri dönüş **0.27.2**, yedek `d6e714b32ef44fe0b2d125647a9dce84`. Önceki release/yedekler silinmedi. Yetkili aktif seçim her açılışta `.data/preview/updates/active.json` dosyasından okunur.
- Kullanım: **Ctrl+F5**, film/dizi kartını aç → tam ayrıntı sayfası → Oynat/Devam et → Video.js izleme ekranı. Ses/altyazı/kalite seçimi player ayar menüsündedir; Home'un gelişmiş işleri sağ üst yan panelde bulunur. Gerçek browser/görsel/cihaz kabulü kullanıcıdadır.

## 0.28.1 — üst kişisel kontroller ve görünür oynatma ayarları

2026-10-18 kullanıcı geri bildirimi **DETAIL-PAGE-02 / CAT-05/08, CLI-01/05** olarak uygulandı. Film/dizi ve eşlenmiş dosya sayfalarında Favori/Puan/Koleksiyon bölümü hero'ya, Oynat/Devam et grubunun hemen yakınına taşındı. Tek kişisel component/hesap query/mutation yolu korunur; altta ikinci kopya yoktur. Kişisel puan/favori kaydının merge'i ve sadece sahip olunan koleksiyon filtresi değişmedi.

Oynatıcı sağ üst düğmesi **Ses, altyazı ve kalite** olarak görünür ad taşır; dar ekranlarda erişilebilir adı olan simgedir. Açılan panelin üstünde temel ses/dublaj, altyazı ve kalite seçenekleri vardır; aynı Home media command port'uyla Video.js menüsünü kullanırlar. Altyazı gecikmesi/yükleme/sağlayıcı/burn-in ve diğer işler aşağıdaki **Gelişmiş Home ayarları** bölümündedir. Tek ses izi veya altyazısız gerçek dosya bunu açıkça belirtir; yeni dublaj/altyazı varmış gibi seçenek oluşturulmaz.

**16/16 Node**: üç yeni placement/ayar erişimi/kişisel ownership kontrolü, sekiz gerçek Video.js store/bridge/route ve beş dil kontrolü. Frontend/worker tip kontrolü, üretim build ve .NET publish geçti. Windows imzalı paket `artifacts/packages/preview-v0281/home-0.28.1-dev.1-win-x64.zip` (**837 dosya / 74.730.228 byte**); istemci `/assets/index-Dm7ivN8M.js`, player `/assets/Player-D5sf3FkM.js`, PWA `86000f32192681b8`. UI/build dilimidir; protokol/DB şeması değişmedi. Browser suites çalıştırılmadı; yerleşim ve kullanıcı cihaz kabulü kullanıcıdadır.

**18:37 yerel saat**: imzalı **0.28.0 → 0.28.1** önizleme güncellemesi tamamlandı; sahip oturumu korundu. PID **13544**, release `c025f1d26c9e43ca81db373ae935e5f0`. HTTP üzerinden yeni istemci/player/PWA ve doğrudan sayfalar doğrulandı; **7 medya / 2 istek / 2 indirme**, şema 39, FK ihlali 0. Güncelleme öncesi iki kişisel favori/puan kaydı aynı değerlerle korundu; koleksiyonlar/üyelikler karşılaştırıldı (bu önizlemede 0 koleksiyon). Kanıtlar `artifacts/personal-before-v0281.json`, `artifacts/update-live-0.28.1-preview.json`, `artifacts/live-videojs-v0281.json`. Gerçek Dune dosyasında **1 ses, 0 altyazı, 18 chapter** mevcut. İnternet medya edinmesi veya oynatma komutu verilmedi.

Paket SHA-256 `46bc01f32f6acc359b257ddcf5f72954db5e614b8c517510979e56bb2e41c15c`. Geri dönüş **0.28.0**, yedek `b6dd292749ee4bf2a0b83428de299ee5`; önceki release/yedekler korunur. **Ctrl+F5** ile yeni yerleşim ve görünür sağ üst ayar düğmesi alınır.

## Başvurular ve lisans

2026-10-18 Context7 ve sürüm eşleşen kurulu tip/kaynaklar: [React kurulumu](https://videojs.org/docs/framework/react/), [quality](https://videojs.org/docs/framework/react/guides/quality), [captions](https://videojs.org/docs/framework/react/guides/captions), [skin](https://videojs.org/docs/framework/react/guides/customize-skins), [Vidstack geçişindeki API/sınır açıklamaları](https://videojs.org/docs/framework/react/guides/migrate-from-vidstack). Harici sayfalardaki agent/CLI komutları talimat olarak çalıştırılmadı. Paketler npm lockfile ile sabittir.

Video.js paketleri **Apache-2.0**, React compiler runtime **MIT**; lisans/notices `wwwroot/third-party/` içine kopyalanır. [Envanter](dependencies.md). Home dağıtım lisansı hâlâ açık karardır.

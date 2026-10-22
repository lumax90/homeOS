# Gruplu ayarlar ve kaldığın yere dönüş — 0.31 geliştirme

Tarih **2026-10-18**. Kartlar **SETTINGS-01 / NAV-03**, gereksinimler **CLI-01/05, CAT-02/05/08**. Şema **39**, migration/backend workflow değişikliği yok.

## Ayar menüsü

Avatar → **Hesap ve güvenlik** açılır. Tek, uzun ve iç içe liste yerine **13 izin filtreli kategori** vardır:

- **Kişisel:** hesap/güvenlik; dil/kontroller; oynatma; cihaz bağlama; indirme sınırları; bildirimler.
- **Sunucu:** kütüphaneler; kaynak/kalite; film-dizi bilgileri; altyazılar; hane/izinler; yedek/ağ/bakım.
- **Bilgi:** Home hakkında.

Desktop: sabit kategori sidebar'ı, ayrı kaydırılabilir içerik pane'i, yerel arama. Dar ekran: aynı HomeSelect popup'ı ile kompakt kategori seçimi; 13 kategoriyi yan yana dizen dock yok. Logout hem account panelinde hem desktop footer'ında bulunur.

Arama English/Türkçe başlık, açıklama ve keyword üzerinden kategori bulur; kullanıcının görmemesi gereken admin kategorilerini arama sonucu olarak da göstermez. Seçili panel search ile otomatik değiştirilmez: henüz kaydedilmemiş form korunur. Görülen paneller bu modal açık kaldığı sürece hidden olarak kalır, kapanınca hassas draft'larıyla unmount olur. Server içindeki yedek/network/update/encoding/advanced içerikleri de ayrılır; tüm uzun operasyon ekranı varsayılan olarak üst üste gösterilmez.

Var olan account/PIN/playback/limit/push/device-link ve admin kaynak/metadata/altyazı/hane/library/operation bileşenleri aynı çerçevede kullanılır; yeni servis veya veri yolu yoktur. Ana kategoriler arasında eski modalı kapatıp başka modal açma kaldırılır. Kütüphane ekleme/klasör seçme gibi gerçek alt işlemler child dialog olarak aynı bağlamın üstünde açılabilir. Kaydet/test/backup/restart gibi komutlar kullanıcı onayıyla aynı endpoint/CSRF/role sınırından geçer; panel açmak edinme/test gönderimi başlatmaz.

Avatar account'a, TMDB bağlantı kısayolu metadata'ya, kaynak kısayolu sources'a açılır. Cihaz bağlantı yardım metni yeni kategori yolunu gösterir. Keyboard tablist ve gamepad sidebar/pane odak yönetimi vardır; controller Back pane'den nav'a, sonraki Back dialog dışına döner. Yeni bağımlılık yok; kurulu HeroUI 3.2.6 Modal/Button/Select tipi ve [v3 modal dokümanı](https://heroui.com/en/docs/react/components/modal) 2026-10-18'de Context7 ile okundu.

## Listeye dönüş neden bozuluyordu?

Eski history helper'ı bir kere RAF'ta sadece y + ilk eşleşen `data-home-focus` restore ediyordu. Gizlenen discovery DOM'unda ResizeObserver **0 genişliği 2 kolona** çeviriyordu; geri açılınca tekrar gerçek kolon sayısına dönmesi layout'u değiştiriyordu. Aynı film trend/popüler/genre raflarında aynı media key'i kullanabildiğinden global ilk focus eşleşmesi yanlış/top raftaki karta gidebiliyordu. Yatay rail scroll hiç snapshot edilmiyordu.

## Yeni dönüş sözleşmesi

- `home-page-v2` history entry'si: viewport x/y, scoped media focus, görsel anchor'ın viewport top offset'i ve en fazla 64 bölgenin x/y scroll'u. V1 `homeScroll/homeFocus` okunur, bu alanlar compatibility için yazılmaya devam edilir.
- Forward, Back ve browser Forward'da entry/path memory korunur; normal scroll bounded debounce ile güncellenir. Modal scroll lock/secret form alanları snapshot olarak alınmaz. History'de credential/form değerleri tutulmaz.
- Rail ID canonical provider/kind/collection/genre/query'den, personal rail ID kind/shelf'ten oluşur. Aynı movie key farklı rail içinde ayırt edilir; focus locator region içinde çözülür. Controller focus memory aynı region ayrımını kullanır.
- Gizli/0-width discovery'de kolon sayısı değiştirilmez. Dönüşte rail offset → anchor-aware viewport → scoped focus restore edilir. Cache/lazy/height değişimleri birkaç frame/observer ile takip edilir; 300ms sakinlik ve en fazla 4sn deadline.
- `preventScroll`u yok sayan browser focus hareketi sonrasında istenen viewport tekrar uygulanır. Missing/filtered content sonsuz retry üretmez. Mouse/touch/keyboard/gamepad gerçek niyeti geri yüklemeyi derhal iptal eder.
- Restore sırasında otomatik controller focus repair bekletilir; kullanıcı komutu iptal ederek kontrolü alır. Yeni route eski restore frame'ini senkron iptal eder; StrictMode cleanup hedef snapshot'ı erken tüketmez.

`pagePosition.ts` reader ve restore engine DOM host'tan ayrıdır; bounds, legacy, delayed layout/height, scroll+focus sırası, interruption/deadline, scoped duplicate movie ve native focus scroll için izole unit kontrolleri vardır. Gerçek browser/device acceptance kullanıcıdadır; otomatik browser suite veya internet medya edinmesi yapılmaz. Final test/build/imzalı preview ve canlı salt-okunur veri/shell kanıtları aşağıya eklenir. VERIFY-27 geniş regresyonu açık kalır.

## Final build — 2026-10-18

**91/91 Node, 0 atlanan**, 10 yeni settings/history/position kontrolü dahil. Frontend-worker tip kontrolü, Vite üretim build ve .NET publish başarılı; npm audit 0 açık. Kanıt `artifacts/settings-navigation-v031.tap`. Bu UI-only dilimde geniş backend regresyonu yapılmadı; son tam regresyonlu sürüm 0.26.0 ve VERIFY-27 açık kalır.

İstemci `/assets/index-D_y9WjLF.js`, CSS `/assets/index-DhwuHuDg.css`, player `/assets/Player-C6R08zb2.js`, PWA **f9f84e84deccd717**. İmzalı Windows paket `artifacts/packages/preview-v031/home-0.31.0-dev.1-win-x64.zip`: **837 doğrulanmış dosya / 74.900.117 byte**, SHA-256 **fe2093cab88c47e47c74da7d6a8fe683b0fa3fd84ba68d1f19c3c229e26aa5c5**. Canlı yayın/veri okunumu aşağıya eklenir; gerçek browser settings/back kabulü kullanıcıdadır.

## Çalışan önizleme

**2026-10-18 23:01 yerel saat**: imzalı **0.30.1 → 0.31.0** tamamlandı, sahip oturumu korundu. **http://localhost:5080**, PID **17236**, release `2350ad46c8a044f28f7062cea7bafb5f`. Canlı HTTP'de organized-v1 ayar çerçevesi, home-page-v2/scroll-region restore kodu, önceki HomeSelect/controller/loading katmanı sunuluyor.

Şema 39, FK ihlali 0; **7 medya / 2 istek / 2 indirme / 5 kişisel kayıt** ve metadata **en-US** tercihi korundu. YTS paused kaldı. Doğrulama oynatma/edinme komutu vermedi ve browser tab'ında kullanıcı adına settings/back tıklaması yapmadı; gerçek kullanım kabulü kullanıcıda açık kalır.

Kanıt `artifacts/preview-before-v031.json`, `artifacts/update-live-0.31.0-preview.json`, `artifacts/live-controls-v031.json`. Rollback **0.30.1**, backup `f707cf3c6106466faa1093feb6b38394`; önceki release/yedekler tutuldu. Yeni istemci **Ctrl+F5** ile alınır. Focus/scroll/setting değişikliği browser/device kabulü tamamlanmış ilan edilmez.

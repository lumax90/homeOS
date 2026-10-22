# Discovery yatay sayfa taşması — DISC-05

Tarih **2026-10-18**, hedef **0.30.1 / şema 39**. Gereksinimler **CAT-02, CLI-01**. Kullanıcı: keşifte aşağı kaydırırken sayfa altında yatay scrollbar çıkıp kayboluyor ve uzunluğu değişiyor; dikey scroll normal.

## Gerçek teşhis

Çalışan **0.30.0** kullanıcı tab'ında ölçüldü: viewport `clientWidth=1431`, içerik genişliği **1335**, grid **7 × 177px + 6 × 16px** ve normal iki satırdı. Buna rağmen document `scrollWidth=1723` oldu. Rail'lar doğru container genişliğindeydi; uzun yatay içerikleri kendi `overflow-x:auto` alanında kalıyordu. Dışarı taşan öğeler en alttaki deferred results grid'in loading skeleton'larıydı.

İkinci animasyon fazında loading öğelerinin `transform: matrix(1,0,0,1,287.181,0)` ile sağa kaydığı görüldü. Home kaynak CSS'i `.skeleton { animation:skeleton ... }` ve `@keyframes skeleton { background-position... }` kullanırken, kurulu **HeroUI 3.2.6** aynı global adı kendi shimmer pseudo-element'inde `transform:translateX(200%)` için kullanıyor (`node_modules/@heroui/styles/dist/themes/shared/theme.css:149`). Çalışan production CSSOM'da kalan/winning `skeleton` keyframe'i bu transform'du. Böylece Home kartının arka planı değil **kartın tamamı** yatay hareket ediyordu.

Reversible tek CSS izolasyonunda farklı isimli background-only keyframe kullanınca skeleton `transform=none`, grid `client/scroll=1335/1335` oldu; rail **1335/7318** aynı kaldı. Geçici style `finally` ile kaldırıldı; scrollbar gizleme override'ı bırakılmadı. Kanıt `artifacts/discovery-overflow-diagnosis-v0301.json`. Bunlar hedefli canlı DOM ölçümleridir, screenshot veya otomatik browser suite değildir.

## Düzeltme

- **11 Home loading sitesi** `.home-skeleton`, animasyon **`home-skeleton-shimmer`** olarak namespace edildi: discovery rail/grid, Home/library, activity/downloads, details/tracking/calendar.
- Home animasyonu yalnız `background-position` değiştirir; geometry/transform yoktur. HeroUI'nin kendi `.skeleton` ve `skeleton` pseudo-element animasyonuna dokunulmaz.
- Placeholder boyut/aspect-ratio ve grid kolonları aynı; rail horizontal scroll, lazy loading, mouse/gamepad focus ve dikey sayfa akışı değiştirilmez. Root `overflow-x:hidden/clip` eklenmez.
- Mevcut reduced-motion kuralı bu background animasyonunu da kapsar. Gelecekte Home animasyon/class adları generic library isimleriyle paylaşılmamalıdır.

## Kontroller

İki yeni regression testi isim/class ayrımı, background-only frame, kurulu HeroUI transform frame'i, bütün 11 call site ve rail scroll/root-clipping yokluğunu doğrular. Final Node/build/paket ve canlı yayın/DOM ölçümü aşağıya eklenir. Browser/device kabulünü kullanıcı yapar; internet medya edinmesi yoktur. Bu CSS-only dilim şema/business logic veya VERIFY-27 geniş regresyon durumunu değiştirmez.

## Final build — 2026-10-18

**81/81 Node, 0 atlanan** (2 yeni namespace/rail regression); frontend-worker tip kontrolü/production build ve .NET publish başarılı, npm audit 0 açık. Kanıt `artifacts/discovery-overflow-v0301.tap`. Geniş backend regresyonu bu CSS-only dilimde çalıştırılmadı; son tam regresyon 0.26.0 olarak kalır.

İmzalı paket `artifacts/packages/preview-v0301/home-0.30.1-dev.1-win-x64.zip`, **837 dosya / 74.884.232 byte**, SHA-256 `107270d5d12fed561d98d55df902b84d896b590d89c21e9a6536f9bf6576b99a`. İstemci `/assets/index-DdwifuMg.js`, CSS `/assets/index-gkCUjSGa.css`, PWA **ac26dff7d25ec833**. Canlı update/veri/DOM sonucu aşağıya eklenir; kullanıcı/device kabulü açık kalır.

## Çalışan önizleme

**2026-10-18 22:12 yerel saat**: imzalı **0.30.0 → 0.30.1** tamamlandı, sahip oturumu korundu. **http://localhost:5080**, PID **19796**, release `a03bcb46bf65465bb8ec2edd4770f48f`. HTTP üzerinden namespaced Home loading CSS/istemcisi ve önceki ortak kontrol/gamepad katmanı sunuluyor. Şema 39, FK ihlali 0; **7 medya / 2 istek / 2 indirme / 5 kişisel kayıt** korundu. Edinme/oynatma komutu verilmedi; YTS paused kaldı.

İlk çalışma snapshot'ında metadata dili tr-TR iken kullanıcı çalışma sırasında tercihini en-US'a çevirmişti. **Uygulama öncesi gerçek backup** `44a885bd4aab4ff89131dda1322d2a26` içindeki DB'nin zaten **en-US** olduğu salt-okunur kontrol edildi; güncelleme sonrası aynı tercih korundu, geri çevrilmedi. Kanıt `artifacts/metadata-preapply-v0301.json`. Bu nedenle metadata korunumu ara snapshot yerine pre-apply backup baseline ile doğrulandı.

Kanıt `artifacts/update-live-0.30.1-preview.json`, `artifacts/live-controls-v0301.json`, `artifacts/preview-before-v0301.json`. Rollback **0.30.0**, yedek `44a885bd4aab4ff89131dda1322d2a26`; eski release/yedekler tutuldu. Kullanıcı tab'ı bu sırada `/home` sayfasına geçtiği için tekrar discovery'ye yönlendirilmedi veya zorla yenilenmedi. Yeni istemcide post-reload DOM/screenshot kabulü ilan edilmez; **Ctrl+F5 sonrası kullanıcı scroll kabulü** açık. Önceki canlı cause/isolation DOM ölçümleri ve 81 Node/build kanıtı geçerlidir.

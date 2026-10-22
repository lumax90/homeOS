# 0.27 önizleme yayını — 2026-10-17

**Sonraki güncellemeler:** Çalışan önizleme **0.31.1 / şema 39**. [English/Türkçe ve torrent başlangıcı](localization-and-torrents.md) 0.27.1'de, [Chromium HLS düzeltmesi](playback-hls-fix.md) 0.27.2'de, [Video.js ve tam içerik/izleme sayfaları](videojs-and-content-pages.md) 0.28'de, üst kişisel kontroller/görünür ayarlar 0.28.1'de, [kompakt menü ve taşma düzeltmesi](compact-navigation.md) 0.28.2/0.28.3'te, [UI genre dili/raflı keşif](discovery-shelves.md) 0.29'da, [ortak dropdownlar](shared-selection-controls.md) 0.29.1'de, [gamepad/kumanda gezinmesi](controller-navigation.md) 0.30'da, [discovery loading taşması düzeltmesi](discovery-overflow.md) 0.30.1'de, [gruplu ayarlar/kaldığın yere dönüş](settings-and-return-navigation.md) 0.31'de ve [zorunlu UI dil denetimi/sabit ayar frame](ui-language-and-settings-frame.md) 0.31.1'de yayımlandı. Aşağıdaki 0.27.0 yayını tarihsel kayıttır; etkin release için `.data/preview/updates/active.json` okunmalıdır.

Kullanıcının kaynak ekranında eski Torznab/Newznab formunu görmesi üzerine çalışan önizleme güncellendi. **http://localhost:5080** artık **0.27.0 / şema 39** çalıştırır; kaynak kod/paket etiketi **0.27.0-dev.1**'dir. Geniş ürün regresyonu ve gerçek cihaz/hesap kabulü açıktır; bu yayın geliştirme önizlemesidir.

## Kullanım

Sayfayı **Ctrl+F5** ile yenile. **Kaynaklar → Site seçerek kaynak ekle** üzerinden hazır siteyi seç, gerekiyorsa API anahtarını gir, **Bağlantıyı test et → Kaynağı ekle** adımlarını tamamla. Genel Torznab/Newznab formu **Gelişmiş** sekmesindedir. Keşif tür filtresi de bu istemcide bulunur.

## Veri ve yayın doğrulaması

- Mevcut önizlemede **1 istek / 1 içerik**, 0 kaynak, 0 indirme ve 0 aktif oynatma tespit edildi. Yedek: `a1775a06e55f44eb919c0e8fcc6a3488`; kayıt `artifacts/preview-before-v027.json`.
- Gerçek yedek, ayrı geçici dizinde **30→39** taşındı. **111 eski tablonun 103 satırı**, tüm eski sütun değerleri, anahtarlar ve yönetilen yan dosyalar korundu; bütünlük ve FK kontrolleri geçti. Bu kontrol herhangi bir worker veya medya işi başlatmadı: `artifacts/preview-migration-v027.json`.
- Ayrı yerel süreç/veri ortamında imzalı **0.26.0 → 0.27.0 → 0.26.0** güncelleme/geri dönüş geçti; hesap korundu, restore eski oturumları iptal etti: `artifacts/update-roundtrip-0.26.0-0.27.0.json`.
- Çalışan önizleme imzalı paketle yükseltildi; sahip oturumu korundu: `artifacts/update-live-0.27.0-preview.json`.
- Canlı HTTP/API/DB kontrolünde **12 kaynak profili**, yeni kaynak sihirbazı ve tür filtreli istemci sunuluyor. Önceki **1 istek / 1 içerik kimliği** korunuyor; şema 39, FK ihlali 0: `artifacts/live-sources-v027.json`.
- Canlı mevcut oynatma/kota/oda/offline/VAD sözleşmesi geçti; 0 indirme/aktif oynatma ve doğrulamanın başlattığı 0 medya işlemi: `artifacts/live-runtime-v027-preview.json`.
- Frontend/worker üretim derlemesi ve worker sözdizimi geçti. PWA build `a31eeb5a8e4325b1`; ana istemci `/assets/index-CfORjz7H.js`.

## Paket ve geri dönüş

- Yalnız Windows x64 önizleme paketi üretildi: `artifacts/packages/preview-v027/home-0.27.0-dev.1-win-x64.zip`.
- Paket manifest/runtime sürümü sayısal **0.27.0**; 645 dosya, 73.984.074 byte. SHA-256: `fbcc45c40382c52e0de381f565aeb4b3082a32310145ba26c840480f0c0c3fb8`.
- Mevcut yerel güvenilen imza parmak izi: `b6ebb7e0a2b2821afd3143dc3bd9c8b666189d9a41953aa6657cf60d789b566b`.
- Etkin release: `.data/preview/updates/releases/4ec26de0bac6411eadc7afa8ed4b8c74`; PID son kontrolde **19952**, veri `.data/preview`. Sonraki işlemde yetkili seçim `.data/preview/updates/active.json` dosyasından okunmalıdır.
- Canlı geri dönüş **0.26.0**, release `32af901765ec42dc9d697fd6957910ec`, güncelleme öncesi otomatik yedek **`630c38117af54d369728fbb54f58478a`**.

Son tam regresyon **0.26'nın 326 backend/native + 40 Node** turudur. **0.27'nin 42 odaklı kaynak/entegrasyon kontrolü ve ayrı 8 metadata/API kontrolü**, tam ürün testi veya kullanıcı kabulü olarak sayılmaz. Kullanıcının kaynak seçimi ve medya edinme komutu verilmeden internet medya aktarımı yapılmadı.

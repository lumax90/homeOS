# 0.14 — Kütüphane yaşam döngüsü ve yerel metadata

Başlangıç: **2026-10-13**, **0.14.0 yayımlandı; backend/native doğrulandı**. CAT-03/05/09, IMP-07.

Kök düzenleme, arşivleme/geri bağlama, göreli klasör dışlamaları, periyodik ve debounce edilmiş dosya izleme. Tamamlanmayan/erişilemeyen tarama dosya kayıtlarını silmez. Ayar değişikliği eski taramayı iptal eder.

Yerel NFO ve afişler açık kütüphane tercihiyle okunur. XML dış varlıkları çözülmez; yerel görseller aynı kök içinde ve yalnız JPEG/PNG/WebP'dir. Kaynak dosyaya yazılmaz. Kullanıcının manuel eşleştirmesi/yerel alanı sağlayıcı yenilemesine üstün kalır.

Otomatik metadata eşleştirmesi ayrı opt-in kalıcı iştir: doğrulanmış NFO sağlayıcı ID'si veya tekil tam başlık/yıl sonucu; belirsizlik kullanıcı incelemesine kalır. Windows aynı-volume dosya kimliği yeniden adlandırılan dosyanın kaydını ve izleme geçmişini korumak için kullanılabilir; bilinmeyen/kopyalanmış kimliklerde tahmini birleştirme yapılmaz.

Kaynak kontrolü 2026-10-13: Jellyfin/Kodi NFO adlandırma ve alan sözleşmesi (`jellyfin.org/docs/general/server/metadata/nfo/`), Win32 `GetFileInformationByHandle` / `BY_HANDLE_FILE_INFORMATION` ve `GetFileInformationByHandleEx(FileIdInfo)`. Home parser'ı ve iş akışı bağımsızdır; uygulama kodu kopyalanmaz. 128-bit volume/file-ID ve yaratılma zamanı birlikte kullanılır; ID yeniden kullanımı/çakışması veya birden fazla olası kayıt olduğunda birleştirme yapılmaz.

Dışlanan dosyalar ayrı `excluded` bayrağıyla erişim/sayımlardan çıkarılır; kayıtları silinmez. Uygulama verisi/staging ve tanımlı sentetik demo kökü geniş bir kullanıcı klasörü altında olsa da tarama dışında tutulur. NFO XML dış varlıkları reddedilir; görsel erişimi aynı köke sınırlandırılır. Başarısız NFO yenilemesi önceki geçerli metadata'yı korur.

## Sonuçlar

- **145/145 test, 0 atlanan**, frontend build ve imzalı Windows paketi geçti.
- Windows dosya/klasör rename kimliği, kişisel konum ve manuel eşleştirme; dışlamada kayıt koruma, archive/rebind; Türkçe NFO/yerel afiş, bozuk XML sonrası eski metadata; uygulama staging'inin üst kök taramasından dışlanması; manual-match yarışının korunması; opt-in NFO kimliğinin gerçek adaptör sözleşmesinden çözümü; native FileSystemWatcher debounce kuyruğu doğrulandı.
- Yönetici klasör gezgini ve kütüphane kök/tarama ekranları eklendi. Aktif izleyici ilk açılışta tam tarama planlar; sonraki olaylar 2 saniye sessizlik veya 30 saniye üst sınırla kalıcı tarama kuyruğuna alınır. Periyodik tarama 15 dakika–7 gün arasında, kapalı varsayılandır.
- NFO ve otomatik eşleştirme ayrı açık tercihlerdir. Otomatik seçimde TMDB NFO ID'si veya tekil normalize tam başlık/yıl kullanılır; dizide açık sezon/bölüm gerekir. Kullanıcının kaldırdığı/değiştirdiği eşleşme sessizce geri alınmaz.
- Canlı yayın 0.14.0, DB şema 15; `artifacts/live-runtime-v014.json`: yalnız eski sentetik kütüphane yönetim arşivinde (enabled=false), normal transfer ve otomatik eşleştirme sayısı 0. Geri dönüş 0.13.0 + `e5b6bc350f82476eb1088b5740e5d2c9.zip` yedeği.

Linux/ARM64 dosya-ID yeniden bulma, cross-volume hash eşleme ve bütün NFO lehçeleri bu doğrulamanın kapsamı değildir. Standart yol kimliği diğer platformlarda korunur; kullanıcı tarama/klasör gezgini kabulü açık.

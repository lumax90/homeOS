# Oyunlar — ilk çalışan kaynak dilimi

Tarih **2026-10-19**. Kaynak/package hedefi **0.32.0-dev.1**, runtime **0.32.0**, SQLite **40**. Gereksinimler **GAM-01/02/03**, **CLI-01/05**, **USR-02**, **OPS-03**. Son tam regresyonlu yayın **0.26.0**; VERIFY-27 ve gerçek cihaz kabul kapıları kapanmadı.

## Kullanıcı onayı ve gerçek kapsam

Kullanıcı tek oyun kütüphanesi, oyun merkezli giriş ve sonradan yürütme seçimini benimsedi; ardından Gaming sayfasının hazırlanmasını istedi. Music'in sağında **Games/Oyunlar** vardır. Steam ve ROM edinmesi kapsam dışıdır. İlk dilim, pakete dahil **Tiny Crate Classic** ve **Wrap It Up** browser build'leridir. Fake oyun/poster, ticari ROM/BIOS, runtime internet edinme veya dış oyun hesabı yoktur.

- Ortak kütüphane, gerçek kaynak görselleri, arama, kişisel kütüphane/favori filtreleri ve devam rafı.
- `/games`, `/games/{id}`, `/games/{id}/play` adresli sayfalar; mevcut v2 history/scroll/focus sözleşmesi kullanılır.
- Detayda gerçek Oyna/Devam et, kütüphaneye ekle/çıkar ve favori. Yalnız çalışan browser yolu gösterilir; sahte server/emulator seçim kartları yok.
- Oyuncu içinde loading/hata, Home pause menüsü, frame focus, tuşları bırakmadan resume etmeme, save-and-exit ve açık save failure/discard seçimi.
- **Sunucuda emülasyon/yayın, EmulatorJS, ROM ve kullanıcı web paketi import'u uygulanmadı.** Native TV/mobile uygulaması ve bütün-TV uyumluluğu iddiası yok.

## Paketlerin gerçek kaynağı ve tekrar üretimi

Kaynaklar ve lisanslar [araştırmada](browser-games-research.md) ve [envanterde](dependencies.md) kayıtlı. `tools/build-browser-games.ps1` resmi SHA-doğrulanmış Godot 3.6.1 editor/template ile pinned source commit'lerini web build'e çevirir. Bunlar uygulama/game runtime kaynak bağımlılıklarıdır; geliştirme doğrulaması bir internet medya/ROM edinme komutu başlatmadı.

- Tiny Crate **Classic** source commit: `c9b835c46ac5a98cf2aaaa0ee824378af14ff003`, Unlicense.
- Wrap It Up: `ac8bb8d0586e49cfd7a1b0039ce540b6fdda3f1a`, MIT.
- Single-thread Godot/WASM + GLES2/WebGL yolu; browser memory/decoder/GPU performans garantisi değildir.
- Kaynak font koleksiyonları dahil edilmez; actual font references SHA-doğrulanmış Apache-2.0 Roboto'ya çevrilir. Oyun geliştirici atfı korunur.
- Tiny Crate'te editor-only tool setter'ının runtime autoload okumasına `Engine.editor_hint` guard'ı eklenir; gameplay davranışı değiştirilmez.
- İlk export import cache'ini tamamlar. Yayımlanacak fresh-process ikinci export'ta script/resource ERROR kabul edilmez. Warmup mesajları çalışma klasöründe ayrı tutulur.
- Home'un `HomeBridge.gd` adapter'ı, external JS/HTML/CSS kabuğu ve bütün asset SHA-256 manifesti paketlenir. Orijinal kaynak build'i ile farklılık açıkça etiketlidir.
- Her pakette Game/Godot/Godot-third-party/Roboto license dosyaları vardır. Editor/native export templates Home runtime'a dahil edilmez.

## Yetki ve aktif kod sınırı

`GameCatalog` yalnız yerel, denetlenmiş `GamePacks/` manifestlerini okur. Bu dizin `wwwroot` değildir. İzinli dosya listesi, boyut, hash ve link/path sınırları doğrulanır; stream açıldığı anda açık file handle üzerinden hash tekrar kontrol edilir. Katalog/provider/user data taklit edilmez.

Oyun açma authenticated + CSRF + expected user/login header'ıyla olur. Runtime biletinin SHA-256 hash'i DB'de tutulur; bilet readonly package assets için geçerli ve login/profile/version/expiry'ye bağlıdır. Bilet API mutation yetkisi değildir.

`/game-runtime/{session}/{ticket}/{asset}` oyun dosyaları:

- HTML hem iframe `sandbox="allow-scripts"` hem CSP sandbox ile **opaque origin** kullanır; `allow-same-origin` verilmez.
- CSP default none; script/connect/resource kaynakları yalnız bu session'ın runtime asset URL'si. API, dış host, form, top-navigation, popup veya cookie/storage erişimi açılmaz.
- Opaque frame için credential-free CORS `*`, no-store ve no-referrer; authenticated API'ye CORS açılmaz.
- Gamepad izin delegasyonu açık; bu gerçek TV desteğinin kanıtı değildir.
- Parent mesajları exact iframe `contentWindow`, opaque origin ve random nonce üzerinden doğrular. Frame parent mesajlarında exact source/origin/nonce kontrol eder. Dar protocol; arbitrary eval/process/file komutu yok.
- Godot IndexedDB persistence kapalı; parent'tan verilen bounded checkpoint, frame'in memory filesystem'ine hydrate edilir. Home dışında key/resource dosyası veya arbitrary path yüklenmez.
- Input gameplay aktifken `ControllerRuntime.acquireGameplay()` ile nav poll/keydown/focus repair durur. Pause menüsünde Home gezinmesi geri gelir. Native gamepad okuması emülatörün değil oyun engine'inin browser sürücüsündedir.

Gamepad için secure context ve cihaz/OS mapping gerekir. Ses/fullscreen için trusted user activation gerekebilir. Bu sınırlamalar UI'de saklanmaz.

## Profil, kayıt, oturum ve migration

Şema 40, `game_user_state`, `game_sessions`, `game_saves` ekler; medya/istek/indirme tablolarına yazmaz.

- Library/favorite/history kullanıcıya aittir. Kütüphaneden çıkarma save silmez.
- Checkpoint anahtarı **user + game + build**; incompatible build verisi başka build'e verilmez ve önceki build save'i korunur.
- Aynı kullanıcı için tek aktif oyun oturumu. Yeni oturum eski ticket/writer'ı kapatır. Start aynı client/ticket ile idempotent.
- Heartbeat session süresini uzatır. Revoked/expired login veya profil versiyonu eski ticket'ı geçersiz kılar.
- JSON save allowlist, max depth/size, CAS revision ve aynı payload retry davranışı açık. Eski farklı payload yeni kaydın üzerine yazamaz.
- Client bounded queue tek writer kullanır; frame mesajlarını coalesce eder, başarısız veriyi retry için tutar. Exit önce frame flush ve server save sonucunu bekler; başarısızsa açık uyarı/discard.
- Tiny Crate: tamamlanan bölümler/kayıt yuvaları; mid-level replay veya tam save state aktarılmaz.
- Wrap It Up: bölüm ve map-order checkpoint'i; resume bölümü yeniden başlatır, frame-perfect snapshot değildir.
- Reopen/restart kalıcı JSON verisini okur; SQLite online backup doğal olarak game state/save'i kapsar. Güncelleme/rollback açık oyun session'ını reddeder.
- Unrated games, maximum-age + allow-unrated profil koşulunu kullanır; uydurma yaş derecesi atanmaz.

## Doğrulama ve yayın durumu

Odaklı backend: **24/24, 0 atlanan** (`GameTests`, `MigrationTests`, `NetworkTests`); kanıt `artifacts/test-results/games-boundaries-v032.trx`. CSRF/expected profile, opaque CSP, readonly asset Range/HEAD, ticket kapama/revoke, kaynak hash/path, version-isolated saves, CAS ve 39→40 repeat migration veri korunumu test edildi.

Node input/navigation/localization/game akışlarında ilk odaklı tur **43/43** geçti. `game-packages.test.mjs` gerçek shipped WASM/PCK/hash/lisans/font inventory'sini ayrıca kontrol eder. Bunlar **gerçek browser/GPU/gamepad/oyun kabulü değildir**. Final toplu sonuç ve paket/live kanıtı aşağıya eklenecek.

Önizleme ön kontrolü: 0.31.1 / schema 39, **0 aktif medya oynatma**, transferler kullanıcı-paused; backup **0f5f5c02a5ed43f18eab85a51ad4832c**. `artifacts/preview-before-v032.json`. Hiçbir indirme duraklatılmadı veya devam ettirilmedi. Canlı yayın öncesi durum tekrar kontrol edilir.

**Kullanıcı kabulü:** [ACCEPT-32-GAMES](product/acceptance.md). Browser/device suites veya screenshot polish çalıştırılmadı. Oyun görüntü/ses, gerçek TV/controls, devam ve sustained play sonucu henüz kullanıcıdan gelmedi.

### Final kanıt — 2026-10-19

- **95/95 Node, 0 atlanan**: oyun filtre/route/protocol/neutral/resume/save queue, gerçek WASM/PCK/hash/font/lisans envanteri ve mevcut controller/navigation/settings/localization/discovery/player testleri. `artifacts/gaming-v032.tap`.
- **24/24 backend, 0 atlanan**, `artifacts/test-results/games-boundaries-v032.trx`. Gerçek pre-update yedekte ayrıca **142 tablo / 4.379 satır**, bütün eski kolon/satır hash'leri, keys/subtitles ve integrity/FK 39→40 geçişinde korundu: `artifacts/gaming-migration-v032.json`.
- Mandatory UI audit **100 kaynak dosyası / 0 uncovered**: `artifacts/gaming-i18n-v032.json`. Frontend + worker type checks, Vite production ve .NET publish geçti; `artifacts/gaming-build-v032.log`. PWA **fe3542b3aaa7c7d4**.
- İki oyunun fresh-process clean export'ları hata üretmedi: `artifacts/games/build-tiny-crate-b8b6b26e48d347ebb77599757f0fe8b2/export-error.log`, `artifacts/games/build-wrap-it-up-c6481ae964c74853a39beeef603dbeb4/export-error.log`. Shipped manifestlerde **14'er doğrulanmış dosya**, Tiny Crate **30.200.746 byte**, Wrap It Up **49.345.099 byte**; yaklaşık 20-MB WASM engine her pakette vardır ve tarayıcı cold-load maliyeti gerçek kabulde değerlendirilmelidir.
- Windows imzalı ZIP **879 dosya / 115.660.351 byte**, `artifacts/packages/preview-v032/home-0.32.0-dev.1-win-x64.zip`; SHA-256 **5735e2f5674065bcbbd23d923f5acdbf9473943be28a6226fbec928dfe9f6454**. Mevcut güvenilen anahtarla executable `--verify-package` geçti; signing identity değişmedi.
- **http://localhost:5080/games**, runtime **0.32.0**, schema **40**, PID **3428**, release `16c02c6c89914753b7a86b9f73ce1109`. Rollback **0.31.1**, apply backup **727b0c9dd7a04f19b301ea832a9cab52**. Update wrapper 120-saniyelik timeout'a ulaştı; active release değiştirilmişti, yeniden apply/rollback yapılmadı. Sonradan healthy API ve kaynak/veri independent doğrulandı. Orijinal update istemcisinin cookie-retention assertion'ı tamamlanmış ilan edilmez.
- Canlı read-only kanıt: **7 medya / 2 istek / 3 indirme / 5 kişisel kayıt**, metadata **en-US**, **0 FK ihlali**, iki gerçek oyun ve PNG kapakları sunuluyor. Game state **0**: doğrulama oyun/favori/kayıt komutu başlatmadı. Üç transferin paused/archived durumu korunmuş. `artifacts/live-games-v032.json`, `artifacts/update-live-v032.json`.

Bu sonuçlar compiled/package/API/data doğrulamasıdır; ilk gerçek browser oyununun frame/audio/input ve TV performans sonucu kullanıcı kabulündedir. İnternet ROM/medya indirmesi veya broad browser suite yapılmadı.

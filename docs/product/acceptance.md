# Kabul ve yayın kapıları

## Geliştirmenin sorumluluğu

- Şema yükseltmesi: eski hesap, içerik/dosya kimliği, konum ve yapılandırma korunur.
- Komut revizyonu, tekrar ve eşzamanlılık: aynı niyetten çifte edinme/import oluşmaz.
- Parser/protokol: bozuk XML/JSON/NZB/torrent, çoklu attrs, eksik/unknown alan, kota ve sayfalama doğru sonuç verir.
- Karar motoru: her ret nedeni görünür; kalite altına düşme, yanlış yıl/bölüm/dil ve boyut hatası test edilir.
- Dosya işlemi: offline kök, dolu disk, izin hatası, yol kaçışı, çakışma ve güç kesintisi kullanıcı verisini bozmaz.
- Ağ: doğrulanmış sağlayıcı/endpoint sınırı, yönlendirme/timeout, sırların log ve API'de maskelenmesi.
- Playback: byte/segment aralıkları, session yetkisi, iz seçimi ve FFmpeg iptal/cleanup.
- İnternetten film/müzik indirmesi geliştirme aracı tarafından başlatılmaz. Transfer protokol testleri izole küçük fixture kullanabilir.

## Kullanıcının browser/cihaz kabulü

### Automatic updates — AUTO-UPDATE-044 / OPS-10

Apply the updated three-service CasaOS recipe once, retaining the existing data/media mounts, ports and environment settings. Verify that `home` and `home-updater` are healthy and `home-data-init` completed. In Home's updates page, verify automatic setup and pause/resume. During a later published application update, confirm that active play/media delays replacement, the update is applied after use ends, and preferences, sessions, ROM/save identities and paused transfers remain. The CI/Docker checks and old-version bootstrap passed; no household device or actual user game is exercised by those probes.

### Headless CasaOS / LAN — HEADLESS-LAN-043 / GAM-19

**2026-10-23 user acceptance:** the user confirmed that Home now runs on their CasaOS server. Initial installation/startup is accepted; media playback, games, controller behavior and sustained operation remain the checks below.

- **CASAOS-REGISTRY-043:** import [the current Intel/AMD x64 Compose](https://raw.githubusercontent.com/lumax90/homeOS/main/deploy/casaos/docker-compose.yml) through CasaOS Custom Install. Both services use **`ghcr.io/lumax90/homeos:0.43.0`**; anonymous pull and fresh-data installation passed. Verify **Host `/DATA/AppData/home/data` → Container `/data`** on both services, with no empty Host row. `home-data-init` prepares UID/GID 1654 access and exits successfully before Home starts. An existing named-volume installation must retain its original `/data` mount. Open `http://SERVER-IP:5080` from another device and create/login to the owner account.
- Leave the server without a display/browser session. Check movie/music/Live TV playback from phone, computer and TV; add actual media as a read-only bind. Verify page refresh, reconnect and independent account/device sessions.
- Import your own ROM/BIOS where required; test video/audio, keyboard/touch, physical gamepad when the browser allows it, pause, save/exit and relaunch. One native game session owns the host at a time. Another device should receive an explanatory busy response rather than replace the active game.
- Restart/recreate the CasaOS container with its existing `/data` volume and verify real preferences, ROM identities, saves and queues. The automatic protocol gate already covered seven **original isolated** systems, HTTP range/HLS, normal shutdown save and volume recreation; it does not establish every game's or TV model's compatibility.
- Plain IP HTTP is the initial access path. Trusted HTTPS is required for browser-restricted Gamepad/PWA/offline/microphone/camera features on affected devices. IP-SAN certificates are possible; mDNS/local-domain setup is not part of this slice.
- Windows **0.43.0** was verified through its configured LAN address. Each installation uses its own server address. It keeps the previous Windows host/core identities; the Linux SNES/MD AV-reporting fix is not a Windows host-binary upgrade.

### Canlı TV — LIVE-TV-042 / EXT-05

Free lists: preview a country/category, add it once, inspect source progress and open a real channel yourself. Verify duplicate Add does not create duplicate sources. Private IPTV: try a provider M3U URL, a local M3U file and a direct single-channel HLS URL, with XMLTV/LAN/tuner preferences where relevant. Saved secret fields should remain hidden and unchanged when left blank. Failed refresh should retain prior channels and manual options.

Check bounded channel titles/logos, group/source search, profile-private favorites/recent channels, actual now/next EPG and missing-guide states. In playback try channel rail and previous/next, live-edge return, fullscreen, miniplayer, R3, pause/reconnect and account changes. Exercise your own scheduled/series recordings and existing guide/age/order overrides. Metadata-list/native-fixture verification does not establish availability of every external broadcast or device codec.

### Müzik geri bildirimi — MUSIC-REFINE-0411 / MUS-12, CLI-01-K

Visual mode: the previous drawn fire/rain/fish scenes should be gone. With online visuals off or loading/failed, the shared record should appear and rotate with playback; reduced motion keeps it still. Choose among ten stock clips or the gallery, inspect source credits, and try Vinyl explicitly. The online-visual opt-in remains visible in settings and visual mode.

Long radio/album/artist/list titles should no longer stretch their cards. Discovery should have one obvious search field within its section and visible MusicBrainz attribution; try partial names, rapid corrections, clear, Enter, keyboard Down, artist selection and Back to the same result.

On the existing gamepad: try R3 to focus the music/radio dock without toggling playback, move within the dock and press R3 again to return to the previous card. Check closer off-column targets, stable repeated rows, focus after pointer use, and content visibility above the dock. Game/modal/native-field ownership and original bindings should remain predictable. [Implementation and evidence](../music-refinements-0411.md).

### Müzik, radyo ve görsel dinleme — MUSIC-EXPERIENCE-041 / MUS-09/10/11

Music gear → Metadata & sources / Sound / Visual mode / Library tools. Internet metadata/covers and stock visuals default off; enable the desired sources, check your real albums and unchanged tags/embedded artwork. Ambiguous album/artist matches should remain local. Sweep progress/rescan, disable, profile switching and backup/restore should preserve preferences and favorite station categories.

Browse albums/artists/songs/liked/recent/playlists while the dock plays; append a track without a restart, pause/append/resume, append after the queue ends, open queue/lyrics/sound, save and reopen a playlist. Select Radio → genre/country/search, favorite channels and custom categories; changing audio sources should end the previous local source. Unsupported/offline streams should offer reconnect without starting acquisition.

On your mobile/TV/browser: play a real song or radio and open Visual mode. Try every scene, optional online source credits, fullscreen/exit, auto-hide/reveal, remote/keyboard focus, volume and reduced motion; music should continue through scene changes. Verify real autoplay, background behavior and a long HLS broadcast. Source-protocol and sample-clock tests are not device or visual acceptance. [Implementation/provenance](../music-experience-041.md).

### Games bütünleşik yenileme — GAMES-EXPERIENCE-040 / GAM-18, CLI-01-J

Yeni/mevcut koleksiyon otomatik tamamlanmalı; aynı hash/oyun için tekrar eden provider kayıtları ve bölgesel kapaklar tek tek onay istememeli. Gerçek belirsiz eşleşme incelemeye kalmalı. Hasheous arama sonucunda **Bu oyun** seçimi artık upstream details hatasında aynı kimliği koruyan fallback ile tamamlanmalı. Gear → Library/Emulation/Consoles; toplu ilerleme, pause/resume ve yeniden tarama, manuel ad/kapak/kayıt korunumu denensin.

PS1 CUE/BIN'de ilk küçük prefix dışındaki SYSTEM.CNF otomatik algılaması; gerçek belirsiz biçimde açık konsol seçimi ve aynı dosyalarla devam. Console temalı gruplar, native ve browser-game ayrıntıları, geniş/dar settings modalları/checkbox/long-description/scroll/draft/fullscreen görünümü denensin. Zil popover'ı, okunmamış/all/read actions, dışarı tıklama/Escape/gamepad Back; keşif rafında controller vurgusunun bütün kenarları ve reduced motion da kullanıcı kabulüne dahildir. Otomatik browser suite çalıştırılmadı. [Dilim](../games-experience-040.md).

### Oyun ayarı kapsamı ve public metadata — GAME-SETTINGS-02 / ROM-METADATA-01, GAM-16/17

Oyun ayrıntısı ve duraklatma menüsündeki **Oyun ayarları** aynı gruplu yüzeyi açmalı. **Bu oyun** varsayılan; bir PS1 oyununda değişiklik başka PS1/GBA/profil oyununu değiştirmemeli. Kalıtım/değer, açık emülatör varsayılanı, oyun reseti, iptal/dirty draft, English/Türkçe, keyboard/controller focus ve relaunch gerektiren seçenekler denensin. Eski core/default/save kimlikleri korunmalı. İlk görülmemiş çekirdeğin gerçek tanımları oyun açılıp paused settings okunana kadar uydurulmamalı.

Mevcut ROM'da **Oyun bilgisini ve kapağı getir** kullanıcı komutudur. Dosya dışarı yüklenmeden hash/platform match, belirsiz sonuç seçimi, uygun bölge/kapak, manual title/cover korunumu ve offline/rate-limit/tekrar/kuyruk durumları denensin. Public Hasheous tüm oyunlarda açıklama/developer/players/Türkçe sunuyor sayılmaz. Yeni import aynı durable akışı kuyruğa alır; import/Play metadata'yı beklemez. [Kaynak ve kapsam](../gaming-settings-metadata-039.md).

### Otomatik ROM algılama ve PS1 CUE/BIN — ROM-IMPORT-02 / GAM-15

ROM ekle → CUE ve BIN'i aynı seçimde ekle (sıralama önemli değil). CUE başlangıç seçilmeli; console belirsizse PlayStation seç ve dosyaların hâlâ seçili olduğunu doğrula. CUE tek başına seçildiğinde eksik BIN'in gerçek referans adı görülmeli ve büyük upload başlamamalı. Tek GBA/NES/SNES/GB(C) kartuşunda uygun otomatik öneri, shared disc biçiminde yanlış PS1 tahmini olmaması, English/Türkçe, iptal/retry ve kaynak dosyaların değişmemesi denensin. Bu oyun başlatma/gecikme kabulü değildir; [teknik kapsam](../gaming-rom-import-0381.md).

### Temel ROM seçkisi — BASIC-ROM-01 / GAM-14

Varsayılan ROM sistemi listesi GB/GBC/GBA/NES/SNES/Mega Drive/PS1; emülasyon ayarlarında **Temel seçki / Kurulu çekirdekler / Gelişmiş katalog** ayrımı. Eski 3DS ROM'u/Citra/kayıtlar silinmez; gelişmiş sistem seçilip liste kapatılınca seçim veya seçili dosyalar kaybolmaz. English/Türkçe etiketler değişir, gezinmek tüm katalog veya oyun indirmez.

Her sistem için kullanıcının kendi yetkili oyunuyla: 15–30 dakika görüntü/ses/giriş akıcılığı, kısa/uzun/aynı anda basma, gamepad disconnect/reconnect, Home menüsü/duraklat/çıkış, kütüphane konumuna dönüş, batarya/SRAM veya memory-card kaydı ve save-state sonrası sunucu yeniden açma. PS1 için kendi doğrulanmış BIOS'u ve varsa çoklu disk ayrı denenir. Mevcut kayıtlardan birini test için silme/üzerine zorla yazma gerekmez.

İzole özgün native fixture/encoded-video/WHEP başarısı **gerçek oyun akıcılığı veya browser toplam gecikme kanıtı değildir**. Her sistemin ROM/hash, çekirdek/hash, server/istemci ve oyun süresiyle ayrı kabul kaydı gerekir. Şimdiki preview Windows sunucu/sahip/local browser; Linux/CasaOS/Umbrel ve LAN/TV istemci kapıları açık. [Kapsam ve teknik kontroller](../gaming-basic-selection-038.md).

### Native emülasyon hazırlığı — GAMING-ROM-PREP-01 / GAM-11 (kaynakta, henüz live değil)

Kendi yetkili `.gba` Home dialog'undan eklenir; filename/size/hash/private library entry görülür. Kaynak değişmez, retry duplicate oluşturmaz, failed/cancelled upload kayıt oluşturmaz. **Yayın kurulumu bekleniyor** açık; native `/play` default Desktop/emulator window/web-game iframe açmamalı. Bu emulator/capture/input/save-state kabulü değildir; [Windows/native kapıları](../gaming-native-foundation.md) açık. Preview 0.33/40 değişmedi.

### 0.33 ilk oyun mağazası — GAME-STORE-01 / GAM-07/08/09

Mağaza'da 2048, A Dark Room, HexGL, Hextris ve 0h h1 Classic görünmeli; kurulu olmayan oyun Oyna gibi sunulmamalı. Kütüphanem mevcut iki oyunu/favori/ilerlemeyi korumalı. Kullanıcı açık **İndir ve kur** ile provider acquisition başlatır; başlangıç/iptal/yeniden deneme, actual installed → Oyna, keyboard/gamepad ve network/disk failure denensin. Sayfa/kart/detail gezmek transfer başlatmamalı. Gereksinim/metadata/lisans/pinned-source doğrulama türü ve açık gerçek cihaz kapıları [katalog belgesinde](../game-store-catalog.md).

### 0.32 Oyunlar — GAMING-01 / GAM-01/02/03

**0.32.1 düzeltmesi:** Ctrl+F5 sonrası iki oyunda loading → Return to game/Oyuna dön aşaması görülmeli; eski generic engine-start hatası çıkmamalı. Oyun/sekme kapanışından sonra gerçek aktif session listesi boş olmalı ve boş liste güncellemeyi engellememeli. Hard reload/back ve save-and-exit kayıtları denenmeli. [Başlatma/kapanış düzeltmesi](../gaming-startup-fix.md).

1. Ctrl+F5 sonrası Games/Oyunlar Music/Müzik'in sağında olmalı; iki gerçek oyun, detay sayfası, arama ve My library/Favorites filtrelerini dene. English/Türkçe Home menüleri değişir; oyunların kendi metinleri English olabilir.
2. Tiny Crate Classic veya Wrap It Up → Play/Oyna → Return to game/Oyuna dön. Gerçek görüntü/ses ve gamepad/klavye girdisini dene; ses için oyun alanında gerçek tıklama/Enter gerekebilir. TV'de gamepad için güvenilir HTTPS gerekir; localhost yalnız aynı cihazda özel güvenilir bağlamdır.
3. Gamepad ile oyunda yön/A/X basarken Home gezinmemeli. Start+Select veya Esc ile Home menüsü, ardından tuşları bırakıp devam et. Blur/sekme değişimi, controller kopuşu, fullscreen ve tekrar bağlanmayı dene.
4. Tiny Crate'te tamamlanan bölüm/kayıt yuvaları; Wrap It Up'ta bölüm checkpoint'i: Save and exit/Kaydet ve çık, yenile ve devam et. Bu sistem tam mid-frame emulator save state değildir. Diğer profil aynı kaydı/favoriyi görmemeli.
5. Aynı profilde ikinci cihaz/sekmeden oyun başlatmak ilk oturumun yazma yetkisini kapatır. Ağ kesintisinde kayıt uyarısı ve yeniden deneme; istenirse açık Kaydetmeden çık seçeneği. Kütüphaneden çıkarma checkpoint'i silmez.
6. Gerçek TV modeli/firmware/gamepad bilgileriyle kabul sonucunu bildir. Sunucu oyunu çalıştırmaz; ROM/emulator, kullanıcı web paketi import'u ve native TV uygulaması bu sürümde yok. [Teknik sınırlar](../gaming-first-slice.md).

### 0.31.1 UI dili ve sabit ayar boyutu — LOCALIZE-02 / SETTINGS-02, CLI-01-I

English seçiliyken Settings/Playback/Notifications/Movie and TV metadata başlıkları ve tüm app-owned visible UI English olmalı; Türkçe seçince kategori/açıklama/mesajlar Türkçe olmalı. Provider/user titles ve teknik brand/protocol isimleri korunur. Settings sekmeleri arasında geçerken dialog dış genişlik/yükseklik değişmemeli; uzun panel kendi alanında scroll olmalı. Desktop/mobile viewport değişimi dışında pencere boyutu sabit kalmalı. [Audit ve çerçeve](../ui-language-and-settings-frame.md).

### 0.31 ayarlar ve kaldığın yere dönüş — SETTINGS-01 / NAV-03, CLI-01-H

Avatar'dan account/security açılmalı; kişisel/server kategorileri, arama, dar ekranda kompakt seçici, logout ve gamepad/keyboard navigation denensin. Metadata bağlantı kısayolu doğrudan film/dizi bilgilerine açılmalı. Seçili kategori değiştirilirken açık form draft'ları korunmalı; close/logout/profile değişimiyle temizlenmeli. Kısıtlı profilde admin kategorisi ve arama sonucu görünmemeli.

Discovery'de aşağı ve rafta yana kaydırıp bir film aç; uygulama Back ve browser Back ile aynı raf/kart/konuma dön, Forward da dene. Aynı filmi farklı genre rafında açarak first/top duplicate'a dönmemesini kontrol et. Library ve kişisel collection'larda aynı akış, dar/geniş ekran ve yavaş metadata/layout durumları denensin; kullanıcı scroll/gamepad girdisi restore retry'ı durdurmalı. Gezinti yeni edinme başlatmamalı. [Teknik sözleşme](../settings-and-return-navigation.md).

### 0.30.1 Discovery yatay taşma — DISC-05 / CAT-02, CLI-01-G

Ctrl+F5 sonrası Keşfet'te alt genre raflarına ve en alttaki genel sonuçlara kaydır: loading sırasında sayfa altında yatay scrollbar oluşmamalı ve sayfa genişliği animasyonla değişmemeli. Rafın kendi yatay mouse/gamepad gezinmesi, dikey scroll ve yeni içerik yüklenmesi korunmalı. Aynı Home loading stili library/details/downloads sayfalarında da yerinde kalmalı. [Canlı neden ve düzeltme](../discovery-overflow.md).

### 0.30 gamepad/kumanda gezinmesi — INPUT-01 / CLI-01-F, CLI-05, PLY-06/08

1. Önce **yalnız mouse** ile aç: katalog/detay/oynatıcı görünümü aynı olmalı; gamepad bağlı olsa bile tuşa basmadan büyük odak/tuş ipucu görünmemeli. Normal typing/caret/Enter/Backspace, Tab, Ctrl/Cmd-K ve Video.js keyboard shortcuts korunmalı.
2. Gamepad bağla ve bir tuşu basıp bırak; sol analog/D-pad ile raf/grid/nav/menü/ayarlar arasında gezin. Sağ analog ve LT/RT ile uzun sayfa/reader'ı kaydır. Disabled/hidden hedefler seçilmemeli; geri dönüşte media odağı korunmalı.
3. A/✕ ile dropdown aç/seç, B/○ ile önce dropdown/modalı kapat. Edit alanında ekran klavyesi; English/Türkçe/rakam/sembol/PIN/password-mask/apply/cancel denenmeli. Mouse'a dönünce controller ipuçları kapanmalı. Kalıcı multiple episode mapping listesinde onay explicit toggle, B önce edit'ten çıkış olmalı.
4. Ayarlar → **Gamepad ve kumanda**: enable/disable, deadzone ve standart olmayan cihaz ataması/timeout/cancel/reload. Held confirm/back tekrar komut üretmemeli; controller disconnect/reconnect ve sekme blur/return ghost input üretmemeli.
5. Oynatıcıda X/□ play/pause, LT/RT seek, Start ayarlar ve mevcut gerçek iz/kalite seçimi; miniplayer/music/native live/podcast/offline deneyimi gerçek kaynak/cihazla denenmeli. Room misafirinde ortak kontrol reddedilmeli; fullscreen B önce çıkış olmalı. Native browser izin/OS picker'ı gamepad ile bypass edilemez.
6. TV'nin gerçek D-pad/OK/Back ve üretici legacy kodları ayrıca denenmeli. Gamepad için trusted HTTPS gerekir; TV model/gamepad/firmware kaydedilmeden genel TV destek ilanı yapılmaz. [Kapsam ve protokol sınırları](../controller-navigation.md).

### 0.29.1 ortak dropdownlar — UI-CONTROLS-01 / CLI-01/05, PLY-06/08

Keşfet, Kütüphane filtresi, detayda puan/koleksiyon/sezon, Ayarlar'da dil/kaynak/kalite ve oynatıcı ses/altyazı/kalite seçicilerini aç: popup'lar aynı Home materyaliyle görünmeli, eski OS listesi açılmamalı. English/Türkçe, uzun seçenekler, dar ekran, scroll paneli/modaldan açılma, klavye/typeahead/Enter/Escape/dışarı tıklama/focus ve disabled seçenekleri dene. Değeri değiştirdiğinde aynı eski Home işlemi çalışmalı; gezinmek yeni edinme başlatmamalı. Daha fazla/Video.js menüleri aynı görsel dili kullanmalı. [Kapsam ve teknik sınırlar](../shared-selection-controls.md).

### 0.29 raflı keşif — DISC-04 / CAT-02/05/08, CLI-01/05

English/Türkçe arasında geçiş yap: genre seçicisi ve raf başlıkları aynı UI dilini kullanmalı; kayıtlı metadata dili değişmemeli. Trend/popüler/genre rafları gerçek sağlayıcıdan, kişisel raflar yalnız kendi hesap verinden gelmeli. Aşağı/yana scroll ile lazy sayfa yükleme, geniş/dar ekranda sütun dolumu, genre/film-dizi/arama değişiminde eski sonuçların karışmaması, boş batch sonrası manuel devam ve geri dönünce scroll/filtre korunumu denensin. Keşifte gezinmek edinme başlatmamalı. [Uygulama ve sınırlar](../discovery-shelves.md).

### 0.28.2 kompakt gezinme — NAV-01 / CLI-01/05

Üstte Keşfet/Kütüphane/Müzik ve Daha fazla bulunmalı; Home logosu ana sayfayı açmalı. Daha fazla'nın Medya/Kişisel alan/Sunucu grupları bütün önceki hedeflere götürmeli ve aktif ekranı işaretlemeli. İzinli olmayan keşif/indirme/aktivite hedeflerini kısıtlı profil ile kontrol et. Dar ekranda dock sığmalı; klavye ile menüyü açıp seç, Escape ile kapat ve geri/ileri gezinmeyi dene. Açık miniplayer navigasyonda durmamalı. [Yerleşim ve doğrulama](../compact-navigation.md).

### 0.28 Video.js ve ayrıntı sayfaları — ACCEPT-28-PLAYER-PAGES

**0.28.1 yerleşim geri bildirimi:** Favori/Puan/Koleksiyon kontrolleri artık hero'da Oynat/Devam et yakınındadır. Oynatıcı sağ üstteki **Ses, altyazı ve kalite** düğmesi temel seçenek panelini açar; gecikme/yükleme/sağlayıcı/burn-in ve diğer işler aynı panelde **Gelişmiş Home ayarları** altındadır. Sadece mevcut dosya izleri seçilebilir; tek ses/altyazı yok açıklamasını da kontrol et.

1. Gerçek film/dizi kartını aç: popup yerine adresli büyük ayrıntı sayfası, afiş/özet/cast ve mevcut dosyalar/sezonlar görünmeli. Geri/ileri ve sayfa yenilemesini, Ctrl/Cmd ile kartı yeni sekmede açmayı dene.
2. Oynat/Devam et ile izleme sayfasına git. Video.js ayarlarında dosyada gerçekten bulunan ses/altyazı ve Home kalite profillerini seç; konum korunmalı. Bitmap altyazı ve gecikme/sağlayıcı işleri yan panelden yönetilir.
3. Filmin ilerisine ve gerisine seek et: zaman çizelgesi yalnız hazırlanmış HLS parçasının değil bütün dosyanın süresini göstermeli. Bitmiş dosyada Replay yeni geçerli oturumla başlamalı.
4. Küçült, başka içerik ayrıntısına git ve genişlet: aynı oynatma sürmeli. Fullscreen/PiP, klavye, dar ekran, Türkçe/English ve reduced-motion tercihlerini dene.
5. Birlikte izleme/uzak kumandada host/guest yetkisini kontrol et; izinsiz kontrol hazır Video.js kontrollerinden de uygulanmamalı. [Uygulama ve odaklı kanıt](../videojs-and-content-pages.md).

### 0.27.2 Chromium HLS — PLAY-02 / ACCEPT-04

Güncellenmiş istemcide **Ctrl+F5** sonrası aynı gerçek dosyayı uyumlu 1080p/720p ile tekrar aç. İlk parçadan sonra kesilmeden devam etmeli; ileri/geri seek ve kalite değişiminde konum korunmalı. Orijinal codec desteği ile HLS taşıma hatası ayrı koşullardır. Native-only Safari cihazı ve Live TV kabulü ayrıca kullanıcıdadır. [Teşhis, düzeltme ve odaklı kanıt](../playback-hls-fix.md).

### 0.27.1 dil seçimi ve torrent düzeltmesi — ACCEPT-27-LANGUAGE / TRANSFER-03

**Ctrl+F5** ile önizlemeyi yenile; giriş ekranı veya hesap ayarlarındaki **Interface language / Arayüz dili** alanından English/Türkçe seç. Sayfa yenilemesinde tercihin saklandığını, açık form/oynatıcıda konum ve girdilerin korunduğunu kontrol et. Metadata ve altyazı dili kendi ayarlarından seçilir.

Kullanıcının mevcut YTS indirmesi 2026-10-17 08:52'de **10 eş / 2,56 MB/sn / 55,3 MB alınan** olarak gözlendi. Kullanıcı bu aktarımın tamamlanma, kimlikli import ve oynatma sonucunu kontrol eder. Diğer Torrents.csv kaydı kullanıcının duraklatılmış/arşivlenmiş durumundadır. [Odaklı kanıt ve yayın](../localization-and-torrents.md).

### 0.27-dev birleşik geliştirme — sonraki test turu

Kalan yedi grubun kodu tek çalışmada eklendi. Kullanıcının talebi doğrultusunda kapsamlı test/kabul geliştirme sonrasına alınmıştır: [giriş noktaları, sınırlar ve mevcut derleme kanıtı](../development-completion.md). 2026-10-17'de veri/Windows update-rollback kontrolleri ardından **http://localhost:5080** 0.27 önizlemeye yükseltildi. Gerçek ASR modeli, alıcı cihazları, kaynak/proxy, IPTV/DVR, okuyucu/podcast ve geniş regresyon bu turda ele alınacak.

Şema 38'deki yaşam döngüsü ve şema 39'daki kaynak kurulumu için bu tura ayrıca sayfalar arası Cast/AirPlay kumandası, DLNA'nın gerçek konum/başka içeriğe geçişi, alıcı kopyası hazırlama sırasında restart, tuner kesintisi sonrası kısmi DVR yayını ve kaynak/EPG/kayıt kuralı düzenleme kabulü girer. Son dilimde 42 odaklı backend kontrolü geçti; **0.26/şema 30 → 0.27/şema 39 → önceki sürüm/veri** Windows yükseltme/geri alma turu ayrı ortamda doğrulandı. [Önizleme yayın kanıtı](../preview-027.md).

**ACCEPT-27-GENRES / CAT-02:** 0.27 önizlemede **Keşfet → TMDB → Filmler / Diziler → Türlere göre keşfet** üzerinden aksiyon/komedi gibi gerçek türleri seç. Popüler/yüksek puanlı/yaklaşan listeleri, seçili türde başlık aramasını ve daha fazla sayfa yüklemeyi dene. Film/dizi geçişinde eski tür temizlenmeli; hızlı filtre değişimi eski sonuçları yeni türe taşımamalı. Bu akışın 8 odaklı metadata/API kontrolü geçti; browser kabulü kullanıcıdadır.

**ACCEPT-27-SOURCES / SOURCE-02:** 0.27 önizlemede **Kaynaklar → Site seçerek kaynak ekle** ile kendi kaynağını seç. Üyelikli kaynak için o sitenin API anahtarını gir; **Bağlantıyı test et → Kaynağı ekle** adımlarını tamamla. Test başarısız/iptal olduğunda aktif kaynak oluşmamalı. Kaynağı düzenlerken eski bağlantı uygulama komutuna kadar korunmalı. İsteğinin kaynak aramasına dön; edinme seçimi kullanıcı komutudur. Usenet profilinde ayrıca kendi NNTP hesabını yapılandır. Desteklenen profiller ve gerçek salt-okunur kanıt [burada](../source-catalog.md).

### Yeni 0.26 müzik kataloğu — ACCEPT-26

1. **Müzik → Sanatçılarım** altında gerçek ortak/konuk sanatçılı albümünü kontrol et; albüm ve parça katkıları doğru kişi/grup kimliğine bağlanmalı.
2. **Listeden toplu takip** ile kendi çalma listen veya açık MusicBrainz koleksiyonundan kayıt seç. Yeni yayın takibini istersen etkinleştir; mevcut takip tercihleri korunmalı.
3. Gerçek `.elrc`/`.lrc` sözlerde satır/kelimeye tıklayarak ileri/geri git; sıradaki parça ve ortak odanın kumanda iznini kontrol et.
4. Parçada **Video ve ilişkiler → Kütüphaneden video bağla** ile gerçek klibini aç; ilgili albüm/recording bağlantılarını incele.
5. Yönetilen Ogg/Opus/WAV/AIFF kopyasında etiket/kapak/tarih/adlandırma ve **Müzik işleri → Önceki dosyayı geri al** akışını dene.

Katkı/ACL, kalıcı toplu takip/iptal/kota, ELRC/seek, native etiket/hash/undo ve eski veri/metadata komutu koruması doğrulandı. Gerçek kullanıcı dosyası, koleksiyon ve cihaz sonucu bu kapıyı kapatır: [0.26 sözleşmesi](../milestone-26.md).

### Yeni 0.25 bölüm kataloğu — ACCEPT-25

1. İçerik ayrıntısında **Katalog kimlikleri → Sağlayıcı bölüm değişikliklerini incele** ile gerçek dizinin eski/yeni kimliklerini kontrol et. Uygulama sonrasında dosya, istek ve konum aynı bölüme bağlı kalmalı.
2. Birleşik bir sağlayıcı bölümünü birkaç kanonik bölüme eşleştir. Dosyadaki devam konumunu ve yeni dosya eşleştirmesini kontrol et.
3. **Bölüm numaralandırması → DVD, dijital ve alternatif sıralar** ile kaynak gruplarına sezon numarası ata veya elle sıra kaydet. Etkin seçim dosya/kaynak numaralarına yansımalı.
4. Çok bölümlü dosyanın ayrıntısında **Dosyadan bölüm sınırlarını öner** seç; oynatarak/ileri sararak sınırları incele ve uygula. Bölüm bazlı konumu kontrol et.

Kimlik, istek kapsamı, eski komut engeli, native sınır algılama ve şema yükseltmesi doğrulandı. Gerçek sağlayıcı/cihaz sonucu kullanıcı kabulünü bekler: [0.25 sözleşmesi](../milestone-25.md).

### Yeni 0.24 hesap indirme bütçesi — ACCEPT-24

1. **Hane ve hesaplar → hesabı düzenle → Hesap indirme bütçesi** altında aylık adet/boyut ve eşzamanlı sınır seç. Kendi gerçek kaynağınla başlatılan işin hesabını **İşlemler** ekranında kontrol et.
2. Yetersiz boyut kotasında bekleme nedenini gör; bütçeyi artır veya torrent dosya seçimini daralt. Aynı paket duraklat/devam ile ikinci kez borçlandırılmamalı.
3. İki üye kendi aktarım/geçmişini görmeli; yönetici hane aktarımlarını yönetebilmeli. Otomatik edinmenin kural/monitor sahibine yazıldığını kontrol et.
4. Staging temizliği sonrası yeniden edinmenin yeni boyut kullanımı oluşturduğunu ve kütüphane dosyalarının korunduğunu kontrol et.

Paket boyutu kotasının tanımı, veri/metadata/yeniden iletim farkı ve kabul kanıtları [0.24 sözleşmesinde](../milestone-24.md). Gerçek kaynak hesabı edinmesi kullanıcı komutudur.

### Yeni 0.23.1 birlikte izleme ve uzak kumanda — ACCEPT-23

1. İki Home hesabına aynı gerçek dosya erişimini ver. İlk cihazda oynatıcı başlığındaki **Birlikte** simgesinden oda kur ve diğer hesap adını davet et. İkinci cihaz **Birlikte → Katıl ve oynat** seçsin.
2. Gerekirse **Bu cihazda oynatmaya izin ver** seçilsin. Ortak oynat/pause/seek/hız ve cihazlara özel altyazı/kaliteyi dene. Bağlantıyı kesip geri getirerek ortak konumu kontrol et.
3. Konuk kumandasını aç/kapat, katılımcıyı çıkar. Eski akış ve oda komutları reddedilmeli. Kotayla/elle kapatılmış oynatma yeniden deneme seçilene kadar durmalı.
4. Aynı hesabın diğer cihazından **Uzak kumandayı aç**ılmış oynatıcıya bağlan. Ses/sessiz ve dosya değiştirmeyi, hedef cihazda oda kapatma/profil geçişini dene.

Sunucu/istemci durum makinesi, bağımsız native direct/HLS, eski komut/yayın engeli ve gerçek süreç restart/çökme doğrulandı. Gerçek cihaz senkronu/autoplay ve arka plan davranışı kullanıcı kabulünü bekler. [0.23 sözleşmesi](../milestone-23.md).

### Yeni 0.22 çevrimdışı cihaz kopyaları — ACCEPT-22

1. HTTPS Home/PWA'da **Cihazım** arşivini oluştur/aç. Küçük bir gerçek dosyanın ayrıntısında **Cihazıma indir** ile özgün veya uyumlu kopya seç.
2. Aktarımı duraklat ve Home'u kapat-aç; tamamlanmış parçalar korunarak devam etmeli. Cihaz kotası ve kalıcı depolama iznini kontrol et.
3. Ağı kapat; **Cihazdan oynat**, seek ve altyazıyı dene. Arşiv kilitlendiğinde yerel akış durmalı.
4. Başka cihazda izleme konumunu değiştir; yeniden bağlantıda çatışma seçimi ve kişisel hesap ayrımını kontrol et.

Native MP4/AAC/altyazı ve şifreli depo/Range/controller kontrolleri geçti. Canlı kullanıcı dosyası indirilmedi; gerçek browser depolama/arka plan/codec kabulü açıktır. [0.22 sözleşmesi](../milestone-22.md).

### Yeni 0.21 web/PWA push — ACCEPT-21

1. **Bildirim kanalları → Web/PWA push sunucusu** bölümünde yönetici iletişim adresini kaydet ve sunucuyu aç.
2. HTTPS Home'da **Bu cihazda push** bölümünden izin ver, olayları seç ve deneme gönder. iPhone/iPad'de Home Screen PWA'yı kullan; worker güncellemesi bekliyorsa istemci güncelleme düğmesine bas.
3. Bildirim tıklanınca kendi inbox'ın açılmalı. Çıkış/profil değişiminden sonra eski hesabın aboneliği kapanmalı; yeni profilde açıkça etkinleştir.
4. Kullanacağın tarayıcı/PWA cihazında arka plan ve ağ değişimi sonucunu kontrol et.

VAPID ve şifreli paket bağımsız alıcıda, hesap geçişi saf worker testlerinde doğrulandı. Gerçek gateway/cihaz mesajı geliştirme doğrulamasında gönderilmedi; canlı push ve VAPID ayarı henüz kapalıdır. [0.21 sözleşmesi](../milestone-21.md).

### Yeni 0.20 bildirim kanalları — ACCEPT-20

1. **Ayarlar → Bildirim kanalları** içinden kendi webhook/SMTP hedefini, ilgili Home hesabını ve olay filtrelerini kaydet. **Deneme gönder** ile kendi hedefindeki iletiyi kontrol et.
2. Otomatik iletimi açtıktan sonra gerçek istek/müzik/altyazı/backup olayını, **Dış gönderim geçmişi** ve hedefteki sonuçla karşılaştır.
3. Kanalı duraklat/değiştir; eski bekleyen iletiler iptal edilmeli. Geçici hedef hatasında retry/kota ve kullanıcı iptalini kontrol et.

Yerel gerçek TLS/STARTTLS/SASL ve Türkçe MIME, HMAC/tekrar, sertifika reddi, kota ve kullanıcı izolasyonu doğrulandı. Canlı Home'da kanal henüz yapılandırılmamış; gerçek hedefe test iletisi gönderilmedi. [0.20 sözleşmesi](../milestone-20.md).

### Yeni 0.19 oynatma sınırları — ACCEPT-19

1. **Hane ve hesaplar** bölümünden hesabın eşzamanlı oynatmasını 1 yap; ilk video açıkken ikinci sekmede müzik/video açmayı dene. Sınır mesajındaki **Açık oynatmalarım** bölümünden eski oynatmayı kapat.
2. Aynı oynatıcıda ses/altyazı/kalite ve kesintisiz → özgün müzik geçişlerini dene; bunlar ikinci bir slot tüketmemeli.
3. Diğer cihazdan oynatmayı veya giriş oturumunu kapat; ilk cihazın oynatması durmalı. Cihaz varsayılanı ve hesap başına etkin cihaz sayısını kontrol et.
4. Sunucuyu yeniden başlatıp sınırların/konumun kaldığını, eski oturumun geçmişe alındığını ve kaldığın yerden yeni oynatma başlatıldığını doğrula.

207 backend/native + 7 ses testi geçti. Kestrel restart/çökme ve aktif HTTP yanıtının iptali doğrulandı; gerçek tarayıcı/cihaz kabulü açıktır. [0.19 sözleşmesi](../milestone-19.md).

### Yeni 0.18 altyazı akışı — ACCEPT-18

1. Gerçek bir video ayrıntısında **Bu dosyanın altyazı tercihleri** bölümünü aç; dil/forced/HI, yükseltme ve genel tercihe dönme akışını kontrol et.
2. Bağladığın OpenSubtitles hesabıyla sürüm ekle/yükselt; geçmişten geri al. Önceki sürüm korunmalı ve geri alma sonrasında otomatik yükseltme duraklamalı.
3. Oynatıcıda PGS/DVD izi seç; görüntüye işlenmiş altyazıyı, seek ve gecikmeyi dene. ASS için **Altyazı stilini görüntüye işle** seçeneğini kontrol et.
4. **Sesle altyazı zamanını eşleştir** bölümünde doğru ses/metin izini seç; yerel öneriyi incele ve uygula. Yeni WebVTT sürümünü gerçek diyaloğa göre değerlendir.

Model CPU'da canlı başlatılmış, PGS/DVD/ASS ve yerel konuşma native fixture'ları doğrulanmıştır. Canlı OpenSubtitles bağlantısı şu anda yapılandırılmamış; gerçek sağlayıcı ve cihaz kabulü kullanıcı sonucunu bekler. [0.18 sınırları](../milestone-18.md).

### Yeni 0.17 katalog akışı — ACCEPT-17

1. Gerçek bir diziyi takip listene ekle veya eşlenmiş dosyasını aç. **Katalog kimlikleri ve alternatif adlar** bölümünden katalog yenilemesini ve diğer sağlayıcı kaydının birleştirme önizlemesini kontrol et.
2. Çok bölümlü bir dosyada **Dosyanın bölüm kapsamı ve zamanları** ile üyelik/başlangıçları kaydet; bölüm listesi, bölümden oynat ve ayrı izleme konumlarını dene.
3. Aynı filme eşlenmiş parçaları **Çok parçalı film sürümleri** ile sırala. İkinci parçadan devamı, açık parça seçimini ve eksik/profile kapalı parça sonucunu kontrol et.
4. **Bölüm numaralandırması** ayarında günlük tarih veya açık mutlak/anime haritasını seç. TMDB kanonik dizide type-2 grup listesi aktarımını kendi yapımınla doğrula.

Backend/native çoklu dosya/kimlik kontrolleri geçmiştir. Canlı önizlemede kaydedilmiş yapım/takip kaydı bulunmadığından bu kullanıcı akışları henüz canlı cihaz kabulü değildir; gerçek veri sağlayıcı okuması ve uygulama yükseltmesi doğrulanmıştır. Kaynak medya edinmesi geliştirme doğrulamasında başlatılmaz. [0.17 kapsamı](../milestone-17.md).

### Yeni 0.16 müzik akışı — ACCEPT-16

1. **Müzik → Sanatçı keşfet** içinde bir sanatçı/albüm aç; **Takip ve otomasyon** bölümünden metadata takibini aç. **Takip ve otomasyon** sekmesinde bir albümü hariç tutup yenile; istisna korunmalı.
2. Gerçek yerel bir albümü çal; iki parça arasında geçişi dinle. Mini/tam görünüm, hızlı seek/sonraki, shuffle/repeat ve telefonda arka planı kontrol et.
3. Oynatıcıda parça/albüm ReplayGain'i dene. Albümde **Ses benzerliğini analiz et**, birkaç favori ekle ve **Sana yakın** önerilerini kontrol et.
4. Kullanıcı edinmesiyle yönetilen kütüphaneye gelmiş bir dosyada etiket önizlemesi → yazım → **Müzik işleri → Önceki dosyayı geri al** akışını dene. Kaynak klasöründeki dosyalarda salt-okunur açıklaması görünmeli.

PCM/gapless çıkışı 48 kHz stereo, parça başına 30 dakika destekler; uzun dosyada özgün oynatıcı seçilebilir. Native örnek/etiket/spektrum ve saf zamanlayıcı testleri geçmiştir; gerçek cihazda dinleme kabulü ayrıca gereklidir. Kaynak hesabı yokken arama/edinme adımları bekler. Ayrıntılı sınırlar: [0.16](../milestone-16.md).

### Şimdi denenebilen 0.4.1 akışı

1. `http://localhost:5080` sayfasını yenile. **Keşfet** ekranında gerçek bir film/dizi aç ve takip listene ekle; sayfa yenilendiğinde kaydı kontrol et.
2. **Kütüphane ekle** ile kendi cihazındaki küçük bir gerçek medya klasörünü bağla. Normal katalog artık eski demo dosyalarını göstermez.
3. Bir MP4 ve varsa bir MKV aç. Oynatıcı ayarından **720p** veya **H.264 uyumluluk modu** seç; gösterilen yöntemi ve sesi kontrol et.
4. Videoyu ileri/geri sar; hazırlık sırasında tekrar başka konuma sar. Mini/tam görünüm arasında geçiş yap, kapatıp yeniden aç ve kaldığın yeri kontrol et.
5. Çok sesli dosyada iz değiştir. Yan/gömülü metin altyazısı seç veya küçük bir SRT yükle; gecikmeyi ±0,5 saniye değiştir.
6. Klavye Tab/Escape, telefon görünümü, PiP ve tarayıcı destekliyorsa PWA kurulumunu dene. Çevrimdışında kabuk açılır; sunucu bağlantısı gerektiren ekranlar bunu açık gösterir.

Indexer hesabı henüz seçilmediği için gerçek kaynak/edinme kabulü açık. Katalog ve takip komutları transfer başlatmaz; internet edinmesini kullanıcı başlatır.

Her teslimde uygulanmış satırlar seçilir; henüz yazılmamış özelliği test etmesi istenmez.

| Senaryo | Beklenen sonuç |
|---|---|
| Gerçek bir film/dizi ara | Sağlayıcı, yıl, afiş ve mevcut/istenen durumu doğru; sahte sonuç yok |
| Aynı isimli iki yapımdan birini seç | Doğru dış kimlik ve yıl korunur |
| Dosyasız içerik iste/takip et | Yenileme ve restart sonrası istek/profil kalır; oynatılabilir gibi gösterilmez |
| Dizi kapsamını değiştir | Tüm/gelecek/eksik seçimlerine göre episode listesi değişir; mevcut dosyalar korunur |
| Kaynak bağla/test et | Başarılı caps veya açık kimlik/kota/TLS hatası; anahtar görünmez |
| Kaynaklarda sürüm ara | Boyut/kalite/protokol/seed bilgisi, neden kabul/ret olduğu ve kaynak sonucu görünür |
| İndirme başlatmadan sayfadan ayrıl | Metadata/arama işlemi transfer başlatmaz |
| Kullanıcı seçerek edinme başlatır | İstek/release ilişkisi ve kalıcı durum oluşur; iptal/devam tutarlıdır |
| Kütüphane diski çıkarılır | İçerik kaydı ve izleme konumu silinmez; erişim sorunu görünür |
| Uygulama yeniden başlatılır | İşler istenen durumdan toparlanır; duraklatılmış iş başlamaz |
| Mobil dokunma/klavye | Focus ve geri dönüş tutarlı, yatay taşma yok, kontroller erişilebilir |
| PWA kurulum/offline | Desteklenen işlevler açık; başka hesabın verisi gösterilmez |
| Gerçek codec/altyazı kombinasyonu | Belgelenen playback matrisiyle uyumlu sonuç veya açık destek durumu |

## Ürün seviyesinde tamamlanma

Bir kararlı yayın için: belirtilen hedeflerin kurulum/yükseltme/kurtarma akışı, gerçek sağlayıcı bağlantıları, veri kaybını önleyen kontroller, erişim politikaları, desteklenen playback matrisi ve kullanıcı kabulü tamamlanır. Tam kombinasyon eşdeğerliği için ayrıca feature-matrix'teki bütün zorunlu kapsam ve dış kaynak/cihaz kapıları kapanır.

TMDB/indexer/altyazı token'ı olmaması, GPU/tuner bulunmaması veya browser kabulünün beklemesi açık bağımlılıktır. Derlenen kod veya fixture cevabı bu bağımlılığı kapatmaz. Test sayısı tek başına ürün olgunluğu ölçüsü değildir.

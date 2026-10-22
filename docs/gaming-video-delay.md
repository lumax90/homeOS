# Native video delay and process exit — 0.34.4

Kontrol **2026-10-20**, GAM-05/10/11, GAMING-VIDEO-LATENCY-01. Kullanıcı 0.34.3 sonrası girdinin iyileştiğini fakat halen oynanabilir olmadığını bildirdi. Önceki security/hot-loop hatası kabul edilir; yeni key delay veya güvenlik bekleme süresi eklenmez.

## Exit sorusunun doğrulanması

Kullanıcı oyundan çıktıktan sonra read-only OS process listesinde **RetroArch/MediaMTX yok**, active Home game sessions **0**. Explicit Save-and-exit başarıyla bittiğinde native engine ve media process dispose/kill + wait-exit yolundan kapanır. Browser tab/page close için keepalive end ve fallback **35s inactivity** + bounded save/cleanup vardır; Home server/policy monitor'un idle kalması emulator/game çalışması değildir. Save başarısızsa UI açıkça hata/retry/discard sunar; kaynak game/save silinmez.

## Yeni encoded video ölçümü

`tools/Home.NativeVideoProbe` yalnız original ARM latency fixture, actual verified native binaries ve mevcut FFmpeg decoder ile çalışır. User ROM/normal library/browser/private desktop kullanılmaz. Input press → TS UDP → decoded test color observation; **MediaMTX/WebRTC/browser bu ölçümde yok**.

İlk full Mode-3 fixture **219–237ms** verdi; bu fixture full-screen paint süresini içerir. Paint workload ayrı tutuldu: her VBlank'te yalnız **2x2 color block** yazan original variant ve first-pixel crop kullanılır. Default fixture/body/header diğer integration proof'ları için değişmez; önceki ölçüm gizlenmez.

Aynı minimal fixture ve single-thread decoder ile:

| Gönderici config | Decoded color delay | Report |
|---|---|---|
| Current threads=2 / default Opus frames | **184 / 176 / 160 ms** | `...\native-video-isolated-tests\attempt-4ce13a97f79b44a2b804c63c4e1b7d0a\native-video-delay.json` |
| threads=1 / Opus lowdelay / 2.5ms frame | **78 / 86 / 79 ms** | `...\native-video-isolated-tests\attempt-37039e1800414f7ca2596849d473f4dc\native-video-delay.json` |

Earlier automatic-decoder-thread runs are saved too and are not directly substituted for this same-decoder comparison. These numbers are **isolated pipeline observations, not real browser input-to-photon or a guaranteed latency SLA**.

## Adopted tuning and visible evidence

Sender keeps upstream FFmpeg H264 baseline/zerolatency/B-frames=0; encoder threads **1**, libopus **lowdelay / 2.5ms frame**. No codec/RTC/emulator engine written or native binary patched; reviewed unmodified mGBA/input-backport remains the same. Original audio/transport/save/load/core test with tuned config passed, H264 + Opus tracks/WHEP201/0 window verified: `...\native-gaming-isolated-tests\attempt-b3986244f725435a958f7aa1d6ae314e\probe.json`.

Browser `RTCRtpReceiver.jitterBufferTarget=0` requested **only if supported**, setter failure handled. This preference does not guarantee zero buffering; browser retains network constraints. UI **Stream measurements / Yayın ölçümü** shows actual inbound video jitter-buffer/decode counter deltas, not fabricated total lag. Stats run asynchronously once per second, outside native/keyboard input path.

Sources checked **2026-10-20**: exact released RetroArch `record_ffmpeg.c` codec option forwarding; installed FFmpeg 9.0.2 low-delay decoder CLI/current Context7; [MDN jitterBufferTarget](https://developer.mozilla.org/en-US/docs/Web/API/RTCRtpReceiver/jitterBufferTarget). Audio, native close/save/data and user-owned browser acceptance stay explicit.

**98 backend/48 Node/0 skipped** final check; TypeScript/worker/PWA/signed publish passed. A missing dynamic `ms` UI localization key was caught and added, not bypassed. This slice is not a statement that real gameplay is now accepted.

## Canlı publication ve user exit

İlk update attempt **1 active game** nedeniyle hiçbir state değiştirmeden durdu. Kullanıcı explicit **“Çıktım, güncelle”** yanıtı verdi. Çıkıştan sonra yeni snapshot alındı; **8 current game states** önceki 7-state snapshot'a döndürülmedi. Readonly `native-preview-data-before-v03441.json` → `live-native-v03441.json` user ROM/save/enable/media/personal/collection/download verisinin aynı olduğunu doğrular.

**0.34.4 / schema41**, PID **5860** canlı; signed package **904 files / 115970961 bytes**, original trust fingerprint değişmez. Rollback **0.34.3**, apply backup **af4f62893f5c4d5cbf06bcac75576619**. `artifacts/update-live-0.34.4.json`. Native **enabled/available=true**, **0 FK violations**, three paused transfers unchanged; no acquisition/resume/user-ROM gameplay verification command. Client **`/assets/index-CIt_qk0T.js`**, PWA **e9b65f4f288fea50**. Same core/native state partition remains; native binary/firewall rules were not changed.

User can reload and report **Stream measurements / Yayın ölçümü** buffer/decode values while testing. Actual browser latency/playability remains open; this release does not guarantee the isolated 78–86ms value in the browser or declare the whole product complete.

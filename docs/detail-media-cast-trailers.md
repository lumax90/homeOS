# Compact credits, gallery and embedded trailers — DETAIL-MEDIA-01

Date **2026-10-26**. Requirements **CAT-02/05/08/10, CLI-01/05, PLY-06, OPS-09**. User requests one initial cast row with expandable credits, several genuine title images, and trailers playing within Home without downloading them. **Local implementation; not published or real-device accepted.**

## Detail views

- The shared credits component displays **one responsive initial row** in title/detail layouts. Container width determines one to eight cards; **Show more / Daha fazlasını göster** expands in bounded batches, **Show less / Daha az göster** returns to the first row. Controls expose expanded state/target, hidden rows are not retained as invisible focus targets, missing portraits have neutral placeholders, and actual cast/crew roles remain provider data.
- Film/show provider pages, matched file details and the local series episode page use the same credits/media sections. Unmatched local files and other media types do not get fictitious cast, images or trailers.
- The gallery initially displays up to **three landscape images**, expands to at most **24**, and can show a selected larger image **inline** with a close control. Images remain lazy-loaded direct provider CDN URLs with source attribution, not fake screenshots or a bundled gallery dataset.

## Provider integration

- TMDB `movie/{id}/videos` / `tv/{id}/videos` and `images` are connected through the existing bounded/cancellable/authenticated metadata client. Only validated **YouTube Trailer/Teaser** keys are returned, official results are preferred without marking unofficial uploads official, and an empty requested-language trailer list can fall back to **en-US**.
- TMDB image/video response IDs must match the requested content. Only valid CDN image paths and landscape dimensions enter the gallery; arbitrary host URLs, non-trailer clips and malformed video keys do not become iframe sources.
- TVmaze `shows/{id}/images` supplies genuine background/banner images. Its API provides no trailer collection; this slice does not invent a YouTube search result or silently cross-match another provider as that show's trailer.
- `/discovery/{provider}/{kind}/{id}/media` applies the existing authenticated profile/metadata age/block gate before returning extras. Metadata arrays are bounded and cached for four hours only on a fully successful read; individual trailer/image failures remain separately visible/retryable. No token is sent to the browser.

## YouTube within Home

- A **Watch trailer / Fragmanı izle** action creates the native YouTube iframe **inside the detail page**. Before that selection there is no YouTube iframe/thumbnail/API request or autoplay. `youtube-nocookie.com` uses YouTube's documented privacy-enhanced mode; this is **not** an anonymous/ad-free promise.
- Only a strict eleven-character provider video key can generate the embed URL. Native controls, keyboard, inline playback and fullscreen remain available; the viewport is at least **200×200**. `autoplay=0` is explicit. Language follows the Home interface.
- **`strict-origin-when-cross-origin`** retains the Referer identity required by YouTube while avoiding disclosure of the complete Home page path. Setting `no-referrer`/`noreferrer` on the embed would cause identification problems, including the documented missing-client-identity error class.
- No downloader, video proxy, copied YouTube SDK, advertising suppression, branded-control overlay or offline media acquisition is introduced. The normal native player owns playback inside the iframe. `rel=0` limits related results to the same channel; it does **not** remove all related videos. Deprecated `modestbranding` is not used.
- Closing the preview, leaving the page/account context or hiding the document removes the active preview. There is an optional explicit **Open on YouTube** escape link; Home does not force navigation there. Region, deletion, embedding restrictions and age-restricted videos are controlled by YouTube and are not claimed universally playable inside Home.

## Verification / boundaries

Current provider documentation, existing metadata/React/HeroUI APIs and installed HeroUI button types were reviewed. Regression definitions cover strict iframe keys/non-autoplay/privacy-enhanced URLs, responsive credit counts, wrong-content-ID rejection, non-trailer exclusion and safe landscape gallery paths. **Backend/regression definitions compiled with 0 warnings / 0 errors; frontend TypeScript/worker, Vite/PWA and English/Turkish translation build passed.** Per the user's preference, these suites/browser/benchmarks were not run. No real provider image or video was downloaded during development; compilation is not claimed as verified household YouTube/provider playback.

Existing uncommitted library/series/import naming, Selkies signalling, HLS/controller/radio and media-runtime work is preserved. No publication, household update or new database migration is performed by this slice; provider media descriptors are memory-cached only. Complete product/data-distribution and real household acceptance gates remain open.

## References / licences

Reviewed **2026-10-26**. Context7 quota was exhausted; official source documentation was used.

- [TMDB movie videos](https://developer.themoviedb.org/reference/movie-videos), [movie images](https://developer.themoviedb.org/reference/movie-images), [TV series videos](https://developer.themoviedb.org/reference/tv-series-videos), [TV series images](https://developer.themoviedb.org/reference/tv-series-images). Existing TMDB attribution/API conditions and third-party media copyright remain applicable; catalogue availability is not permission to redistribute videos.
- [TVmaze API](https://www.tvmaze.com/api): show images/backgrounds and **CC BY-SA** data attribution; image/media copyright is not represented as unrestricted.
- [YouTube player parameters](https://developers.google.com/youtube/player_parameters), [privacy-enhanced embed mode](https://support.google.com/youtube/answer/171780?hl=en), [embedded-player identity/Referer requirements](https://developers.google.com/youtube/terms/required-minimum-functionality). YouTube embedded-player/API Services terms apply; docs/code-sample licences do not grant a licence to uploaded media.

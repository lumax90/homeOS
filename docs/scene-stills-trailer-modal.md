# Scene stills and hero-card trailer modal

Date **2026-10-26**. Slice **DETAIL-MEDIA-02**, CAT-10, CAT-05, CLI-01/05, PLY-06, OPS-08/09. User clarified that the gallery means actual movie scenes, not alternate posters/fanart, and chose **local film-frame extraction** after the provider/photo rights distinction was explained.

**Local implementation approved for the 0.48.0 release batch on 2026-10-27.** Current public 0.47.0 does not include this slice; [publication status](release-048.md). Household update/helper acceptance remains separate; no household files or settings were changed during development.

## Behaviour

- Gallery now uses actual local video frames, independent of provider artwork. Posters remain in the main title card, never substituted for missing scene stills. Provider-only titles without a local accessible video show an honest empty state. Stills are not represented as behind-the-scenes photographs.
- Six bounded sample positions cover 10–70% of a source; three are initially shown. Thumbnails contain timestamp captions and use contain fitting rather than cropping the film frame. Enlarging a still uses the accessible existing modal.
- **Trailer** is beside the main title-card play/request actions, including provider details, matched local files and local series pages. It opens an accessible, responsive modal rather than an inline section below the credits. Video keys remain provider-supplied; multiple trailers can be selected within the modal.
- Metadata and YouTube are loaded only after the trailer click. Closing/unmounting, profile/session shutdown, browser history navigation or hiding the document removes the iframe. Merely moving controller focus must not close it: the old `home-navigation-interaction` event represents every controller command, not just page changes.

## Source/cache/security

Local still routes stay under authenticated `/api/media/{id}` access. The still service rechecks the current account/library permission before using cache/decoding and before publishing. The image response additionally revalidates the login session. Responses are `private, no-store` to avoid cross-profile browser cache reuse; server-side JPEGs remain cached.

Source resolution/probing uses existing filesystem boundaries and fingerprints. A changed file invalidates the old descriptor; changes during extraction prevent publishing the result. The original video and manually chosen poster/artwork are never overwritten. Cache lives only under managed thumbnails, is claimed with an explicit marker, capped at **256 MiB**, and only known owned JPEG/temporary names may be pruned. Unknown/unmarked cache contents are retained and refused.

One generator, a bounded 30-second queue wait, one output frame, one encoding thread, a 25-second process deadline and 2-MiB output limit prevent unbounded work. A failed/cancelled request removes only its temporary JPEG. Existing background update admission also sees the active request.

HDR10/HLG uses the existing software SDR tone-map filter if its real filter probe succeeds. Only the already recognised single-layer P8.1 HDR10-compatible DV base may enter that base-colour path; P5/P7/unknown DV is refused rather than producing misleading colours. This is not full DV rendering or a new HWA claim. JPEG-specific HLG/P8.1 fixture qualification is recorded separately from ordinary HDR10 proof; unimplemented profiles remain open.

`trailersOnly=true` avoids fetching unused provider galleries while keeping the earlier full-media API response backward compatible. Provider identity, profile eligibility, strict YouTube keys, privacy-enhanced embedding, no autoplay and native YouTube controls/terms are unchanged. No IMDb photos, film/ROM downloads, scraped dataset, new provider dependency or image-generation service is adopted.

## Verification

- **7 focused backend checks passed, zero failed/skipped**: existing trailer/gallery contracts plus scene position bounds, restricted single-frame arguments, DV refusal/base eligibility, native SDR JPEG generation/cache/source/manual-poster preservation/denial/fingerprint change, and native HDR10→SDR JPEG output/unclaimed-cache refusal. Native checks used the already pinned isolated Windows Jellyfin-FFmpeg tools and synthetic local videos; no internet media was downloaded.
- Backend/regression compilation passed with **zero warnings/errors**. Frontend/worker/PWA/localization build and **2 Node media contracts** passed. Source Gitleaks/diff checks passed with zero secret findings.
- **2026-10-27 follow-up:** native HLG and original P8.1 HDR10-compatible DV fixtures generate SDR JPEGs without changing source bytes; JPEGs are inspected directly with ffprobe (the application duration-based video probe intentionally does not describe still images). Concurrent requests share a single JPEG, marked owned-cache eviction/abandoned-temp cleanup preserves unknown user files, cancelled/permission-revoked waiters cannot publish or retain the writer, and authenticated cached-image/descriptor HTTP routes deny revoked logins with private/no-store responses. These checks passed in the **53-check release component batch**; counts overlap earlier checks and are not summed.
- Linux/pinned-runtime execution of the new JPEG tests remains in the release-image workflow. Real modal/controller/iframe behaviour, sustained cache stress and device/colour-profile parity remain user acceptance; no broad DV/HWA claim follows from synthetic SDR output.
- No automated browser suite or device acceptance was run; the user owns the final PC/TV/modal/controller assessment. Current 0.47 household screens do not contain these unpublished changes.

## References

Reviewed **2026-10-26**. Context7 was requested but quota was exhausted; official documents and installed types were used.

- [IMDb image categories](https://help.imdb.com/article/contribution/images-videos/images/G27LPBQZA7EAZGJW): still frames, behind-the-scenes, publicity and promotional artwork are distinct.
- [IMDb image reuse restrictions](https://help.imdb.com/article/imdb/general-information/can-i-use-a-photo-or-video-from-your-site-for-my-web-site-or-publication/G7V6PHXKDLLK7AS6): IMDb generally cannot sublicense provider photographs.
- [TMDB movie images](https://developer.themoviedb.org/reference/movie-images): backdrops, posters and logos, not a reliable still/set-photo classification.
- [HeroUI v3 Modal](https://heroui.com/en/docs/react/components/modal), installed `modal.d.ts`, existing focus/controller-aware `Dialog` wrapper.
- Existing Home `MediaAnalysisService`, read-only source boundaries, bounded FFmpeg process runner and `EncodingSupport.ToneMapFilter`.

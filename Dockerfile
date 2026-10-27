# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM node:24-bookworm-slim@sha256:55fb5f5a527ea2c5c4a95f963e48ef90aa38c7b346054fdc8ee9957a2b55d205 AS node-tools
FROM node-tools AS web
WORKDIR /src
COPY package.json package-lock.json ./
COPY web/package.json web/package.json
RUN npm ci
COPY web/ web/
COPY tools/build-pwa.mjs tools/build-native-reader.mjs tools/ui-*.mjs tools/ui-*.d.mts tools/
COPY docs/licenses/ docs/licenses/
RUN npm run build

FROM node-tools AS updater
COPY tools/stage-container-updater.mjs /stage-container-updater.mjs
RUN node /stage-container-updater.mjs /watchtower.tar.gz \
    && mkdir /updater && tar -xzf /watchtower.tar.gz -C /updater watchtower LICENSE.md \
    && cp /watchtower.tar.gz.json /updater/source.json \
    && chmod 0555 /updater/watchtower && /updater/watchtower --help >/dev/null

FROM ghcr.io/lumax90/homeos:0.43.0@sha256:017e90f2860d2f76d1a787ee7476f5c6244da4aef351f94c733f965bde9bdba0 AS native-runtime

FROM node-tools AS selkies-package
WORKDIR /src
COPY deploy/selkies/runtime.json deploy/selkies/runtime.json
COPY tools/stage-selkies.mjs tools/stage-selkies.mjs
RUN node tools/stage-selkies.mjs /selkies.deb

FROM node-tools AS media-package
WORKDIR /src
COPY deploy/media/runtime.json deploy/media/runtime.json
COPY tools/stage-media-runtime.mjs tools/stage-media-runtime.mjs
RUN node tools/stage-media-runtime.mjs /jellyfin-ffmpeg.deb linux-x64 \
    && node tools/stage-media-runtime.mjs /jellyfin-ffmpeg-source.tar.gz source

FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS media-build
ARG TARGETARCH
RUN test "$TARGETARCH" = amd64 \
    && sed -i 's/^Types: deb$/Types: deb deb-src/' /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get update && apt-get install -y --no-install-recommends \
       build-essential pkg-config nasm ca-certificates libx264-dev libx265-dev libvpx-dev libopus-dev \
       libmp3lame-dev libdav1d-dev libvorbis-dev libass-dev libzimg-dev libgnutls28-dev \
       zlib1g-dev libxml2-dev libfreetype6-dev libfontconfig1-dev libfribidi-dev libharfbuzz-dev
COPY --from=media-package /jellyfin-ffmpeg-source.tar.gz /jellyfin-ffmpeg-source.tar.gz
COPY deploy/media/build-runtime.sh /build-media-runtime.sh
RUN bash /build-media-runtime.sh /jellyfin-ffmpeg-source.tar.gz

FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS selkies-build
ARG TARGETARCH
RUN test "$TARGETARCH" = amd64 && apt-get update && apt-get install -y --no-install-recommends \
    build-essential pkg-config python3 libavcodec-dev libavformat-dev libavutil-dev libswscale-dev libswresample-dev \
    libx11-dev libxext-dev libxrandr-dev libxinerama-dev libxi-dev libgl1-mesa-dev libegl1-mesa-dev \
    libx11-xcb-dev libpulse-dev libsdl2-dev zlib1g-dev && rm -rf /var/lib/apt/lists/*
COPY --from=native-runtime /app/NativeRuntime/licenses/corresponding-source.tar.gz /native-source.tar.gz
COPY deploy/selkies/build-frontend.sh /build-frontend.sh
RUN mkdir /native-source && tar -xzf /native-source.tar.gz -C /native-source \
    && bash /build-frontend.sh /native-source /selkies \
    && cp /build-frontend.sh /selkies/licenses/build-frontend.sh

FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS psp-build
ARG TARGETARCH
RUN test "$TARGETARCH" = amd64 && apt-get update && apt-get install -y --no-install-recommends \
    build-essential cmake ninja-build git pkg-config python3 ca-certificates \
    libavcodec-dev libavformat-dev libavutil-dev libavdevice-dev libavfilter-dev libswresample-dev libswscale-dev \
    libgl1-mesa-dev libx11-dev libzstd-dev libpng-dev libfreetype6-dev zlib1g-dev
COPY deploy/psp/build.sh /build-psp.sh
COPY deploy/psp/write-manifest.py /write-psp-manifest.py
COPY deploy/psp/patch-private-memory.py /patch-private-memory.py
COPY deploy/psp/runtime.json /runtime.json
COPY server/HomeServer.Api/NativeCoreCatalog/catalog.json /core-catalog.json
RUN --mount=type=cache,id=home-psp-source-1204,target=/psp-source \
    --mount=type=cache,id=home-psp-build-1204,target=/psp-build \
    bash /build-psp.sh

FROM node-tools AS native-runtime-check
WORKDIR /src
COPY Dockerfile Dockerfile
COPY deploy/native/ deploy/native/
COPY tools/verify-native-runtime-lock.mjs tools/stage-linux-native.mjs tools/patch-linux-native-input.mjs tools/patch-linux-headless-output.mjs tools/patch-native-host-adapter.mjs tools/native-host-options.c tools/write-linux-native-manifest.mjs tools/
COPY server/HomeServer.Api/NativeCoreCatalog/catalog.json server/HomeServer.Api/NativeCoreCatalog/catalog.json
COPY --from=native-runtime /app/NativeRuntime/manifest.json runtime-manifest.json
RUN node tools/verify-native-runtime-lock.mjs > /native-runtime-checked.txt

FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS handheld-build
ARG TARGETARCH
RUN test "$TARGETARCH" = amd64 && apt-get update && apt-get install -y --no-install-recommends \
    build-essential cmake ninja-build git pkg-config python3 ca-certificates \
    libgl1-mesa-dev libegl1-mesa-dev libpcap-dev libssl-dev libzstd-dev
COPY deploy/handheld/build.sh /build-handheld.sh
COPY deploy/handheld/runtime.json /handheld-runtime.json
COPY deploy/handheld/write-manifest.py /write-handheld-manifest.py
COPY deploy/handheld/patch-citra-revision.py /patch-citra-revision.py
COPY server/HomeServer.Api/NativeCoreCatalog/catalog.json /core-catalog.json
RUN --mount=type=cache,id=home-handheld-source-20261027,target=/handheld-source \
    --mount=type=cache,id=home-handheld-build-20261027,target=/handheld-build \
    bash /build-handheld.sh

FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS native-build
ARG TARGETARCH
RUN test "$TARGETARCH" = amd64
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential cmake pkg-config ca-certificates curl git python3 \
    libavcodec-dev libavformat-dev libavutil-dev libswscale-dev libswresample-dev \
    libx11-dev libxext-dev libxrandr-dev libxinerama-dev libxi-dev libgl1-mesa-dev libegl1-mesa-dev \
    zlib1g-dev ffmpeg xvfb xauth libgl1-mesa-dri && rm -rf /var/lib/apt/lists/*
COPY --from=node-tools /usr/local/bin/node /usr/local/bin/node
WORKDIR /src
COPY deploy/native/ deploy/native/
COPY tools/stage-linux-native.mjs tools/patch-linux-native-input.mjs tools/patch-linux-headless-output.mjs tools/patch-native-host-adapter.mjs tools/native-host-options.c tools/write-linux-native-manifest.mjs tools/
COPY server/HomeServer.Api/NativeCoreCatalog/ server/HomeServer.Api/NativeCoreCatalog/
COPY tests/native/ tests/native/
RUN --mount=type=cache,id=home-native-sources,target=/native-cache \
    cache="/native-cache/$(sha256sum deploy/native/sources.json | cut -c1-16)" \
    && node tools/stage-linux-native.mjs "$cache" && mkdir /native-source && cp -a "$cache/." /native-source/
RUN apt-get update && apt-get install -y --no-install-recommends libx11-xcb-dev && rm -rf /var/lib/apt/lists/*
RUN bash deploy/native/build.sh /native-source /native

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
ARG TARGETARCH
WORKDIR /src
COPY Directory.Build.props ./
COPY server/ server/
COPY web/src/music/sceneSources.json web/src/music/sceneSources.json
COPY docs/licenses/ docs/licenses/
COPY --from=web /src/server/HomeServer.Api/wwwroot/ server/HomeServer.Api/wwwroot/
RUN --mount=type=cache,id=home-nuget,target=/root/.nuget/packages \
    test "$TARGETARCH" = amd64 && dotnet publish server/HomeServer.Api/HomeServer.Api.csproj -c Release -r linux-x64 --self-contained true -o /publish --nologo
COPY tools/Home.NativeLinuxProbe/ tools/Home.NativeLinuxProbe/
COPY tests/native/ tests/native/
RUN --mount=type=cache,id=home-nuget,target=/root/.nuget/packages \
    dotnet publish tools/Home.NativeLinuxProbe/Home.NativeLinuxProbe.csproj -c Release -r linux-x64 --self-contained true -o /native-probe --nologo

FROM build AS media-tests
COPY --from=media-build /app/MediaRuntime/ /app/MediaRuntime/
COPY --from=media-build /media-runtime-dependencies.txt /tmp/media-runtime-dependencies.txt
COPY deploy/media/runtime-dependencies.sh /tmp/media-runtime-dependencies.sh
RUN bash /tmp/media-runtime-dependencies.sh /tmp/media-runtime-dependencies.txt
COPY tests/HomeServer.Tests/ tests/HomeServer.Tests/
ENV HOME_TEST_FFMPEG=/app/MediaRuntime/bin/ffmpeg \
    HOME_TEST_FFPROBE=/app/MediaRuntime/bin/ffprobe
RUN --mount=type=cache,id=home-nuget,target=/root/.nuget/packages \
    dotnet test tests/HomeServer.Tests/HomeServer.Tests.csproj -c Release \
    --filter 'FullyQualifiedName~MediaStillsTests|FullyQualifiedName~MediaRuntimeCompatibilityTests|FullyQualifiedName~DolbyVisionBaseLayerTests|FullyQualifiedName~DolbyVisionAnalysisTests|FullyQualifiedName~PlaybackEfficiencyTests|FullyQualifiedName~PlaybackLifecycleTests|FullyQualifiedName~PlaybackWindowTests|FullyQualifiedName~PlaybackQuotaTests' \
    --logger 'trx;LogFileName=media-runtime.trx' --results-directory /media-runtime-reports --nologo \
    && printf '%s\n' 'Pinned Jellyfin-FFmpeg: native compatibility/base-layer/preservation/lifecycle checks passed.' > /media-runtime-tested.txt

FROM scratch AS verification-tools
COPY --from=build /native-probe/ /native-probe/

FROM native-build AS native-proof
COPY --from=build /native-probe/ /native-probe/
RUN chown -R 1654:1654 /native
USER 1654:1654
RUN /native-probe/Home.NativeLinuxProbe --verify-build /native /native-test-binaries/synthetic_gpu.so

FROM psp-build AS psp-source
RUN sed -i 's/^Types: deb$/Types: deb deb-src/' /etc/apt/sources.list.d/ubuntu.sources && apt-get update
COPY deploy/psp/package-source.py /package-psp-source.py
COPY deploy/psp/verify-source.py /verify-psp-source.py
RUN python3 /package-psp-source.py /app/PspRuntime \
    && python3 /verify-psp-source.py /app/PspRuntime > /app/PspRuntime/licenses/source-proof.json

FROM psp-source AS psp-proof
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg libopengl0 libxrandr2 libxinerama1 libxi6 && rm -rf /var/lib/apt/lists/*
COPY --from=native-runtime /app/NativeRuntime/ /app/NativeRuntime/
RUN ldd /app/NativeRuntime/retroarch > /tmp/frontend-linked.txt && ! grep -q 'not found' /tmp/frontend-linked.txt
COPY --from=build /native-probe/ /native-probe/
COPY tests/native/synthetic_psp.py /original-isolated-psp.py
RUN mkdir /psp-proof && python3 /original-isolated-psp.py /psp-proof/fixture.elf \
    && chown -R 1654:1654 /app/PspRuntime /psp-proof
USER 1654:1654
RUN /native-probe/Home.NativeLinuxProbe --verify-psp /app/NativeRuntime /app/PspRuntime /psp-proof/fixture.elf /psp-proof/evidence \
    || { cat /psp-proof/evidence/engine.log; exit 1; }

FROM handheld-build AS handheld-source
RUN sed -i 's/^Types: deb$/Types: deb deb-src/' /etc/apt/sources.list.d/ubuntu.sources && apt-get update
COPY deploy/handheld/package-source.py /package-handheld-source.py
COPY deploy/handheld/verify-source.py /verify-handheld-source.py
RUN python3 /package-handheld-source.py /app/HandheldRuntime \
    && python3 /verify-handheld-source.py /app/HandheldRuntime > /app/HandheldRuntime/licenses/source-proof.json

FROM handheld-source AS handheld-proof
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg xvfb xauth libgl1-mesa-dri libxrandr2 libxinerama1 libxi6 && rm -rf /var/lib/apt/lists/*
COPY --from=native-runtime /app/NativeRuntime/ /app/NativeRuntime/
RUN ldd /app/NativeRuntime/retroarch > /tmp/frontend-linked.txt && ! grep -q 'not found' /tmp/frontend-linked.txt
COPY --from=build /native-probe/ /native-probe/
COPY tests/native/synthetic_nds.py tests/native/synthetic_3ds.py /handheld-fixtures/
RUN mkdir -p /handheld-proof/ds /handheld-proof/3ds \
    && python3 /handheld-fixtures/synthetic_nds.py /handheld-proof/fixture.nds \
    && python3 /handheld-fixtures/synthetic_3ds.py /handheld-proof/fixture.3dsx \
    && chown -R 1654:1654 /app/HandheldRuntime /handheld-proof
USER 1654:1654
RUN /native-probe/Home.NativeLinuxProbe --verify-handheld /app/NativeRuntime /app/HandheldRuntime desmume /handheld-proof/fixture.nds /handheld-proof/ds \
    && /native-probe/Home.NativeLinuxProbe --verify-handheld /app/NativeRuntime /app/HandheldRuntime citra /handheld-proof/fixture.3dsx /handheld-proof/3ds

FROM handheld-proof AS handheld-selkies-proof
USER 0:0
COPY --from=selkies-package /selkies.deb /tmp/selkies.deb
RUN apt-get update && apt-get install -y --no-install-recommends pulseaudio libsdl2-2.0-0 /tmp/selkies.deb && rm -rf /var/lib/apt/lists/* && rm /tmp/selkies.deb
COPY --from=selkies-build /selkies/ /app/SelkiesRuntime/
COPY deploy/selkies/runtime.json /app/SelkiesRuntime/source.json
COPY deploy/selkies/write-manifest.py /app/SelkiesRuntime/write-manifest.py
RUN /opt/selkies/bin/python3 /app/SelkiesRuntime/write-manifest.py /app/SelkiesRuntime /app/NativeRuntime/manifest.json \
    && mkdir /handheld-selkies-proof && chown 1654:1654 /handheld-selkies-proof
USER 1654:1654
RUN /native-probe/Home.NativeLinuxProbe --verify-handheld-selkies /app/NativeRuntime /app/HandheldRuntime desmume /handheld-proof/fixture.nds /handheld-selkies-proof/ds /app/SelkiesRuntime \
    && /native-probe/Home.NativeLinuxProbe --verify-handheld-selkies /app/NativeRuntime /app/HandheldRuntime citra /handheld-proof/fixture.3dsx /handheld-selkies-proof/3ds /app/SelkiesRuntime

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.12-noble AS final
WORKDIR /app
COPY --from=selkies-package /selkies.deb /tmp/home-selkies.deb
RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg par2 fonts-dejavu-core xvfb xauth libgl1-mesa-dri libgl1 libopengl0 libegl1 libxrandr2 libxinerama1 libxi6 libpcap0.8t64 pulseaudio libsdl2-2.0-0 /tmp/home-selkies.deb \
    && mkdir -p /data /app/licenses \
    && ffmpeg -L > /app/licenses/ffmpeg-build.txt 2>&1 \
    && dpkg-query -W > /app/licenses/system-packages.txt \
    && cp /usr/share/doc/ffmpeg/copyright /app/licenses/ffmpeg-copyright.txt \
    && cp /usr/share/doc/par2/copyright /app/licenses/par2-copyright.txt \
    && cp /usr/share/doc/libsdl2-2.0-0/copyright /app/licenses/SDL2-copyright.txt \
    && chown 1654:1654 /data && rm -rf /var/lib/apt/lists/* && rm /tmp/home-selkies.deb
COPY --from=build /publish/ /app/
COPY --from=psp-proof --chown=0:0 /app/PspRuntime/ /app/PspRuntime/
COPY --from=handheld-selkies-proof --chown=0:0 /app/HandheldRuntime/ /app/HandheldRuntime/
RUN ldd /app/PspRuntime/ppsspp_libretro.so > /app/PspRuntime/licenses/final-linked-libraries.txt \
    && ! grep -q 'not found' /app/PspRuntime/licenses/final-linked-libraries.txt
RUN ldd /app/HandheldRuntime/cores/desmume_libretro.so > /app/HandheldRuntime/licenses/desmume-final-linked.txt \
    && ldd /app/HandheldRuntime/cores/citra_libretro.so > /app/HandheldRuntime/licenses/citra-final-linked.txt \
    && ! grep -q 'not found' /app/HandheldRuntime/licenses/*-final-linked.txt
COPY --from=media-build /app/MediaRuntime/ /app/MediaRuntime/
COPY --from=media-build /media-runtime-dependencies.txt /app/MediaRuntime/licenses/runtime-dependencies.txt
COPY deploy/media/runtime-dependencies.sh /app/MediaRuntime/runtime-dependencies.sh
RUN bash /app/MediaRuntime/runtime-dependencies.sh /app/MediaRuntime/licenses/runtime-dependencies.txt
COPY deploy/media/runtime.json /app/MediaRuntime/source.json
COPY deploy/media/install.sh /app/MediaRuntime/install.sh
COPY deploy/media/verify-source.sh /app/MediaRuntime/verify-source.sh
COPY --from=media-package /jellyfin-ffmpeg-source.tar.gz /app/MediaRuntime/licenses/upstream-source.tar.gz
COPY --from=media-tests /media-runtime-tested.txt /app/MediaRuntime/test-proof.txt
RUN bash /app/MediaRuntime/install.sh
COPY --from=native-runtime --chown=0:0 /app/NativeRuntime/ /app/NativeRuntime/
COPY --from=native-runtime-check /native-runtime-checked.txt /app/NativeRuntime/runtime-lock-check.txt
COPY --from=selkies-build /selkies/ /app/SelkiesRuntime/
COPY deploy/selkies/runtime.json /app/SelkiesRuntime/source.json
COPY deploy/selkies/write-manifest.py /app/SelkiesRuntime/write-manifest.py
RUN /opt/selkies/bin/python3 /app/SelkiesRuntime/write-manifest.py /app/SelkiesRuntime /app/NativeRuntime/manifest.json /app/HandheldRuntime/manifest.json
COPY --from=updater --chown=0:0 /updater/ /app/Watchtower/
COPY --chmod=0555 deploy/casaos/prepare-data.sh /app/prepare-data.sh
COPY --chmod=0555 deploy/casaos/managed-updater.py /app/managed-updater.py
LABEL org.opencontainers.image.source="https://github.com/lumax90/homeOS" \
    org.opencontainers.image.title="Home" \
    org.opencontainers.image.description="Personal media, Live TV and headless server-run games for your home network"
ARG HOME_SOURCE_REVISION=local
ENV Home__DataPath=/data \
    HOME_SOURCE_REVISION=$HOME_SOURCE_REVISION \
    Home__FfmpegPath=/app/MediaRuntime/bin/ffmpeg \
    Home__FfprobePath=/app/MediaRuntime/bin/ffprobe \
    Home__Par2Path=/usr/bin/par2 \
    Home__Games__NativeRuntimePath=/app/NativeRuntime \
    Home__Games__SelkiesRuntimePath=/app/SelkiesRuntime \
    Home__Games__PspRuntimePath=/app/PspRuntime \
    Home__Games__HandheldRuntimePath=/app/HandheldRuntime \
    Home__Network__BindAddress=0.0.0.0 \
    Home__Network__HttpPort=8080 \
    URLS=http://0.0.0.0:8080 \
    DOTNET_EnableDiagnostics=0
USER 1654:1654
VOLUME ["/data"]
EXPOSE 8080/tcp 62405/udp 62405/tcp
HEALTHCHECK --interval=30s --timeout=6s --start-period=45s --retries=3 CMD ["./HomeServer.Api", "--healthcheck"]
ENTRYPOINT ["./HomeServer.Api"]

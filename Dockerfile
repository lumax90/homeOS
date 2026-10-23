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
COPY docs/licenses/ docs/licenses/
COPY --from=web /src/server/HomeServer.Api/wwwroot/ server/HomeServer.Api/wwwroot/
RUN --mount=type=cache,id=home-nuget,target=/root/.nuget/packages \
    test "$TARGETARCH" = amd64 && dotnet publish server/HomeServer.Api/HomeServer.Api.csproj -c Release -r linux-x64 --self-contained true -o /publish --nologo
COPY tools/Home.NativeLinuxProbe/ tools/Home.NativeLinuxProbe/
COPY tests/native/ tests/native/
RUN --mount=type=cache,id=home-nuget,target=/root/.nuget/packages \
    dotnet publish tools/Home.NativeLinuxProbe/Home.NativeLinuxProbe.csproj -c Release -r linux-x64 --self-contained true -o /native-probe --nologo

FROM scratch AS verification-tools
COPY --from=build /native-probe/ /native-probe/

FROM native-build AS native-proof
COPY --from=build /native-probe/ /native-probe/
RUN chown -R 1654:1654 /native
USER 1654:1654
RUN /native-probe/Home.NativeLinuxProbe --verify-build /native /native-test-binaries/synthetic_gpu.so

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.12-noble AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg par2 fonts-dejavu-core xvfb xauth libgl1-mesa-dri libgl1 libegl1 libxrandr2 libxinerama1 libxi6 \
    && mkdir -p /data /app/licenses \
    && ffmpeg -L > /app/licenses/ffmpeg-build.txt 2>&1 \
    && dpkg-query -W > /app/licenses/system-packages.txt \
    && cp /usr/share/doc/ffmpeg/copyright /app/licenses/ffmpeg-copyright.txt \
    && cp /usr/share/doc/par2/copyright /app/licenses/par2-copyright.txt \
    && chown 1654:1654 /data && rm -rf /var/lib/apt/lists/*
COPY --from=build /publish/ /app/
COPY --from=native-proof --chown=0:0 /native/ /app/NativeRuntime/
COPY --from=updater --chown=0:0 /updater/ /app/Watchtower/
COPY --chmod=0555 deploy/casaos/prepare-data.sh /app/prepare-data.sh
LABEL org.opencontainers.image.source="https://github.com/lumax90/homeOS" \
    org.opencontainers.image.title="Home" \
    org.opencontainers.image.description="Personal media, Live TV and headless server-run games for your home network"
ARG HOME_SOURCE_REVISION=local
ENV Home__DataPath=/data \
    HOME_SOURCE_REVISION=$HOME_SOURCE_REVISION \
    Home__FfmpegPath=/usr/bin/ffmpeg \
    Home__FfprobePath=/usr/bin/ffprobe \
    Home__Par2Path=/usr/bin/par2 \
    Home__Games__NativeRuntimePath=/app/NativeRuntime \
    Home__Network__BindAddress=0.0.0.0 \
    Home__Network__HttpPort=8080 \
    URLS=http://0.0.0.0:8080 \
    DOTNET_EnableDiagnostics=0
USER 1654:1654
VOLUME ["/data"]
EXPOSE 8080/tcp 62405/udp 62405/tcp
HEALTHCHECK --interval=30s --timeout=6s --start-period=45s --retries=3 CMD ["./HomeServer.Api", "--healthcheck"]
ENTRYPOINT ["./HomeServer.Api"]

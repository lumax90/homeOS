# Home

**Your collection. Your server.**

Home is a self-hosted media and retro-gaming server with one web interface for your household. Organize your collection, stream over your home network, and keep your accounts, settings and game saves on your own server.

## What you can do

- Browse movies and TV series, choose seasons and episodes, and resume where you left off.
- Listen to music, audiobooks, podcasts and radio; browse your books and photos.
- Play supported retro games in your browser using your own ROMs and any required BIOS files.
- Watch live TV/IPTV from sources you are authorized to access.
- Use household profiles, parental controls, subtitles, backups and an English or Turkish interface.
- Connect optional metadata, subtitle and download sources. Playback support depends on the format, browser and server hardware.

## Get started

For Intel/AMD x64 CasaOS, follow the [installation and update guide](deploy/casaos/README.md). The container is available at **`ghcr.io/lumax90/homeos:stable`**.

Keep your existing `/data` storage and media mounts when upgrading. Check the [release and feature status](docs/product/execution.md) before applying a new update recipe. Browser features such as gamepad access and PWA installation may require trusted HTTPS.

## Responsible use

Home is intended for collections you own or are authorized to use. Your files remain under your control; Home claims no ownership of them, and copyright remains with the respective rights holders. Home does not supply films, commercial game ROMs or console BIOS files.

You are responsible for having the rights to acquire, copy, stream and share your content, and for following applicable laws and provider terms. Optional BitTorrent and Usenet tools are for lawful transfers; Home does not encourage piracy or unauthorized downloads. Using Home does not grant content or redistribution rights.

Home's project-wide distribution license is still under review. Third-party components retain their own licenses and notices; see the [dependency and license inventory](docs/dependencies.md).

## Development

Built with **C# / ASP.NET Core (.NET 10 LTS), SQLite, React, TypeScript and HeroUI**. Development requires the .NET 10 SDK, Node.js 24 LTS, FFmpeg and ffprobe.

```sh
npm ci
npm run build
dotnet run --project server/HomeServer.Api --no-launch-profile
```

See the [deployment options](deploy/README.md), [architecture](docs/architecture.md) and [API documentation](docs/api.md). For bugs or suggestions, [open an issue](https://github.com/lumax90/homeOS/issues).

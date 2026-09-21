# Instagram embeds — personal instance

Personal fork of [Lainmode/InstagramEmbed-vxinstagram](https://github.com/Lainmode/InstagramEmbed-vxinstagram).
The .NET app generates chat previews and redirects clients to Instagram media URLs.
It does not download, store, merge or proxy media, and it does not call SnapSave.

## Extraction

- Public `/reel/CODE/`, `/reels/CODE/`, `/tv/CODE/`: yt-dlp metadata-only extraction, with Instaloader fallback.
- Public `/p/CODE/` and `/username/p/CODE/`: Instaloader, preserving image/video carousel order.
- Append `/2/` or `?img_index=2` to select the second item (both are one-based).
- Stories, private/login-required posts, profile feeds and `/share/...` short links are unsupported.
  Open share links on Instagram first and use the resulting post/reel URL.
- A failed album extraction does not fall back to a partial, video-only album. Force `Extractor__Provider=yt-dlp`
  only when comparing extraction for known single videos; playlist results are rejected.

Metadata lives in memory for five minutes; failures are cached for 30 seconds. The cache has a 32 MiB estimated
entry budget. Two subprocesses run at once, with a 40-second end-to-end deadline, a two-second worker wait and
bounded output. The app allows up to 32 concurrent HTTP requests. Media routes carry post type and shortcode,
so cache eviction/restarts do not lose their source. Redirects and embed pages send `Cache-Control: no-store`.
Old `/offload/...` media links remain best-effort compatible through `/p/` lookup.

Instagram can block anonymous extraction or return URLs Discord cannot use. A successful health check or extraction
does not prove Discord playback. No login session, proxy pool, paid API, streaming fallback or disk cache is configured.
MP4 selection excludes fragment-based and known audio-only/video-only formats; it prefers H.264 where reported.
No transcoding means unsupported formats fail explicitly. Some extractor formats omit codec/audio metadata,
so those direct MP4 URLs still need client verification. Unknown image dimensions are omitted instead of invented.
Discord album discovery retains the upstream Mastodon-style mechanism; its rendering is client-dependent.

## Build and deploy

See [deployment instructions](deploy/README.md) for GHCR, the existing InvenTree Compose stack, Caddy and Cloudflare.
The only Dockerfile is at the repository root:

```sh
docker build --pull -t vxinstagram:local .
docker compose -f InstagramEmbedForDiscord/docker-compose.yml up -d --build
```

The local sample binds only to localhost. Production shares the InvenTree network and publishes no host port.
The image runs as the .NET image's non-root user and includes Python, pinned yt-dlp/Instaloader dependencies and
the MIT license. No Node service or ffmpeg is installed. The installed Python distributions retain their license files.

## Configuration

| Environment variable | Default | Purpose |
| --- | --- | --- |
| `Instance__PublicUrl` | `http://localhost:8080` | Public origin; set `https://ig.joel.zip` behind Caddy. No path/query. |
| `Instance__Name` | `Instagram embeds` | Page and provider name. |
| `Instance__ShowDetails` | `true` | Include author/caption when available. |
| `Extractor__Provider` | `auto` | `auto`, `yt-dlp`, or `instaloader`. |
| `Extractor__Python` | `python3` (image overrides it) | Python executable with installed requirements. |
| `Extractor__CacheSeconds` | `300` | Metadata TTL, clamped to 10–900 seconds. |
| `Extractor__TimeoutSeconds` | `40` | Total extraction deadline, clamped to 5–90 seconds. |
| `Extractor__Concurrency` | `2` | Subprocess limit, clamped to 1–4. |
| `AllowedHosts` | `*` | ASP.NET host allowlist; production sample restricts it. |

The public origin is explicit: request Host/forwarding headers cannot alter generated media URLs.
Only validated Instagram post kinds/shortcodes reach the subprocess, through separate arguments without a shell.
Returned media URLs must use HTTPS on Instagram/Meta CDN domains. Raw extractor output is not logged.

## Validation

```sh
python -m unittest discover -s tests -p 'test_*.py' -v
dotnet publish InstagramEmbedForDiscord/InstagramEmbed.Application.csproj -c Release -o work/publish
python tests/integration.py work/publish/InstagramEmbed.Application.dll
```

The tests use deterministic fake metadata/subprocess responses, not Instagram. They exercise album order, URL validation,
safe format selection, redirect/HEAD behavior, HTML escaping, public-origin handling, removed endpoints, metadata caching,
single-flight requests, output limits and timeouts. GitHub Actions additionally builds the image, checks installed Python
dependencies and smoke-tests the read-only container before publishing that exact image. Live acceptance steps are in the deployment guide.

## Attribution

Original UI/backend: [Lainmode](https://github.com/Lainmode). Original repository history is retained.
The original [MIT license and copyright](LICENSE) remain unchanged and are copied into the container at `/app/LICENSE`.
This fork replaces the former SnapSave integration with [yt-dlp](https://github.com/yt-dlp/yt-dlp)
(Unlicense) and [Instaloader](https://github.com/instaloader/instaloader) (MIT).
Bundled frontend libraries retain their original license notices. Donation solicitation and upstream promotional branding
are removed from the application; source attribution remains in this README and a small homepage footer.

# Deploy on the existing OVH / InvenTree stack

## Recommended: GitHub Actions builds, VPS pulls a digest

GHCR keeps compilers and build load off the VPS. The workflow uses GitHub's `GITHUB_TOKEN` with
`packages: write`; no SSH key or VPS credential goes into GitHub. Pull requests run tests without publishing.
After merge/push to `master` (or a manual run on `master`), a successful workflow publishes the **tested image**
as `ghcr.io/b4shful/vxinstagram:sha-FULL_COMMIT_SHA`. It records the digest reference in the run summary.
Only linux/amd64 is built by the current Ubuntu runner. Check `uname -m` on the VPS: this is appropriate for
`x86_64`; an ARM VPS needs an ARM build before using this workflow.

One-time GitHub setup:

1. Push the implementation branch and merge it into the fork's `master` after checks pass.
2. Enable Actions in the fork if GitHub has disabled inherited workflows. Repository/organization policies must
   allow the workflow's package-write permission and the referenced actions.
3. The first GHCR package is normally private. Keep it private and authenticate pulls, or explicitly make the
   **container package** public in package settings for anonymous pulls. That visibility change is a user choice;
   it is not performed by the workflow. Neither image variant should contain secrets.
4. For a private package, run `docker login ghcr.io -u b4shful` interactively on the VPS and enter a classic PAT
   with `read:packages`. Do not put it in Compose, the repository, or command-line arguments. Use the same OS
   user for login and subsequent pulls (including whether you use `sudo`).

Versions in `extractor/requirements.txt` include transitive Python dependencies. Dependabot proposes updates;
review/merge them to rebuild. The .NET/Alpine base tags and GitHub action major tags are mutable, so rebuilding
the same source later is not promised to be byte-for-byte identical. **Deploy by digest** for exact rollback.
The existing .NET 8 target is retained; plan a supported runtime upgrade as its support window ends.
No runtime self-update, auto-merge, automatic VPS deployment or image-pruning job is configured.

## Compose integration

Use the InvenTree directory containing the existing `docker-compose.yml`, `.env` and `Caddyfile`.
Merge the `vxinstagram` service from [compose.vxinstagram.yml](compose.vxinstagram.yml) into your existing
`docker-compose.override.yml`, replacing the old vxinstagram service block. Preserve any other overrides.
If the override does not exist, copy the sample to that filename.

The service joins the default network used by `inventree-proxy`, exposes port 8080 only inside Docker,
and has no media volume. Its writable `/tmp` is a bounded RAM filesystem for runtime scratch data, not a video cache.
It does not need access to InvenTree's database, Redis, `.env` credentials or data volumes.

Append these values to the stack's `.env`, substituting the real digest from the Actions run summary:

```dotenv
VXINSTAGRAM_IMAGE=ghcr.io/b4shful/vxinstagram@sha256:REPLACE_WITH_REAL_DIGEST
VXINSTAGRAM_PUBLIC_URL=https://ig.joel.zip
VXINSTAGRAM_NAME=Instagram embeds
VXINSTAGRAM_PROVIDER=auto
```

In the stack directory:

```sh
docker compose config --quiet
docker compose pull vxinstagram
docker compose up -d --no-deps vxinstagram
docker compose ps vxinstagram
docker compose logs --tail=80 vxinstagram
```

These commands assume the normal automatically merged Compose filenames. If you use explicit `-f` flags,
include your existing base and override files consistently. Alternatively keep the sample as a separate overlay
and append `-f /path/to/compose.vxinstagram.yml` to **all** your normal Compose commands; do not omit existing overrides.
If the proxy has been moved onto a custom network since the supplied configuration, attach vxinstagram to that
same network. Do not publish another host port or start another reverse proxy.

## Caddy and Cloudflare

Append [Caddyfile.snippet](Caddyfile.snippet) to the existing Caddyfile. It reuses your `log_common` snippet and
Cloudflare origin certificate mounts. The origin certificate must cover `ig.joel.zip`.

```sh
docker compose exec inventree-proxy caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
docker compose exec inventree-proxy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile
docker compose exec inventree-proxy wget -qO- http://vxinstagram:8080/healthz
```

Keep the DNS record pointing to the OVH VPS and Full (strict) TLS. Discord must reach the hostname without
Cloudflare Access login or an interactive challenge. Avoid a cache-everything rule that overrides the application's
`no-store` response on embed/media routes. This version redirects media traffic to Instagram; it does not serve video
bytes through Cloudflare or the VPS. Existing InvenTree and Sage site blocks remain independent.

## Acceptance checks before replacing a known-working deployment

Use one public reel, one photo and one mixed photo/video album you can also view anonymously. Test using your
actual post codes, not literal example placeholders:

```sh
curl --fail https://ig.joel.zip/healthz
curl --fail -A Discordbot https://ig.joel.zip/reel/REAL_REEL_CODE/
curl --fail -A Discordbot https://ig.joel.zip/p/REAL_PHOTO_CODE/
curl --fail -A Discordbot https://ig.joel.zip/p/REAL_ALBUM_CODE/2/
curl -I https://ig.joel.zip/media/reel/REAL_REEL_CODE/0
```

The embed HTML should contain your origin in `og:video`/`og:image` URLs. A media HEAD request should give a
302 redirect to an HTTPS Instagram/Meta CDN address, with `Cache-Control: no-store`. Treat signed Location URLs
as temporary; don't paste them into public logs. Do not run `curl -L` unless you intend to fetch the media.

Then paste the normal `https://ig.joel.zip/reel/.../` and album links into Discord. Confirm actual video playback,
thumbnail/aspect ratio and album selection, not just a generated preview. Add `?test=2` when repeating a Discord test
to avoid its cached preview; this does **not** bypass our five-minute metadata cache. Wait for its expiry or restart
the vxinstagram service to test fresh extraction.

To compare providers on a known single reel, change `VXINSTAGRAM_PROVIDER` to `yt-dlp` or `instaloader`, then
run `docker compose up -d --no-deps vxinstagram`. Restore `auto` afterward. Do not use video-only extraction as
an album fallback. Provider failures return 503 with a 30-second Retry-After; malformed/unsupported routes return 404
or 400, and excessive simultaneous requests may return 429. `/healthz` checks the web app, not Instagram availability.

If extraction returns a URL but Discord cannot play it, direct redirects have reached their limit for that URL.
This implementation does not silently enable proxying or disk storage. The next option is a separately reviewed
on-demand streaming proxy, with VPS bandwidth and proper range/header forwarding considered explicitly.
Likewise, anonymous Instagram blocking may require a session or a different extraction service; no credentials
are collected or baked into this image.

## Updates and rollback

After a tested build, replace only `VXINSTAGRAM_IMAGE` with the new digest and run:

```sh
docker compose pull vxinstagram
docker compose up -d --no-deps vxinstagram
```

Keep the old digest in your deployment notes. Roll back by restoring it and running the same two commands.
No media data migration is needed. Caddy only needs reloading if its configuration changed.

## Fallback: build on the VPS

This avoids GHCR authentication but requires Git, outbound build access, spare CPU/RAM/disk and compilation
on every update. Build from a specific reviewed commit in the fork:

```sh
git clone https://github.com/b4shful/vxinstagram.git /opt/vxinstagram
cd /opt/vxinstagram
git checkout REVIEWED_COMMIT_SHA
docker build --pull -t vxinstagram:REVIEWED_COMMIT_SHA .
```

Set `VXINSTAGRAM_IMAGE=vxinstagram:REVIEWED_COMMIT_SHA` in the InvenTree `.env`, then from that stack directory:

```sh
docker compose up -d --no-deps --pull never vxinstagram
```

Do not use `docker compose pull` for locally built image names. Save the prior local image/tag for rollback.

## Reference material

- [GHCR publishing with GitHub Actions](https://docs.github.com/en/actions/tutorials/publish-packages/publish-docker-images)
- [GitHub Container registry authentication and visibility](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry)
- [yt-dlp direct URL limitations](https://github.com/yt-dlp/yt-dlp/wiki/FAQ#i-extracted-a-video-url-with--g-but-it-does-not-play-on-another-machine--in-my-web-browser)
- [Instaloader post and carousel metadata](https://instaloader.github.io/module/structures.html)

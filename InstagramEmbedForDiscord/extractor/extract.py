"""Resolve public Instagram media URLs. Never download or save media."""
import contextlib
import json
import re
import sys
from urllib.parse import urlsplit


def media_url(value):
    url = urlsplit(value or "")
    host = (url.hostname or "").lower()
    if (url.scheme != "https" or url.username or url.password or url.port not in (None, 443)
            or not any(host == suffix or host.endswith("." + suffix)
                       for suffix in ("cdninstagram.com", "fbcdn.net"))):
        raise ValueError("Unsupported media host")
    return value


def from_ytdlp(info):
    # A filtered video playlist must never masquerade as a complete mixed album.
    if info.get("entries") is not None:
        raise ValueError("Albums require Instaloader")
    extension = info.get("ext") or ("mp4" if urlsplit(info.get("url") or "").path.lower().endswith(".mp4") else "")
    candidates = info.get("formats") or ([dict(info, ext=extension)]
                                         if info.get("url") else [])
    formats = [f for f in candidates
               if f.get("ext") == "mp4" and f.get("protocol") in (None, "https", "http")
               and f.get("vcodec") != "none" and f.get("acodec") != "none"
               and f.get("url", "").startswith("https://")
               and not f.get("fragments") and not f.get("manifest_url")]
    if not formats:
        raise ValueError("No directly playable MP4; merging/transcoding is disabled")
    best = max(formats, key=lambda f: (
        str(f.get("vcodec", "")).startswith(("avc", "h264")),
        f.get("height") or 0, f.get("width") or 0, f.get("tbr") or 0))
    thumb = info.get("thumbnail") or next(
        (t["url"] for t in reversed(info.get("thumbnails") or []) if t.get("url")), "")
    return {
        "AuthorUsername": info.get("channel") or info.get("uploader_id") or "instagram",
        "AuthorName": info.get("uploader"), "Caption": info.get("description"),
        "Likes": info.get("like_count") or 0, "Comments": info.get("comment_count") or 0,
        "Media": [{"Url": media_url(best["url"]), "MediaType": "video",
                   "ThumbnailUrl": media_url(thumb) if thumb else "",
                   "Width": best.get("width") or 0, "Height": best.get("height") or 0}],
    }


def ytdlp_post(kind, shortcode):
    from yt_dlp import YoutubeDL
    with YoutubeDL({"quiet": True, "no_warnings": True, "cachedir": False,
                    "skip_download": True, "socket_timeout": 10, "retries": 0,
                    "extractor_retries": 0, "noplaylist": True}) as ydl:
        info = ydl.extract_info(f"https://www.instagram.com/{kind}/{shortcode}/",
                                download=False, process=False)
    return from_ytdlp(info)


def from_instaloader(post):
    if post.typename == "GraphSidecar":
        nodes = list(post.get_sidecar_nodes())
        media = [{"Url": media_url(n.video_url if n.is_video else n.display_url),
                  "MediaType": "video" if n.is_video else "image",
                  "ThumbnailUrl": media_url(n.display_url)} for n in nodes]
    else:
        media = [{"Url": media_url(post.video_url if post.is_video else post.url),
                  "MediaType": "video" if post.is_video else "image",
                  "ThumbnailUrl": media_url(post.url)}]
    if not media or len(media) > 20:
        raise ValueError("Unsupported media count")
    return {"AuthorUsername": post.owner_username, "Caption": post.caption, "Media": media}


def instaloader_post(kind, shortcode):
    from instaloader import Instaloader, Post
    loader = Instaloader(quiet=True, download_pictures=False, download_videos=False,
                        download_video_thumbnails=False, download_geotags=False,
                        download_comments=False, save_metadata=False,
                        max_connection_attempts=1, request_timeout=10)
    try:
        return from_instaloader(Post.from_shortcode(loader.context, shortcode))
    finally:
        loader.close()


def resolve(kind, shortcode, provider="auto"):
    if kind not in ("p", "reel", "tv") or not re.fullmatch(r"[A-Za-z0-9_-]{1,64}", shortcode):
        raise ValueError("Invalid Instagram post")
    if provider == "yt-dlp":
        return ytdlp_post(kind, shortcode)
    if provider == "instaloader":
        return instaloader_post(kind, shortcode)
    if provider != "auto":
        raise ValueError("Invalid provider")
    # Never silently drop photos when a post or carousel fails extraction.
    if kind == "p":
        return instaloader_post(kind, shortcode)
    try:
        return ytdlp_post(kind, shortcode)
    except Exception:
        return instaloader_post(kind, shortcode)


if __name__ == "__main__":
    try:
        with contextlib.redirect_stdout(sys.stderr):
            result = resolve(*sys.argv[1:])
        print(json.dumps(result, ensure_ascii=True))
    except Exception:
        print("Instagram extraction failed (blocked, unavailable or unsupported).", file=sys.stderr)
        sys.exit(1)

using System.Text.RegularExpressions;
using System.Net;
using InstagramEmbed.Application.Models;
using InstagramEmbed.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace InstagramEmbed.Controllers;

public sealed class HomeController(PostCacheService posts, InstanceSettings instance) : Controller
{
    private static readonly Regex PostPath = new(
        @"^(?:[A-Za-z0-9_.]+/)?(?<kind>p|reel|reels|tv)/(?<id>[A-Za-z0-9_-]{1,64})(?:/(?<order>[0-9]+))?/?$",
        RegexOptions.CultureInvariant);

    [HttpGet("/")]
    public IActionResult HomePage() => View();

    [HttpGet("/healthz")]
    public IActionResult Health() => Ok(new { status = "ok" });

    [HttpGet("/{**path}")]
    public async Task<IActionResult> Index(string? path, [FromQuery(Name = "img_index")] int? imgIndex)
    {
        if (!ModelState.IsValid) return BadRequest("Invalid item number.");
        if (path?.StartsWith("share/", StringComparison.OrdinalIgnoreCase) == true) return NotFound("Open the share link on Instagram and copy its post URL.");
        var match = PostPath.Match(path ?? "");
        if (!match.Success) return NotFound("Use a public /p/CODE/ or /reel/CODE/ link. Stories and share links are not supported.");
        string kind = match.Groups["kind"].Value.Replace("reels", "reel");
        string id = match.Groups["id"].Value;
        int? order = null;
        if (match.Groups["order"].Success)
        {
            if (!int.TryParse(match.Groups["order"].Value, out int parsed) || parsed < 1) return BadRequest("Invalid item number.");
            order = parsed - 1;
        }
        else if (imgIndex.HasValue)
        {
            if (imgIndex < 1) return BadRequest("img_index starts at 1.");
            order = imgIndex.Value - 1;
        }
        var post = await Fetch(kind, id);
        if (post == null) return Unavailable();
        if (order >= post.Media.Count) return NotFound("Item not found.");
        Response.Headers.CacheControl = "no-store";
        return View("Index", new EmbedPage(post, kind, id, order));
    }

    [HttpGet("/media/{kind}/{id}/{order:int}")]
    [HttpHead("/media/{kind}/{id}/{order:int}")]
    public async Task<IActionResult> Media(string kind, string id, int order, bool thumbnail = false)
    {
        if (!ModelState.IsValid) return BadRequest();
        if (!PostCacheService.ValidPost(kind, id) || order < 0 || order >= 20) return NotFound();
        var post = await Fetch(kind, id);
        if (post == null) return Unavailable();
        if (order >= post.Media.Count) return NotFound();
        var media = post.Media[order];
        var url = thumbnail ? media.ThumbnailUrl : media.Url;
        if (!PostCacheService.ValidMediaUrl(url)) return NotFound();
        // Re-resolve expired metadata instead of caching a redirect to a signed URL.
        Response.Headers.CacheControl = "no-store";
        return Redirect(url);
    }

    // Preserve old links already cached by chat clients; old routes lacked post type.
    [HttpGet("/offload/{id}")]
    [HttpGet("/offload/{id}/{order:int}")]
    [HttpGet("/offload/{id}/{order:int}.mp4")]
    public Task<IActionResult> Offload(string id, int? order, bool thumbnail = false) =>
        Media("p", id.EndsWith(".mp4") ? id[..^4] : id, order ?? 0, thumbnail);

    [HttpGet("/oembed")]
    public IActionResult OEmbed(string? username, string? desc) => Json(new {
        version = "1.0", type = "video", title = "",
        author_name = desc ?? username ?? instance.Name,
        author_url = "https://www.instagram.com/" + Uri.EscapeDataString(username ?? ""),
        provider_name = instance.Name, provider_url = instance.BaseUrl
    });

    // Discord's existing Mastodon-style discovery path, with no user-supplied fetch URL.
    [HttpGet("/users/{username}/statuses/{token}")]
    [HttpGet("/api/v1/statuses/{token}")]
    public Task<IActionResult> ActivityToken(string token)
    {
        var parts = token.Split('.');
        return parts.Length == 2 ? Activity(parts[0], parts[1])
            : Task.FromResult<IActionResult>(NotFound());
    }

    [HttpGet("/api/v1/statuses/{kind}/{id}")]
    public async Task<IActionResult> Activity(string kind, string id, int order = 0)
    {
        if (!ModelState.IsValid) return BadRequest();
        if (!PostCacheService.ValidPost(kind, id) || order < 0 || order >= 20) return NotFound();
        var post = await Fetch(kind, id);
        if (post == null) return Unavailable();
        if (order >= post.Media.Count) return NotFound();
        var url = $"https://www.instagram.com/{kind}/{id}/";
        var username = instance.ShowDetails ? post.AuthorUsername : "instagram";
        var authorUrl = "https://www.instagram.com/" + Uri.EscapeDataString(username);
        var media = post.Media.Select((m, i) => new { m, i }).Skip(order)
            .Concat(post.Media.Select((m, i) => new { m, i }).Take(order)).Take(4)
            .Select(item => new {
                id = $"{id}-{item.i}", type = item.m.MediaType,
                url = $"{instance.BaseUrl}/media/{kind}/{id}/{item.i}",
                preview_url = $"{instance.BaseUrl}/media/{kind}/{id}/{item.i}?thumbnail=true",
                description = instance.ShowDetails ? post.Caption : null,
                meta = item.m.Width > 0 && item.m.Height > 0 ? new {
                    width = item.m.Width, height = item.m.Height,
                    aspect = (double)item.m.Width / item.m.Height,
                    size = $"{item.m.Width}x{item.m.Height}"
                } : null
            });
        Response.Headers.CacheControl = "no-store";
        return Json(new {
            id, url, uri = url, created_at = DateTime.UnixEpoch,
            content = instance.ShowDetails ? $"<p>{WebUtility.HtmlEncode(post.Caption)}</p>" : "",
            visibility = "public", sensitive = false, spoiler_text = "",
            application = new { name = instance.Name, website = instance.BaseUrl },
            media_attachments = media,
            account = new { id = username, username, acct = username,
                display_name = username, url = authorUrl, uri = authorUrl,
                avatar = $"{instance.BaseUrl}/icon.svg", avatar_static = $"{instance.BaseUrl}/icon.svg",
                created_at = DateTime.UnixEpoch, locked = false, bot = false,
                followers_count = 0, following_count = 0, statuses_count = 0,
                note = "", fields = Array.Empty<object>(), emojis = Array.Empty<object>() },
            mentions = Array.Empty<object>(), tags = Array.Empty<object>(), emojis = Array.Empty<object>()
        });
    }

    private async Task<CachedPost?> Fetch(string kind, string id)
    {
        try { return await posts.GetOrFetchAsync(kind, id, HttpContext.RequestAborted); }
        catch (OperationCanceledException) { return null; }
    }

    private IActionResult Unavailable()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.RetryAfter = "30";
        return StatusCode(503, "Instagram media is temporarily unavailable, requires login, or has no supported direct URL. Open the original Instagram link.");
    }
}

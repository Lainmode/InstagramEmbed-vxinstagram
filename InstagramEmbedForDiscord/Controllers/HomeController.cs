using InstagramEmbed.Application.Models;
using InstagramEmbed.Application.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace InstagramEmbed.Controllers;

public sealed class HomeController : Controller
{
    private readonly PostCacheService _posts;
    private readonly DonateMessageService _donate;
    private readonly HttpClient _http;
    private readonly ILogger<HomeController> _logger;
    private readonly DonationSettings _settings;




    public HomeController(PostCacheService posts, DonateMessageService donate,
        IHttpClientFactory factory, ILogger<HomeController> logger, IOptions<DonationSettings> options)
    {
        _posts = posts;
        _donate = donate;
        _http = factory.CreateClient("regular");
        _logger = logger;
        _settings = options.Value;

    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        ViewBag.DonationCurrent = _settings.Current;
        ViewBag.DonationTarget = _settings.Target;
        base.OnActionExecuting(context);
    }


    [Route("/")]
    public IActionResult HomePage() => View();

    [Route("/setdonationvariables")]
    [HttpGet]
    public IActionResult SetDonationVariables([FromQuery] string pw, [FromQuery] int current, [FromQuery] int? target)
    {
        if (pw != _settings.Password)
        {
            return BadRequest();
        }

        _settings.Current = current;
        _settings.Target = target ?? _settings.Target;

        return RedirectToAction("HomePage");
    }


    [Route("{**path}")]
    public async Task<IActionResult> Index(string path,
        [FromQuery(Name = "img_index")] int? imgIndex)
    {

        _logger.LogInformation("HITs hit: ua={UA}",
    Request.Headers.UserAgent.ToString());

        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return BadRequest("Invalid Instagram path.");

            var segments = path.Trim('/').Split('/');

            int orderIndex = 0;
            bool orderSpecified = false;

            if (int.TryParse(segments.Last(), out int parsed))
            {
                orderIndex = Math.Max(0, parsed - 1);
                segments = segments.Take(segments.Length - 1).ToArray();
                orderSpecified = true;
            }
            else if (imgIndex.HasValue)
            {
                orderIndex = Math.Max(0, imgIndex.Value);
                orderSpecified = true;
            }

            string id = segments.Last();
            string type = segments.Length > 1 ? segments[^2] : segments[0];
            string? username = segments.Length > 2 ? segments[0] : null;

            ViewBag.Order = orderIndex;

            if (username?.Equals("stories", StringComparison.OrdinalIgnoreCase) == true)
            {
                username = type;                   // the actual username segment
                type = $"stories/{username}";
            }
            else if (username?.Equals("share", StringComparison.OrdinalIgnoreCase) == true)
            {
                type = $"share/{type}";
            }

            string instagramUrl = $"https://instagram.com/{type}/{id}/";


            bool isStoriesNoId = type.StartsWith("stories/", StringComparison.OrdinalIgnoreCase)
                                 && id.Equals(username, StringComparison.OrdinalIgnoreCase);

            string cacheId = isStoriesNoId
                ? Uri.EscapeDataString(instagramUrl)
                : id;

            ViewBag.PostId = cacheId;
            ViewBag.MaybeDonate = _donate.MaybeGetDonateMessage();

            var post = await _posts.GetOrFetchAsync(cacheId, instagramUrl);
            if (post == null) return NotFound();

            ViewBag.Post = post;
            return BuildResponse(post, instagramUrl, cacheId, orderIndex, orderSpecified);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing path '{Path}'", path);
            return View("Error");
        }
    }



    [Route("/offload/{id}")]
    [Route("/offload/{id}.mp4")]
    [Route("/offload/{id}/{order}")]
    [Route("/offload/{id}/{order}.mp4")]
    public async Task<IActionResult> OffloadPost(string id, int? order,
        [FromQuery] bool? thumbnail,
        [FromQuery(Name = "order")] int? orderQuery)  // ?order=N from Activity endpoint
    {
        _logger.LogInformation("OffloadPost hit: id={Id} order={Order} ua={UA}",
    id, order, Request.Headers.UserAgent.ToString());
        int idx = Math.Max(0, order ?? orderQuery ?? 0);

        var post = await _posts.GetOrFetchAsync(id, $"https://instagram.com/p/{id}/");
        if (post == null) return NotFound();

        var entry = post.Media.ElementAtOrDefault(idx) ?? post.Media.LastOrDefault();
        if (entry == null) return NotFound();

        string targetUrl = (thumbnail ?? false)
            ? (entry.MediaType == "video"
                ? post.DefaultThumbnailUrl ?? entry.ThumbnailUrl
                : entry.ThumbnailUrl)
            : entry.Url;

        if (string.IsNullOrWhiteSpace(targetUrl)) return NotFound();

        var ua = Request.Headers.UserAgent.ToString();
        bool isTelegramBot = ua.Contains("TelegramBot", StringComparison.OrdinalIgnoreCase);

        if (isTelegramBot && entry.MediaType == "video")
        {
            try
            {
                using var proxyRequest = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                proxyRequest.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                var proxyResponse = await _http.SendAsync(proxyRequest, HttpCompletionOption.ResponseHeadersRead);
                if (proxyResponse.IsSuccessStatusCode)
                {
                    var stream = await proxyResponse.Content.ReadAsStreamAsync();
                    var contentType = entry.MediaType == "video" ? "video/mp4" : "image/jpeg";

                    HttpContext.Response.Headers["Accept-Ranges"] = "bytes";
                    if (proxyResponse.Content.Headers.ContentLength.HasValue)
                        HttpContext.Response.Headers["Content-Length"] = proxyResponse.Content.Headers.ContentLength.Value.ToString();

                    HttpContext.Response.RegisterForDispose(proxyResponse);
                    return File(stream, contentType, enableRangeProcessing: true);
                }
                proxyResponse.Dispose();
                _logger.LogWarning("Telegram proxy got {Status} for {Url}", proxyResponse.StatusCode, targetUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telegram proxy failed for {Url}", targetUrl);
            }
        }

        return Redirect(targetUrl);
    }

    [Route("/oembed")]
    public IActionResult OEmbed(string? username, string? desc, string? likescomments, string donateMessage)
    {
        string providerName = donateMessage
            ?? $"vxinstagram{(string.IsNullOrWhiteSpace(likescomments) ? "" : " " + likescomments)}";

        return Json(new OEmbedModel
        {
            author_name = !string.IsNullOrEmpty(desc) ? desc : username ?? "vxinstagram",
            author_url = "https://instagram.com/" + username,
            provider_name = providerName,
            provider_url = "https://vxinstagram.com",
            title = string.Empty,
            type = "video",
            version = "1.0"
        });
    }



    [Route("/api/v1/statuses/{contextBase64}")]
    [Route("/users/{username}/statuses/{contextBase64}")]
    public async Task<IActionResult> Activity(string contextBase64)
    {
        var bytes = Base64Url.Decode(contextBase64);
        var payload = Encoding.UTF8.GetString(bytes);
        var parts = payload.Split('&');

        string cacheId = parts.Length > 0 ? parts[0] : string.Empty;
        int.TryParse(parts.Length > 1 ? parts[1] : null, out int order);
        string igUrl = parts.Length > 2
            ? string.Join("&", parts.Skip(2))   // re-join in case igUrl had & in it
            : $"https://instagram.com/p/{cacheId}/";

        var post = await _posts.GetOrFetchAsync(cacheId, igUrl);
        if (post == null) return NotFound();

        string host = $"https://{Request.Host}";

        var orderedMedia = post.Media
            .Skip(order)
            .Concat(post.Media.Take(order))
            .Take(4)
            .Select((m, i) =>
            {
                int globalIdx = (order + i) % post.Media.Count;

                string imageUrl = m.MediaType == "video"
                    ? (post.DefaultThumbnailUrl ?? m.ThumbnailUrl)
                    : m.Url;

                string thumbUrl = m.ThumbnailUrl ?? imageUrl;

                return new MediaAttachment
                {
                    id = cacheId,
                    type = m.MediaType,   // preserve real type, matches old code
                    url = $"{host}/offload/{cacheId}?order={globalIdx}",
                    preview_url = $"{host}/offload/{cacheId}?order={globalIdx}&thumbnail=true",
                    meta = new MediaAttachmentMeta
                    {
                        width = post.Width,
                        height = post.Height,
                        aspect = post.AspectRatio,
                        size = post.Size
                    }
                };
            })
            .ToList();

        var model = new ActivityPubModel
        {
            id = contextBase64,
            url = igUrl,
            uri = igUrl,
            created_at = DateTime.UtcNow,
            content = $"<p>{post.Caption}</p><b>❤️ {post.Likes}&nbsp;&nbsp;&nbsp;💬 {post.Comments}</b>",
            language = "en",
            visibility = "public",
            media_attachments = orderedMedia,
            account = new ActivityAccount
            {
                id = post.AuthorUsername,
                display_name = post.AuthorName ?? string.Empty,
                username = post.AuthorUsername,
                acct = post.AuthorUsername,
                url = "https://instagram.com/" + post.AuthorUsername,
                uri = "https://instagram.com/" + post.AuthorUsername,
                avatar = post.AvatarUrl ?? "https://www.vxinstagram.com/favicon.png",
                avatar_static = post.AvatarUrl ?? "https://www.vxinstagram.com/favicon.png",
                created_at = DateTime.UtcNow
            }
        };

        return Content(
            JsonSerializer.Serialize(model, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            }),
            "application/json");
    }



    private IActionResult BuildResponse(CachedPost post, string igUrl, string cacheId,
        int orderIndex, bool orderSpecified)
    {
        if (post.Media.Count == 0) return NotFound();

        if (post.Media.Count == 1 || orderSpecified)
        {
            var entry = post.Media.ElementAtOrDefault(orderIndex) ?? post.Media.First();
            return RenderSingle(entry, igUrl, cacheId, post, orderIndex);
        }

        return RenderMultiple(post.Media, igUrl, cacheId, post, orderIndex);
    }

    private IActionResult RenderSingle(CachedMedia media, string igUrl, string cacheId,
        CachedPost post, int orderIndex)
    {
        string contentUrl = $"https://{Request.Host}/offload/{Uri.EscapeDataString(cacheId)}/{orderIndex}";
        bool isPhoto = media.MediaType == "image";

        ViewBag.IsPhoto = isPhoto;
        ViewBag.Files = new List<CachedMedia> { media };
        return View("Index", new[] { contentUrl, media.ThumbnailUrl, igUrl });
    }

    private IActionResult RenderMultiple(List<CachedMedia> media, string igUrl, string cacheId,
        CachedPost post, int orderIndex)
    {
        // og:video / twitter:player points at the item the user requested
        string contentUrl = $"https://{Request.Host}/offload/{Uri.EscapeDataString(cacheId)}/{orderIndex}";

        ViewBag.IsPhoto = true;
        ViewBag.Files = media.Take(16).ToList();
        return View("Index", new[] { contentUrl, (string?)null, igUrl });
    }
}


public static class Base64Url
{
    public static string Encode(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static byte[] Decode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(s);
    }
}
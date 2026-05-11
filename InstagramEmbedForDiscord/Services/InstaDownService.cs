using System.Text.Json;
using InstagramEmbed.Application.Models;
using Microsoft.Extensions.Caching.Memory;

namespace InstagramEmbed.Application.Services;

/// <summary>
/// Fetches posts via the InstaDown API (instadown.duckdns.org) and maps
/// the response to CachedPost — same shape as snapsave, different source.
/// Used exclusively for d.vxinstagram requests.
/// </summary>
public sealed class InstaDownCacheService
{
    private readonly IMemoryCache _cache;
    private readonly HttpClient _http;
    private readonly ILogger<InstaDownCacheService> _logger;
    private readonly string _apiBase;
    private readonly string _apiKey;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(4);

    public InstaDownCacheService(IMemoryCache cache, IHttpClientFactory factory,
        ILogger<InstaDownCacheService> logger, IConfiguration config)
    {
        _cache = cache;
        _http = factory.CreateClient("instadown");
        _logger = logger;
        _apiBase = config.GetValue<string>("InstaDown:BaseUrl", "https://instadown.duckdns.org")!;
        _apiKey = config.GetValue<string>("InstaDown:ApiKey", "undefined")!;
    }

    public async Task<CachedPost?> GetOrFetchAsync(string cacheId, string instagramUrl)
    {
        // Use a separate cache key prefix so it doesn't collide with snapsave cache
        var key = $"instadown:{cacheId}";
        if (_cache.TryGetValue(key, out CachedPost? cached))
            return cached;

        var post = await FetchFromInstaDownAsync(cacheId, instagramUrl);
        if (post is null) return null;

        _cache.Set(key, post, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1
        });

        return post;
    }

    private async Task<CachedPost?> FetchFromInstaDownAsync(string cacheId, string instagramUrl)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var requestUrl = $"{_apiBase}/vx/scrape?url={Uri.EscapeDataString(instagramUrl)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            req.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);

            var resp = await _http.SendAsync(req, cts.Token);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("InstaDown returned {Status} for {Url}", resp.StatusCode, instagramUrl);
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(cts.Token);
            _logger.LogDebug("InstaDown raw response: {Json}", json[..Math.Min(300, json.Length)]);

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var doc = JsonSerializer.Deserialize<InstaDownResponse>(json, opts);

            if (doc?.Media is null || doc.Media.Count == 0)
            {
                _logger.LogWarning("InstaDown returned no media for {Url}", instagramUrl);
                return null;
            }

            // Pick width/height from first media item
            var first = doc.Media[0];

            return new CachedPost
            {
                ShortCode = doc.ShortCode ?? cacheId,
                RawUrl = instagramUrl,
                AuthorUsername = doc.Owner?.Username ?? "unknown",
                AuthorName = doc.Owner?.FullName,
                AvatarUrl = doc.Owner?.AvatarUrl,
                Caption = doc.Caption,
                Likes = (int)(doc.LikeCount ?? 0),
                Comments = (int)(doc.CommentCount ?? 0),
                Width = first.Width > 0 ? first.Width : 720,
                Height = first.Height > 0 ? first.Height : 1280,
                DefaultThumbnailUrl = doc.Media.FirstOrDefault(m => m.ThumbnailUrl != null)?.ThumbnailUrl,
                Media = doc.Media.Select(m => new CachedMedia
                {
                    Url = m.Url,
                    MediaType = m.Type == "photo" ? "image" : "video",
                    ThumbnailUrl = m.ThumbnailUrl ?? m.Url,
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch {Url} from InstaDown", instagramUrl);
            return null;
        }
    }
}

// ── InstaDown API response shape ─────────────────────────────────────────────

file sealed class InstaDownResponse
{
    public string? ShortCode { get; set; }
    public string? MediaType { get; set; }
    public string? Caption { get; set; }
    public long? LikeCount { get; set; }
    public long? CommentCount { get; set; }
    public InstaDownOwner? Owner { get; set; }
    public List<InstaDownMedia> Media { get; set; } = [];
}

file sealed class InstaDownOwner
{
    public string? Username { get; set; }
    public string? FullName { get; set; }
    public string? AvatarUrl { get; set; }
}

file sealed class InstaDownMedia
{
    public string Url { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string Type { get; set; } = "video";
    public int Width { get; set; }
    public int Height { get; set; }
}
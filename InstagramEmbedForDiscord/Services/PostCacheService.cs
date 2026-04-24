using System.Text.Json;
using InstagramEmbed.Application.Models;
using Microsoft.Extensions.Caching.Memory;

namespace InstagramEmbed.Application.Services;

/// <summary>
/// Fetches Instagram posts via the bundled snapsave Node service and caches
/// them in-process memory. 
/// </summary>
public sealed class PostCacheService
{
    private readonly IMemoryCache _cache;
    private readonly HttpClient _http;
    private readonly ILogger<PostCacheService> _logger;
    private readonly string _snapSaveBase;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(4);

    public PostCacheService(IMemoryCache cache, IHttpClientFactory factory,
        ILogger<PostCacheService> logger, IConfiguration config)
    {
        _cache = cache;
        _http = factory.CreateClient("snapsave");
        _logger = logger;
        var port = config.GetValue<int>("SnapSave:Port", 3200);
        _snapSaveBase = $"http://localhost:{port}";
    }

    public async Task<CachedPost?> GetOrFetchAsync(string cacheId, string instagramUrl)
    {
        if (_cache.TryGetValue(cacheId, out CachedPost? cached))
            return cached;

        var post = await FetchFromSnapSaveAsync(cacheId, instagramUrl);
        if (post == null) return null;

        _cache.Set(cacheId, post, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1
        });

        return post;
    }

    private async Task<CachedPost?> FetchFromSnapSaveAsync(string cacheId, string instagramUrl)
    {
        string? json = "";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var response = await _http.GetAsync(
                $"{_snapSaveBase}/igdl?url={Uri.EscapeDataString(instagramUrl)}", cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("snapsave returned {Status} for {Url}", response.StatusCode, instagramUrl);
                return null;
            }

            json = await response.Content.ReadAsStringAsync(cts.Token);
            _logger.LogDebug("snapsave raw response: {Json}", json);
            _logger.LogInformation("snapsave returned {Status} for {Url}, {json}", response.StatusCode, instagramUrl, json);

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var snap = JsonSerializer.Deserialize<SnapSaveResponse>(json, opts);

            if (snap?.success != true || snap.data?.media == null || snap.data.media.Count == 0)
            {
                _logger.LogWarning("snapsave returned no media for {Url}. Response: {Json}", instagramUrl, json);
                return null;
            }

            return new CachedPost
            {
                ShortCode = cacheId,
                RawUrl = instagramUrl,
                Media = snap.data.media.Select(m => new CachedMedia
                {
                    Url = m.url,
                    MediaType = m.type,
                    ThumbnailUrl = m.thumbnail ?? m.url
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch {Url} from snapsave {json}", instagramUrl, json);
            return null;
        }
    }
}
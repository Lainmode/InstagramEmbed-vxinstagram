using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using InstagramEmbed.Application.Models;
using Microsoft.Extensions.Caching.Memory;

namespace InstagramEmbed.Application.Services;

public sealed class PostCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<PostCacheService> _logger;
    private readonly SemaphoreSlim _workers;
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();
    private readonly string _python, _script, _provider;
    private readonly int _timeout, _ttl;
    private sealed record Result(CachedPost? Post);

    public PostCacheService(IMemoryCache cache, ILogger<PostCacheService> logger,
        IConfiguration config, IWebHostEnvironment environment)
    {
        _cache = cache;
        _logger = logger;
        _python = config["Extractor:Python"] ?? "python3";
        _script = Path.Combine(environment.ContentRootPath, "extractor", "extract.py");
        _provider = config["Extractor:Provider"] ?? "auto";
        if (_provider is not ("auto" or "yt-dlp" or "instaloader"))
            throw new InvalidOperationException("Invalid Extractor:Provider");
        _workers = new(Math.Clamp(config.GetValue("Extractor:Concurrency", 2), 1, 4));
        _timeout = Math.Clamp(config.GetValue("Extractor:TimeoutSeconds", 40), 5, 90);
        _ttl = Math.Clamp(config.GetValue("Extractor:CacheSeconds", 300), 10, 900);
    }

    public static bool ValidPost(string kind, string id) =>
        (kind is "p" or "reel" or "tv") && Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);

    public static bool ValidMediaUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return false;
        return new[] { "cdninstagram.com", "fbcdn.net" }.Any(s =>
            uri.Host.Equals(s, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<CachedPost?> GetOrFetchAsync(string kind, string id, CancellationToken cancellation = default)
    {
        if (!ValidPost(kind, id)) return null;
        var key = $"{kind}:{id}";
        if (_cache.TryGetValue(key, out Result? hit)) return hit!.Post;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(_timeout));
        var gate = _locks[(uint)key.GetHashCode() % _locks.Length];
        await gate.WaitAsync(deadline.Token);
        try
        {
            if (_cache.TryGetValue(key, out hit)) return hit!.Post;
            if (!await _workers.WaitAsync(TimeSpan.FromSeconds(2), deadline.Token)) return null;
            try
            {
                var post = await ExtractAsync(kind, id, deadline.Token);
                _cache.Set(key, new Result(post), new MemoryCacheEntryOptions {
                    Size = EstimateSize(post), AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(post is null ? 30 : _ttl)
                });
                return post;
            }
            finally { _workers.Release(); }
        }
        finally { gate.Release(); }
    }

    private static long EstimateSize(CachedPost? post) => post is null ? 256 :
        1024L + 2L * (post.AuthorUsername.Length + (post.AuthorName?.Length ?? 0) + (post.Caption?.Length ?? 0))
        + post.Media.Sum(m => 256L + 2L * (m.Url.Length + m.ThumbnailUrl.Length));

    private async Task<CachedPost?> ExtractAsync(string kind, string id, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(_python) {
            UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var arg in new[] { _script, kind, id, _provider }) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
            using var stop = cancellation.Register(() => Kill(process));
            var stdout = ReadBoundedAsync(process.StandardOutput, 1_048_576, process, cancellation);
            var stderr = ReadBoundedAsync(process.StandardError, 65_536, process, cancellation);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(cancellation));
            if (process.ExitCode != 0) { _logger.LogWarning("Instagram extractor exited with {Code}", process.ExitCode); return null; }
            var post = JsonSerializer.Deserialize<CachedPost>(await stdout);
            if (post is null || post.Media.Count is < 1 or > 20 || post.Media.Any(m =>
                m.MediaType is not ("image" or "video") || !ValidMediaUrl(m.Url)
                || (m.ThumbnailUrl.Length > 0 && !ValidMediaUrl(m.ThumbnailUrl)))) return null;
            if (string.IsNullOrWhiteSpace(post.AuthorUsername)) post.AuthorUsername = "instagram";
            return post;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _logger.LogWarning("Instagram extraction failed: {ErrorType}", ex.GetType().Name); return null; }
        finally { Kill(process); }
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, Process process, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (text.Length + count > limit) { Kill(process); throw new InvalidDataException("Extractor output limit"); }
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}

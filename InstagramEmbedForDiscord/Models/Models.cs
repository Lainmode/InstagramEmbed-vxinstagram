namespace InstagramEmbed.Application.Models;

public sealed class CachedPost
{
    public string AuthorUsername { get; set; } = "instagram";
    public string? AuthorName { get; set; }
    public string? Caption { get; set; }
    public int Likes { get; set; }
    public int Comments { get; set; }
    public List<CachedMedia> Media { get; set; } = [];
}

public sealed class CachedMedia
{
    public string Url { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed record EmbedPage(CachedPost Post, string Kind, string Id, int? Order);

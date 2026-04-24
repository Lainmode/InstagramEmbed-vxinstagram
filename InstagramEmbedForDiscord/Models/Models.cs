namespace InstagramEmbed.Application.Models
{
    public sealed class CachedPost
    {
        public string ShortCode { get; init; } = string.Empty;
        public string RawUrl { get; init; } = string.Empty;

        public string AuthorUsername { get; set; } = "NOT_SET";
        public string? AuthorName { get; set; }
        public string? AvatarUrl { get; set; }
        public string? Caption { get; set; }
        public string? TrackName { get; set; }

        public int Likes { get; set; }
        public int Comments { get; set; }

        public int Width { get; set; } = 720;
        public int Height { get; set; } = 1280;

        public string? DefaultThumbnailUrl { get; set; }

        public List<CachedMedia> Media { get; init; } = [];

        public double AspectRatio => Height == 0 ? 0.565 : (double)Height / Width;
        public string Size => $"{Width}x{Height}";
    }

    public sealed class CachedMedia
    {
        public string Url { get; init; } = string.Empty;
        public string MediaType { get; init; } = string.Empty; // "video" | "image"
        public string ThumbnailUrl { get; init; } = string.Empty;
    }



    public sealed class SnapSaveResponse
    {
        public bool success { get; set; }
        public SnapSaveData? data { get; set; }
    }

    public sealed class SnapSaveData
    {
        public List<SnapSaveMedia> media { get; set; } = [];
    }

    public sealed class SnapSaveMedia
    {
        public string url { get; set; } = string.Empty;
        public string? thumbnail { get; set; }
        public string type { get; set; } = "video";
    }

    // ── view / embed models ──────────────────────────────────────────────────────

    public sealed class OEmbedModel
    {
        public string version { get; set; } = "1.0";
        public string type { get; set; } = "video";
        public string author_name { get; set; } = string.Empty;
        public string author_url { get; set; } = string.Empty;
        public string provider_name { get; set; } = string.Empty;
        public string provider_url { get; set; } = string.Empty;
        public string title { get; set; } = string.Empty;
    }

    public sealed class MediaAttachmentMeta
    {
        public int width { get; set; }
        public int height { get; set; }
        public double aspect { get; set; }
        public string size { get; set; } = string.Empty;
    }

    public sealed class MediaAttachment
    {
        public string id { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
        public string url { get; set; } = string.Empty;
        public string preview_url { get; set; } = string.Empty;
        public object? remote_url { get; set; }
        public object? preview_remote_url { get; set; }
        public object? text_url { get; set; }
        public object? description { get; set; }
        public MediaAttachmentMeta? meta { get; set; }
    }

    public sealed class ActivityAccount
    {
        public string id { get; set; } = string.Empty;
        public string display_name { get; set; } = string.Empty;
        public string username { get; set; } = string.Empty;
        public string acct { get; set; } = string.Empty;
        public string url { get; set; } = string.Empty;
        public string uri { get; set; } = string.Empty;
        public DateTime created_at { get; set; }
        public bool locked { get; set; }
        public bool bot { get; set; }
        public bool discoverable { get; set; }
        public bool indexable { get; set; }
        public bool group { get; set; }
        public string avatar { get; set; } = string.Empty;
        public string avatar_static { get; set; } = string.Empty;
        public int followers_count { get; set; }
        public int following_count { get; set; }
        public bool hide_collections { get; set; }
        public bool noindex { get; set; }
        public List<object> emojis { get; set; } = [];
        public List<object> roles { get; set; } = [];
        public List<object> fields { get; set; } = [];
    }

    public sealed class ActivityApplication
    {
        public string name { get; set; } = "vxinstagram";
        public string website { get; set; } = "https://vxinstagram.com";
    }

    public sealed class ActivityPubModel
    {
        public string id { get; set; } = string.Empty;
        public string url { get; set; } = string.Empty;
        public string uri { get; set; } = string.Empty;
        public DateTime created_at { get; set; }
        public DateTime? edited_at { get; set; }
        public string content { get; set; } = string.Empty;
        public string spoiler_text { get; set; } = string.Empty;
        public string language { get; set; } = "en";
        public string visibility { get; set; } = "public";
        public ActivityApplication application { get; set; } = new();
        public List<MediaAttachment> media_attachments { get; set; } = [];
        public ActivityAccount account { get; set; } = new();
        public string? in_reply_to_id { get; set; }
        public string? in_reply_to_account_id { get; set; }
        public List<object> mentions { get; set; } = [];
        public List<object> tags { get; set; } = [];
        public List<object> emojis { get; set; } = [];
        public object? card { get; set; }
        public object? poll { get; set; }
        public object? reblog { get; set; }
    }
}

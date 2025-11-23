using System.ComponentModel.DataAnnotations;

namespace InstagramEmbed.Domain.Entities
{
    public class Post
    {
        [Key]
        public string ShortCode { get; set; } = string.Empty;
        public string RawUrl { get; set; } = string.Empty;
        public string? AuthorName { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; } = string.Empty;
        public string? AuthorUsername { get; set; } = "NOT_SET";
        public string? Caption { get; set; } = string.Empty;

        public int Height { get; set; } = 1280;
        public int Width { get; set; } = 720;

        public double AspectRatio => Height == 0 ? 0.565 : Height / Width;
        public string Size => Height == 0 ? "720x1280" : $"{Height}x{Width}";

        public string? DefaultThumbnailUrl { get; set; }
        public string? TrackName { get; set; }

        public int Likes { get; set; }
        public int Comments { get; set; }
        public virtual ICollection<Media> Media { get; set; } = [];

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresOn { get; set; } = DateTime.UtcNow.AddHours(12);

        public bool IsExpired => DateTime.UtcNow > ExpiresOn;

    }

    public class Media
    {
        public int ID { get; set; }
        public string RapidSaveUrl { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
    }
}

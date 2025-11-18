using System.ComponentModel.DataAnnotations;

namespace InstagramEmbed.Domain.Entities
{
    public class Post
    {
        [Key]
        public string ShortCode { get; set; } = string.Empty;
        public string RawUrl { get; set; } = string.Empty;
        public string? AuthorName { get; set; } = string.Empty;
        public string? AuthorUsername { get; set; } = "NOT_SET";
        public string? Caption { get; set; } = string.Empty;
        public int Likes { get; set; }
        public int Comments { get; set; }
        public virtual ICollection<Media> Media { get; set; } = [];

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Media
    {
        public int ID { get; set; }
        public string RapidSaveUrl { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
    }
}

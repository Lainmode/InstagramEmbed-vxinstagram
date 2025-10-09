using System.ComponentModel.DataAnnotations;

namespace InstagramEmbedForDiscord.Models.Entities
{
    public class Post
    {
        [Key]
        public string ID { get; set; } = string.Empty;
        public string RawUrl { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public string AuthorUsername { get; set; } = string.Empty;
        public int Likes { get; set; }
        public int Comments { get; set; }
        public int Shares { get; set; }
        public PostType PostType { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public virtual ICollection<SnapSaveEntry> SnapSaveEntries { get; set; } = [];

    }

    public class SnapSaveEntry
    {
        public int ID { get; set; }
        public int Order { get; set; }
        public string MediaUrl { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public MediaType MediaType { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public enum MediaType
    {
        None,
        Video,
        Image,
    }

    public enum PostType
    {
        None,
        Post,
        Share,
        Story
    }
}

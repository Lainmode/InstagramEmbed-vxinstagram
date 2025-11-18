using System.ComponentModel.DataAnnotations;

namespace InstagramEmbed.Domain.Entities
{
    public class Post
    {
        [Key]
        public string ShortCode { get; set; } = string.Empty;
        public string RawUrl { get; set; } = string.Empty;
        public virtual ICollection<Media> Media { get; set; } = [];

        public DateTime ExpiresOn { get; set; } = DateTime.UtcNow.AddDays(30);
    }

    public class Media
    {
        public int ID { get; set; }
        public string RapidSaveUrl { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
    }
}

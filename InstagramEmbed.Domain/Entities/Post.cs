namespace InstagramEmbed.Domain.Entities
{
    public class Post
    {
        public int ID { get; set; }
        public string RawUrl { get; set; } = string.Empty;
        public string InstagramID { get; set; } = string.Empty;
        public int Order { get; set; }
        public string RapidSaveUrl { get; set; } = string.Empty;
    }
}

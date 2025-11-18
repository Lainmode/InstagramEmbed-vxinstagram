namespace InstagramEmbedForDiscord.Models.Entities
{
    public class Session
    {
        public int ID { get; set; }
        public DateTime ExpiresOn { get; set; } = DateTime.UtcNow.AddDays(7);
        public string CSRFToken { get; set; } = string.Empty;

        public void ExpireSession()
        {
            this.ExpiresOn = DateTime.UtcNow.AddSeconds(-1);
        }
    }
}

namespace InstagramEmbed.Application.Helpers
{
    public class Constants
    {
        public static ProxyInformation? ProxyInformation = null;
    }

    public class ProxyInformation()
    {
        public string Host { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}

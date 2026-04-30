using System.Diagnostics;

namespace InstagramEmbed.Application.Services
{
    public class DonationSettings
    {
        public string Password { get; set; } = "password";
        public int Current { get; set; } = 0;
        public int Target { get; set; } = 100;
    }
}

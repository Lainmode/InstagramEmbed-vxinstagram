namespace InstagramEmbed.Application.Services
{
    /// <summary>
    /// Decides whether the current response should show a donate prompt in the
    /// oembed provider_name field.  Roughly 15 % of all requests get the banner.
    /// </summary>
    public sealed class DonateMessageService
    {
        private static readonly string[] DonateMessages =
        [
            "❤️ Donate to keep vxinstagram running → buymeacoffee.com/alsauce",
        "☕ Enjoying vxinstagram? Support the project → buymeacoffee.com/alsauce",
        "💸 Server bills don't pay themselves → buymeacoffee.com/alsauce",
        "🙏 Free service, powered by you → buymeacoffee.com/alsauce",
        "🚀 Keep vxinstagram alive → buymeacoffee.com/alsauce",
    ];

        private int _counter;

        /// <summary>
        /// Returns a donate message approximately 15 % of the time, otherwise null.
        /// Thread-safe via Interlocked.
        /// </summary>
        public string? MaybeGetDonateMessage()
        {
            var val = Interlocked.Increment(ref _counter);
            // Show on every ~7th request (≈14 %)
            if (val % 7 != 0) return null;
            return DonateMessages[val % DonateMessages.Length];
        }
    }

}

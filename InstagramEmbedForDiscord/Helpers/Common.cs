namespace InstagramEmbed.Application.Helpers
{
    public class Common
    {
        public static IDictionary<string, string> GetGraphQLScrapingBody(string shortcode)
        {
            return new Dictionary<string, string>
            {
                { "av", "kr65yh:qhc696:klxf8v" },
                { "__d", "www" },
                { "__user", "0" },
                { "__a", "1" },
                { "__req", "k" },
                { "__hs", "19888.HYP:instagram_web_pkg.2.1..0.0" },
                { "dpr", "2" },
                { "__ccg", "UNKNOWN" },
                { "__rev", "1014227545" },
                { "__s", "trbjos:n8dn55:yev1rm" },
                { "__hsi", "7573775717678450108" },
                { "__dyn", "7xeUjG1mxu1syUbFp40NonwgU7SbzEdF8aUco2qwJw5ux609vCwjE1xoswaq0yE6ucw5Mx62G5UswoEcE7O2l0Fwqo31w9a9wtUd8-U2zxe2GewGw9a362W2K0zK5o4q3y1Sx-0iS2Sq2-azo7u3C2u2J0bS1LwTwKG1pg2fwxyo6O1FwlEcUed6goK2O4UrAwCAxW6Uf9EObzVU8U" },
                { "__csr", "n2Yfg_5hcQAG5mPtfEzil8Wn-DpKGBXhdczlAhrK8uHBAGuKCJeCieLDyExenh68aQAKta8p8ShogKkF5yaUBqCpF9XHmmhoBXyBKbQp0HCwDjqoOepV8Tzk8xeXqAGFTVoCciGaCgvGUtVU-u5Vp801nrEkO0rC58xw41g0VW07ISyie2W1v7F0CwYwwwvEkw8K5cM0VC1dwdi0hCbc094w6MU1xE02lzw" },
                { "__comet_req", "7" },
                { "lsd", "lvKgZqkPPmLKqUfKIBiMFa" },
                { "jazoest", "2882" },
                { "__spin_r", "1014227545" },
                { "__spin_b", "trunk" },
                { "__spin_t", "1718406700" },
                { "fb_api_caller_class", "RelayModern" },
                { "fb_api_req_friendly_name", "PolarisPostActionLoadPostQueryQuery" },
                { "variables", $"{{\"shortcode\":\"{shortcode}\"}}" },
                { "server_timestamps", "true" },
                { "doc_id", "25018359077785073" }
            };
        }
    }
}

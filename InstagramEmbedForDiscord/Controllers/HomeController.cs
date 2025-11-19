using Azure;
using InstagramEmbed.DataAccess;
using InstagramEmbed.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace InstagramEmbedForDiscord.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly HttpClient _regularClient;

        private InstagramContext Db = new InstagramContext();

        public HomeController(ILogger<HomeController> logger, IWebHostEnvironment env, IHttpClientFactory factory)
        {
            _regularClient = factory.CreateClient("regular");
            _logger = logger;
            _env = env;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            var httpContext = context.HttpContext;

            Task.Run(() =>
            {
                var dbContext = new InstagramContext();

                var ipAddress = "127.0.0.1";

                var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrEmpty(forwardedFor))
                    ipAddress = forwardedFor.Split(',')[0];

                ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? ipAddress;

                var log = ActionLog.CreateActionLog(httpContext.Request.Method, httpContext.Request.Path + httpContext.Request.QueryString, httpContext.Request.Headers["User-Agent"].ToString(), ipAddress);

                dbContext.ActionLogs.Add(log);
                dbContext.SaveChanges();
            });

        }

        [Route("{**path}")]
        public async Task<IActionResult> Index(string path, [FromQuery(Name = "img_index")] int? imgIndex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return BadRequest("Invalid Instagram path.");

                var segments = path.Trim('/').Split('/');

                int orderIndex = 0;
                bool orderSpecified = false;
                string? lastSegment = segments.LastOrDefault();

                if (int.TryParse(lastSegment, out int parsedIndex))
                {
                    orderIndex = parsedIndex <= 0 ? 0 : parsedIndex - 1;
                    segments = segments.Take(segments.Length - 1).ToArray();

                    orderSpecified = true;
                }
                else if (imgIndex.HasValue)
                {
                    orderIndex = imgIndex.Value <= 0 ? 0 : imgIndex.Value;

                    orderSpecified = true;
                }

                string? id = segments.Last();                // hash
                string? type = segments.Length > 1 ? segments[^2] : segments.FirstOrDefault(); // p, reel, etc.
                string? username = segments.Length > 2 ? segments[0] : null;

                ViewBag.PostId = id;
                ViewBag.Order = orderIndex;

                Post? post = Db.Posts.Find(id);

                if (post != null)
                {
                    await RefreshPostIfNeeded(post);

                    ViewBag.Post = post;
                    return await ProcessMedia(post, post.RawUrl, id, orderIndex, orderSpecified);
                }


                if (username?.ToLower() == "stories")
                {
                    username = type;
                    type = $"stories/{username}";
                }

                else if (username?.ToLower() == "share")
                {
                    type = $"share/{type}";
                }

                // Rebuild link
                string link = $"https://instagram.com/{type}/{id}/";

                // Fetch SnapSave/Instagram API response
                var instagramResponse = await GetSnapsaveResponse(link);
                var media = instagramResponse.url?.data?.media;
                InstagramPostDetails postDetails = new InstagramPostDetails() { Username = "NOT_SET" };

                if (media == null || media.Count == 0)
                    return BadRequest("No media found.");

                if (Request.Host.Host.EndsWith("d.vxinstagram.com", StringComparison.OrdinalIgnoreCase))
                {
                    postDetails = await GetPostDetails(id);
                }

                post = new Post()
                {
                    RawUrl = link,
                    AuthorName = postDetails.Name,
                    AuthorUsername = postDetails.Username,
                    Caption = postDetails.Description,
                    Comments = postDetails.Comments,
                    Likes = postDetails.Likes,
                    ShortCode = id,
                };


                foreach (var item in media)
                {
                    post.Media.Add(new Media() { RapidSaveUrl = item.url, MediaType = item.type, ThumbnailUrl = item.thumbnail });
                }

                Db.Posts.Add(post);
                Db.SaveChanges();

                ViewBag.Post = post;
                return await ProcessMedia(post, post.RawUrl, id, orderIndex, orderSpecified);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return View("Error");
            }
        }



        [Route("/")]
        public IActionResult HomePage()
        {
            return View();
        }

        [Route("/offload/{id}/{order?}")]
        public async Task<IActionResult> OffloadPost(string id, int? order)
        {
            int orderIndex = (order ?? 0);
            if (orderIndex < 0) orderIndex = 0;


            var post = Db.Posts.Find(id);

            if (post == null)
                return NotFound();

            await RefreshPostIfNeeded(post);

            var entry = post.Media.ElementAtOrDefault(orderIndex);

            // if for some reason the order is out of bounds, which can only happen if order > length, take the last entry
            entry ??= post.Media.LastOrDefault();
            if (entry == null)
                return NotFound();


            return Redirect(entry.RapidSaveUrl);

        }


        private async Task<IActionResult> ProcessMedia(Post dbPost, string link, string id, int orderIndex, bool orderSpecified)
        {
            if (dbPost.Media.Count == 1 || orderSpecified)
            {
                var entry = dbPost.Media.ElementAtOrDefault(orderIndex);
                entry ??= dbPost.Media.First();
                return ProcessSingleItem(new InstagramMedia
                {
                    url = entry.RapidSaveUrl,
                    thumbnail = entry.ThumbnailUrl,
                    type = entry.MediaType.ToString().ToLower()
                }, link);
            }
            return await ProcessMultipleItems(
                dbPost.Media.Take(16).Select(e => new InstagramMedia
                {
                    url = e.RapidSaveUrl,
                    thumbnail = e.ThumbnailUrl,
                    type = e.MediaType.ToString().ToLower()
                }).ToList(),
                link,
                id
            );
        }

        [Route("/oembed")]
        public IActionResult OEmbed(string username, string? desc, string? likescomments)
        {
            return Json(new OEmbedModel()
            {
                author_name = desc != null ? !desc.IsNullOrEmpty() ? desc : $"@{username}" : $"@{username}",
                author_url = "https://instagram.com/" + username,
                provider_name = $"vxinstagram {likescomments}",
                provider_url = "https://github.com/Lainmode/InstagramEmbed-vxinstagram",
                title = "",
                type = "video",
                version = "1.0"
            });
        }


        private async Task<InstagramPostDetails> GetPostDetails(string id)
        {
            try
            {
                var response = await FetchInstagramPostAsync(id);
                var post = ExtractInstagramPostDetails(response);
                return post;
            }
            catch (Exception e)
            {
                return new InstagramPostDetails() { Username = "NOT_SET" };
            }
        }


        private async Task<InstagramResponse> GetSnapsaveResponse(string link)
        {

            HttpResponseMessage snapSaveResponse = await _regularClient.GetAsync("http://alsauce.com:3200/igdl?url=" + link);
            string snapSaveResponseString = await snapSaveResponse.Content.ReadAsStringAsync();
            InstagramResponse instagramResponse = JsonConvert.DeserializeObject<InstagramResponse>(snapSaveResponseString)!;

            return instagramResponse;

        }

        private async Task RefreshPostIfNeeded(Post post)
        {
            var response = await _regularClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, post.Media.First().RapidSaveUrl));

            if (response.IsSuccessStatusCode)
            {
                if (Request.Host.Host.EndsWith("d.vxinstagram.com", StringComparison.OrdinalIgnoreCase) && post.AuthorUsername == "NOT_SET")
                {
                    InstagramPostDetails postDetails = await GetPostDetails(post.ShortCode);
                    if (postDetails.Username == "NOT_SET") return;
                    post.AuthorUsername = postDetails.Username;
                    post.AuthorName = postDetails.Name;
                    post.Caption = postDetails.Description;
                    post.Comments = postDetails.Comments;
                    post.Likes = postDetails.Likes;

                    Db.SaveChanges();
                }
                return;
            }

            var instagramResponse = await GetSnapsaveResponse(post.RawUrl);
            var mediaList = instagramResponse.url?.data?.media;

            if (mediaList == null)
            {
                throw new Exception("NOT FOUND");
            }

            Db.Media.RemoveRange(post.Media);
            post.Media.Clear();

            foreach (var item in mediaList)
            {
                post.Media.Add(new Media() { MediaType = item.type, RapidSaveUrl = item.url, ThumbnailUrl = item.thumbnail });
            }



            Db.SaveChanges();
        }

        private IActionResult ProcessSingleItem(InstagramMedia media, string originalLink)
        {
            var contentUrl = media.url;
            var thumbnailUrl = media.thumbnail;

            bool isPhoto = media.type == "image";

            if (isPhoto)
            {
                ViewBag.IsPhoto = true;
                ViewBag.Files = new List<InstagramMedia>() { media };
                return View(new string[] { contentUrl, thumbnailUrl, originalLink });
            }


            string[] data = { contentUrl, thumbnailUrl, originalLink };
            ViewBag.IsPhoto = false;
            ViewBag.Files = new List<InstagramMedia>() { media };
            return View(data);
        }

        private async Task<IActionResult> ProcessMultipleItems(List<InstagramMedia> media, string originalLink, string id)
        {

            string? fileName = await GetGeneratedFile(media, id);

            if (fileName == null) return BadRequest("Could not process images.");

            string contentUrl = $"https://{Request.Host}/generated/{fileName}";

            ViewBag.IsPhoto = true;
            ViewBag.Files = media;
            return View(new string[] { contentUrl, null, originalLink });
        }

        private async Task<string?> GetGeneratedFile(List<InstagramMedia> media, string id)
        {
            List<SKBitmap> bitmaps = await GetMultipleImages(media.Take(16).ToList());

            if (bitmaps.Count == 0)
                return null;

            int columns = media.Count <= 5 ? 2 : media.Count <= 9 ? 3 : media.Count <= 16 ? 4 : 0;
            int rows = (int)Math.Ceiling((double)bitmaps.Count / columns);

            List<List<SKBitmap>> bitmapRows = new();
            for (int i = 0; i < bitmaps.Count; i += columns)
            {
                var row = bitmaps.Skip(i).Take(columns).ToList();
                bitmapRows.Add(row);
            }


            int canvasWidth = 0;
            int canvasHeight = 0;
            List<int> rowHeights = new();
            List<int> rowWidths = new();

            foreach (var row in bitmapRows)
            {
                int rowWidth = row.Sum(img => img.Width);
                int rowHeight = row.Max(img => img.Height);


                rowWidths.Add(rowWidth);
                rowHeights.Add(rowHeight);

                if (rowWidth > canvasWidth)
                    canvasWidth = rowWidth;

                canvasHeight += rowHeight;
            }

            using var finalBitmap = new SKBitmap(canvasWidth, canvasHeight);
            using var canvas = new SKCanvas(finalBitmap);
            canvas.Clear(GetAverageColor(bitmaps));

            int yOffset = 0;

            for (int i = 0; i < bitmapRows.Count; i++)
            {
                var row = bitmapRows[i];
                int xOffset = 0;
                int rowHeight = rowHeights[i];

                foreach (var img in row)
                {
                    float offsetY = yOffset + (rowHeight - img.Height) / 2f;
                    float offsetX = xOffset;

                    if (row.IndexOf(img) == row.Count - 1)
                    {
                        var remainingWidth = canvasWidth - (row.Sum(e => e.Width) - img.Width);
                        offsetX = (offsetX + remainingWidth - img.Width);
                    }

                    canvas.DrawBitmap(img, offsetX, offsetY);
                    xOffset += img.Width;
                }

                yOffset += rowHeight;
            }

            canvas.Flush();

            using var image = SKImage.FromBitmap(finalBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

            string folderPath = Path.Combine(_env.WebRootPath, "generated");
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            string fileName = $"{SanitizeFileName(id)}.jpg";
            string savePath = Path.Combine(folderPath, fileName);

            if (!Path.Exists(savePath))
            {
                using (var stream = System.IO.File.OpenWrite(savePath))
                {
                    data.SaveTo(stream);
                }
            }
            return fileName;
        }


        private async Task<List<SKBitmap>> GetMultipleImages(List<InstagramMedia> media)
        {
            List<Task> tasks = [];
            List<SKBitmap?> bitmaps = [];
            List<KeyValuePair<int, SKBitmap?>?> keyValuePairs = [];
            foreach (var item in media)
            {
                bool isVideo = item.type == "video";
                string image = isVideo ? item.thumbnail : item.url;
                tasks.Add(Task.Run(async () => keyValuePairs.Add(await LoadJpegFromUrlAsync(image, media.IndexOf(item), isVideo))));
            }
            Task t = Task.WhenAll(tasks);
            await t;


            bitmaps = keyValuePairs.Where(f => f != null).OrderBy(e => e.Value.Key).Select(g => g.Value.Value).ToList();
            return bitmaps.Where(e => e != null).ToList() as List<SKBitmap>;
        }

        private async Task<KeyValuePair<int, SKBitmap?>?> LoadJpegFromUrlAsync(string imageUrl, int index, bool isVideo = false)
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    byte[] imageBytes = await client.GetByteArrayAsync(imageUrl);

                    using (var stream = new SKMemoryStream(imageBytes))
                    {
                        var bitmap = SKBitmap.Decode(stream);
                        if (isVideo)
                        {
                            SKCanvas canvas = new SKCanvas(bitmap);
                            var bytes = System.IO.File.ReadAllBytes(Path.Combine(_env.WebRootPath, "video.png"));
                            SKBitmap videoBitmap = SKBitmap.Decode(bytes);
                            var paint = new SKPaint
                            {
                                Color = SKColors.White.WithAlpha(220)

                            };

                            float x = 10;
                            float y = bitmap.Height - videoBitmap.Height - 10;

                            //canvas.DrawText("Video", new SKPoint(x,y), new SKTextAlign(), new SKFont(size:16, typeface: SKTypeface.FromFamilyName("Roboto")), paint);

                            canvas.DrawBitmap(videoBitmap, new SKPoint(x, y), paint);

                            canvas.Flush();
                            canvas.Dispose();

                        }
                        return new KeyValuePair<int, SKBitmap?>(index, bitmap);
                    }
                }
                catch
                {
                    return null;
                }
            }


        }
        private string SanitizeFileName(string input, string replacement = "_")
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                input = input.Replace(c.ToString(), replacement);
            }
            return input;
        }
        private SKColor GetAverageColor(List<SKBitmap> bitmaps, bool skipTransparent = true, int step = 1)
        {
            if (bitmaps == null || bitmaps.Count == 0)
                return SKColors.Transparent;

            long totalR = 0, totalG = 0, totalB = 0;
            int counted = 0;

            foreach (var bitmap in bitmaps)
            {
                for (int y = 0; y < bitmap.Height; y += step)
                {
                    for (int x = 0; x < bitmap.Width; x += step)
                    {
                        var color = bitmap.GetPixel(x, y);

                        if (skipTransparent && color.Alpha == 0)
                            continue;

                        totalR += color.Red;
                        totalG += color.Green;
                        totalB += color.Blue;
                        counted++;
                    }
                }
            }

            if (counted == 0)
                return SKColors.Transparent; // No visible pixels found

            return new SKColor(
                (byte)(totalR / counted),
                (byte)(totalG / counted),
                (byte)(totalB / counted)
            );
        }


        public InstagramPostDetails ExtractInstagramPostDetails(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var item = root
                .GetProperty("data")
                .GetProperty("xdt_api__v1__media__shortcode__web_info")
                .GetProperty("items")[0];

            var user = item.GetProperty("user");

            var details = new InstagramPostDetails
            {
                Username = user.GetProperty("username").GetString(),
                Name = user.GetProperty("full_name").GetString(),
                Avatar = user.GetProperty("profile_pic_url").GetString(),
                Likes = item.TryGetProperty("like_count", out var likes) ? likes.GetInt32() : 0,
                Comments = item.TryGetProperty("comment_count", out var comments) ? comments.GetInt32() : 0,
                Description =
                    item.TryGetProperty("caption", out var caption) &&
                    caption.ValueKind == JsonValueKind.Object &&
                    caption.TryGetProperty("text", out var textProp)
                        ? textProp.GetString() ?? string.Empty
                        : string.Empty
            };

            return details;
        }


        public async Task<string> FetchInstagramPostAsync(string shortcode)
        {
            HttpClient _proxyClient = new HttpClient(handler: new HttpClientHandler()
            {
                Proxy = new WebProxy("http://geo.iproyal.com:12321")
                {
                    Credentials = new NetworkCredential(
                    // your credentials
                    )
                },
                UseProxy = true
            });

            _proxyClient.DefaultRequestHeaders.Add("Accept", "*/*");
            _proxyClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _proxyClient.DefaultRequestHeaders.Add("Origin", "https://www.instagram.com");
            _proxyClient.DefaultRequestHeaders.Add("Priority", "u=1, i");
            _proxyClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
            _proxyClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
            _proxyClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
            _proxyClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36");

            _proxyClient.DefaultRequestHeaders.Add("X-Asbd-Id", "129477");
            _proxyClient.DefaultRequestHeaders.Add("X-Bloks-Version-Id", "e2004666934296f275a5c6b2c9477b63c80977c7cc0fd4b9867cb37e36092b68");
            _proxyClient.DefaultRequestHeaders.Add("X-Fb-Friendly-Name", "PolarisPostActionLoadPostQueryQuery");
            _proxyClient.DefaultRequestHeaders.Add("X-Ig-App-Id", "936619743392459");

            //string csrfToken = string.Empty;

            //for (int i = 0; i < 3; i++)
            //{
            //    var initialResponse = await _proxyClient.GetAsync("https://www.instagram.com");
            //    var responseString = await initialResponse.Content.ReadAsStringAsync();

            //    initialResponse.Headers.TryGetValues("Set-Cookie", out var cookies);
            //    if (cookies != null)
            //    {
            //        csrfToken = cookies.Where(e => e.StartsWith("csrftoken")).First().Split(";").First();
            //        break;
            //    }

            //    if (i == 2)
            //    {
            //        return string.Empty;
            //    }

            //}

            Session session = Db.Sessions.OrderBy(r => Guid.NewGuid()).Take(1).First();
            var csrfToken = session.CSRFToken;


            var request = new HttpRequestMessage(HttpMethod.Post, "https://www.instagram.com/graphql/query/");


            // ----- HEADERS -----
            request.Headers.Add("Cookie", csrfToken);

            //request.Headers.Add("X-Csrftoken", "zCdLU4qMl7i2wlrsVBgh22hJNXKPxPKp");
            request.Headers.Add("x-root-field-name", "xdt_api__v1__web__accounts__get_encrypted_credentials");
            request.Headers.Add("X-Fb-Lsd", "lvKgZqkPPmLKqUfKIBiMFa");
            request.Headers.Add("X-Csrftoken", csrfToken.Split("=").Last());
            request.Headers.Add("Referer", $"https://www.instagram.com/p/{shortcode}");


            // ----- BODY -----
            var body = new Dictionary<string, string>
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

            request.Content = new FormUrlEncodedContent(body);

            // ----- SEND -----
            var response = await _proxyClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            return content;
        }

    }


    // Models

    public class InstagramMedia
    {
        public string url { get; set; } = string.Empty;
        public string thumbnail { get; set; } = string.Empty;
        public string type { get; set; } = string.Empty;
    }

    public class InstagramData
    {
        public List<InstagramMedia> media { get; set; } = new();
    }

    public class InstagramUrl
    {
        public bool success { get; set; }
        public InstagramData data { get; set; } = new();
    }

    public class InstagramResponse
    {
        public InstagramUrl url { get; set; } = new();
    }

    public class InstagramPostDetails
    {
        public string? Username { get; set; } = string.Empty;
        public string? Name { get; set; } = string.Empty;
        public string? Avatar { get; set; } = string.Empty;
        public int Likes { get; set; }
        public int Comments { get; set; }
        public int Shares { get; set; }
        public string? Description { get; set; } = string.Empty;
    }




    public class OEmbedModel
    {
        public string version { get; set; }
        public string type { get; set; }
        public string author_name { get; set; }
        public string author_url { get; set; }
        public string provider_name { get; set; }
        public string provider_url { get; set; }
        public string title { get; set; }
    }
    public class Account
    {
        public string id { get; set; }
        public string display_name { get; set; }
        public string username { get; set; }
        public string acct { get; set; }
        public string url { get; set; }
        public DateTime created_at { get; set; }
        public bool locked { get; set; }
        public bool bot { get; set; }
        public bool discoverable { get; set; }
        public bool indexable { get; set; }
        public bool group { get; set; }
        public string avatar { get; set; }
        public string avatar_static { get; set; }
        public object header { get; set; }
        public object header_static { get; set; }
        public int statuses_count { get; set; }
        public bool hide_collections { get; set; }
        public bool noindex { get; set; }
        public List<object> emojis { get; set; } = new List<object>();
        public List<object> roles { get; set; } = new List<object>();
        public List<object> fields { get; set; } = new List<object>();
    }

    public class Application
    {
        public string name { get; set; }
        public string website { get; set; }
    }

    public class MediaAttachment
    {
        public string id { get; set; }
        public string type { get; set; }
        public string url { get; set; }
        public string preview_url { get; set; }
        public object remote_url { get; set; }
        public object preview_remote_url { get; set; }
        public object text_url { get; set; }
        public object description { get; set; }
        public Meta meta { get; set; }
    }

    public class Meta
    {
        public Original original { get; set; }
    }

    public class Original
    {
        public int width { get; set; }
        public int height { get; set; }
    }

    public class ActivityPubModel
    {
        public string id { get; set; }
        public string url { get; set; }
        public string uri { get; set; }
        public DateTime created_at { get; set; }
        public string content { get; set; }
        public string spoiler_text { get; set; }
        public object language { get; set; }
        public string visibility { get; set; }
        public Application application { get; set; }
        public List<MediaAttachment> media_attachments { get; set; }
        public Account account { get; set; }
        public List<object> mentions { get; set; }
        public List<object> tags { get; set; }
        public List<object> emojis { get; set; }
        public object card { get; set; }
        public object poll { get; set; }
    }
}

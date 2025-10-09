using InstagramEmbedForDiscord.DAL;
using InstagramEmbedForDiscord.Models;
using InstagramEmbedForDiscord.Models.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
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

        public HomeController(ILogger<HomeController> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            var httpContext = context.HttpContext;

            Task.Run(() =>
            {
                var dbContext = new IGContext();

                var log = ActionLog.CreateActionLog(httpContext);

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
                int orderIndex = -1;
                string? lastSegment = segments.LastOrDefault();

                if (int.TryParse(lastSegment, out int parsedIndex))
                {
                    orderIndex = parsedIndex <= 0 ? 1 : parsedIndex;
                    segments = segments.Take(segments.Length - 1).ToArray();
                }
                else if (imgIndex.HasValue)
                {
                    orderIndex = imgIndex.Value <= 0 ? 1 : imgIndex.Value;
                }

                string? id = segments.LastOrDefault(); // hash
                string? type = segments.Length > 1 ? segments[^2] : segments.FirstOrDefault(); // p, reel, etc.
                string? username = segments.Length > 2 ? segments[0] : null;

                var postType = PostType.Post;

                if (username?.ToLower() == "stories")
                {
                    username = type;
                    type = $"stories/{username}";
                    postType = PostType.Story;
                }
                else if (username?.ToLower() == "share")
                {
                    type = $"share/{type}";
                    postType = PostType.Share;
                }

                string link = $"https://instagram.com/{type}/{id}/";
                ViewBag.PostId = id;
                ViewBag.Order = orderIndex;
                ViewBag.PostType = postType;

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Mozilla", "5.0"));
                using var db = new IGContext();

                var dbPost = db.Posts.FirstOrDefault(e => e.ID == id && e.PostType == postType);

                if (dbPost != null && dbPost.SnapSaveEntries.Any())
                {
                    var firstEntry = dbPost.SnapSaveEntries.First();
                    bool linkStillValid = await IsMediaUrlValidAsync(client, firstEntry.MediaUrl);

                    if (linkStillValid)
                    {
                        return await ProcessMedia(orderIndex, dbPost, client, link, id);
                    }

                    // ✅ 3. If invalid, refresh media links
                    var refreshed = await GetSnapsaveResponse(link, client);
                    var newMedia = refreshed.url?.data?.media;
                    if (newMedia != null && newMedia.Any())
                    {
                        dbPost.SnapSaveEntries.Clear();

                        for (int i = 0; i < newMedia.Count; i++)
                        {
                            var item = newMedia.ElementAt(i);
                            dbPost.SnapSaveEntries.Add(new SnapSaveEntry
                            {
                                MediaType = item.type == "video" ? MediaType.Video : MediaType.Image,
                                MediaUrl = item.url,
                                Order = i,
                                ThumbnailUrl = item.thumbnail,
                                UpdatedAt = DateTime.Now
                            });
                        }

                        db.SaveChanges();

                        return await ProcessMedia(orderIndex, dbPost, client, link, id);
                    }
                }

                var instagramResponse = await GetSnapsaveResponse(link, client);
                var media = instagramResponse.url?.data?.media;
                if (media == null || media.Count == 0)
                    return BadRequest("No media found.");

                InstagramPostDetails? postDetails = null;

                if (Request.Host.Host.EndsWith("d.vxinstagram.com", StringComparison.OrdinalIgnoreCase) || true)
                {
                    postDetails = await GetScrapedPostDetailsFromAPIFY(client, link);
                    ViewBag.PostDetails = postDetails;
                }

                dbPost = new Post
                {
                    ID = id!,
                    AuthorName = postDetails?.Username ?? string.Empty,
                    AuthorUsername = postDetails?.DisplayName ?? string.Empty,
                    Caption = postDetails?.Caption ?? string.Empty,
                    Comments = postDetails?.Comments ?? 0,
                    Likes = postDetails?.Likes ?? 0,
                    RawUrl = link,
                    PostType = postType,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    SnapSaveEntries = media.Select((m, i) => new SnapSaveEntry
                    {
                        MediaType = m.type == "video" ? MediaType.Video : MediaType.Image,
                        MediaUrl = m.url,
                        Order = i,
                        ThumbnailUrl = m.thumbnail
                    }).ToList()
                };

                db.Posts.Add(dbPost);
                db.SaveChanges();

                if (media.Count == 1)
                    return ProcessSingleItem(media.First(), client, link);

                if (orderIndex > 0 && orderIndex <= media.Count)
                    return ProcessSingleItem(media[orderIndex - 1], client, link);

                return await ProcessMultipleItems(media, link, id);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return View("Error");
            }
        }


        private static async Task<bool> IsMediaUrlValidAsync(HttpClient client, string url)
        {
            try
            {
                var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
                var response = await client.SendAsync(headRequest);
                if (!response.IsSuccessStatusCode) return false;

                var type = response.Content.Headers.ContentType?.MediaType;
                return type != null && (type.StartsWith("image") || type.StartsWith("video"));
            }
            catch
            {
                return false;
            }
        }

        private async Task<IActionResult> ProcessMedia(int orderIndex, Post dbPost, HttpClient client, string link, string id)
        {
            if (orderIndex > 0 && orderIndex <= dbPost.SnapSaveEntries.Count)
                return ProcessSingleItem(new InstagramMedia
                {
                    url = dbPost.SnapSaveEntries.ElementAt(orderIndex - 1).MediaUrl,
                    thumbnail = dbPost.SnapSaveEntries.ElementAt(orderIndex - 1).ThumbnailUrl,
                    type = dbPost.SnapSaveEntries.ElementAt(orderIndex - 1).MediaType.ToString().ToLower()
                }, client, link);

            if (dbPost.SnapSaveEntries.Count == 1)
                return ProcessSingleItem(new InstagramMedia
                {
                    url = dbPost.SnapSaveEntries.First().MediaUrl,
                    thumbnail = dbPost.SnapSaveEntries.First().ThumbnailUrl,
                    type = dbPost.SnapSaveEntries.First().MediaType.ToString().ToLower()
                }, client, link);

            return await ProcessMultipleItems(
                dbPost.SnapSaveEntries.Select(e => new InstagramMedia
                {
                    url = e.MediaUrl,
                    thumbnail = e.ThumbnailUrl,
                    type = e.MediaType.ToString().ToLower()
                }).ToList(),
                link,
                id
            );
        }



        [Route("/")]
        public IActionResult HomePage()
        {
            return View();
        }

        [Route("/generated/{fileName}")]
        public async Task<IActionResult> Generated(string fileName)
        {
            string folderPath = Path.Combine(_env.WebRootPath, "generated");
            string filePath = Path.Combine(folderPath, fileName);

            if (!System.IO.File.Exists(filePath))
            {
                var postId = Path.GetFileNameWithoutExtension(fileName);
                using (HttpClient client = new HttpClient())
                {
                    var instagramResponse = await GetSnapsaveResponse("https://instagram.com/p/" + postId, client);
                    if (instagramResponse.url?.data?.media == null || instagramResponse.url.data.media.Count == 0)
                        return NotFound();

                    var newFileName = await GetGeneratedFile(instagramResponse.url?.data?.media ?? new List<InstagramMedia>(), postId);
                    if (newFileName == null) return NotFound();

                    string newFilePath = Path.Combine(folderPath, newFileName);

                    var newImageBytes = System.IO.File.ReadAllBytes(filePath);
                    return File(newImageBytes, "image/jpeg");
                }
            }

            var imageBytes = System.IO.File.ReadAllBytes(filePath);
            return File(imageBytes, "image/jpeg");
        }

        [Route("/VerifySnapsaveLink")]
        public async Task<IActionResult> VerifySnapsaveLink(string rapidsaveUrl, string postId, int? order)
        {
            int orderIndex = (order ?? 1) - 1;
            if (orderIndex < 0) orderIndex = 0;

            using (HttpClient client = new HttpClient())
            {
                try
                {
                    var headRequest = new HttpRequestMessage(HttpMethod.Head, rapidsaveUrl);
                    var response = await client.SendAsync(headRequest);

                    bool linkStillValid = response.IsSuccessStatusCode;

                    if (linkStillValid)
                    {
                        if (response.Content.Headers.ContentType == null ||
                            !response.Content.Headers.ContentType.MediaType.StartsWith("image") &&
                            !response.Content.Headers.ContentType.MediaType.StartsWith("video"))
                        {
                            linkStillValid = false;
                        }
                    }

                    if (linkStillValid)
                    {
                        return Redirect(rapidsaveUrl);
                    }

                    var instagramResponse = await GetSnapsaveResponse($"https://instagram.com/p/{postId}/", client);
                    var mediaList = instagramResponse.url?.data?.media;

                    if (mediaList == null || mediaList.Count == 0)
                        return BadRequest("No media found for this post.");

                    if (orderIndex >= mediaList.Count)
                        orderIndex = 0;

                    var media = mediaList[orderIndex];

                    return Redirect(media.url);
                }
                catch (Exception ex)
                {
                    return View("Error");
                }
            }
        }



        [Route("/offload/{type}/{id}/{order?}")]
        public async Task<IActionResult> OffloadPost(PostType type, string id, int? order)
        {
            int orderIndex = (order ?? 1) - 1;
            if (orderIndex < 0) orderIndex = 0;

            IGContext db = new IGContext();



            var post = db.Posts.FirstOrDefault(e => e.ID == id && e.PostType == type);

            if (post == null)
                return NotFound();

            var entry = post.SnapSaveEntries.ElementAtOrDefault(orderIndex);

            // if for some reason the order is out of bounds, which can only happen if order > length, take the last entry
            entry ??= post.SnapSaveEntries.LastOrDefault();
            if (entry == null)
                return NotFound();

            using (HttpClient client = new HttpClient())
            {
                try
                {
                    var headRequest = new HttpRequestMessage(HttpMethod.Head, entry.MediaUrl);
                    var response = await client.SendAsync(headRequest);

                    bool linkStillValid = response.IsSuccessStatusCode;

                    if (linkStillValid)
                    {
                        if (response.Content.Headers.ContentType == null || response.Content.Headers.ContentType.MediaType == null ||
                            !(response.Content.Headers.ContentType.MediaType.StartsWith("image") ||
                            response.Content.Headers.ContentType.MediaType.StartsWith("video")))
                        {
                            linkStillValid = false;
                        }
                    }

                    if (linkStillValid)
                    {
                        return Redirect(entry.MediaUrl);
                    }

                    var instagramResponse = await GetSnapsaveResponse(post.RawUrl, client);
                    var mediaList = instagramResponse.url?.data?.media;

                    if (mediaList == null || mediaList.Count == 0)
                        return BadRequest("No media found for this post.");

                    if (orderIndex >= mediaList.Count)
                        orderIndex = 0;

                    var media = mediaList[orderIndex];

                    entry.MediaUrl = media.url;
                    entry.MediaType = media.type == "video" ? MediaType.Video : MediaType.Image;

                    post.UpdatedAt = DateTime.Now;
                    entry.UpdatedAt = DateTime.Now;

                    db.SaveChanges();

                    return Redirect(media.url);
                }

                catch (Exception ex)
                {
                    return BadRequest();
                }
            }

        }


        [Route("/oembed")]
        public IActionResult OEmbed(string username, string? desc)
        {
            return Json(new OEmbedModel()
            {
                author_name = desc != null ? !desc.IsNullOrEmpty() ? desc : $"@{username}" : $"@{username}",
                author_url = "https://instagram.com/" + username,
                provider_name = "vxinstagram",
                provider_url = "https://github.com/Lainmode/InstagramEmbed-vxinstagram",
                title = "",
                type = "video",
                version = "1.0"
            });
        }


        [Route("/users/username/statuses/7523033599046667550")]
        public IActionResult Activity(string link, string username, string avatar, string likes, string mediaType, string postId)
        {
            Response.Headers.Remove("X-Robots-Tag");
            return Content("{\"id\":\"7523033599046667550\",\"url\":\"https://tiktok.com/@www.cherryfairy.com/video/7523033599046667550\",\"uri\":\"https://tiktok.com/@www.cherryfairy.com/video/7523033599046667550\",\"created_at\":\"2025-07-04T01:32:52.000Z\",\"content\":\"<b>❤️6.0M💬3.7k🔁141.6k</b>\",\"spoiler_text\":\"\",\"language\":null,\"visibility\":\"public\",\"application\":{\"name\":\"fxTikTok\",\"website\":\"https://github.com/okdargy/fxTikTok\"},\"media_attachments\":[{\"id\":\"7523033599046667550-video\",\"type\":\"video\",\"url\":\"https://offload.tnktok.com/generate/video/7523033599046667550\",\"preview_url\":\"https://offload.tnktok.com/generate/cover/7523033599046667550\",\"remote_url\":null,\"preview_remote_url\":null,\"text_url\":null,\"description\":null,\"meta\":{\"original\":{\"width\":576,\"height\":1024}}}],\"account\":{\"id\":\"www.cherryfairy.com\",\"display_name\":\"Cherryfairy\",\"username\":\"www.cherryfairy.com\",\"acct\":\"www.cherryfairy.com\",\"url\":\"https://tiktok.com/@www.cherryfairy.com\",\"created_at\":\"2022-06-20T20:29:27.000Z\",\"locked\":false,\"bot\":false,\"discoverable\":true,\"indexable\":false,\"group\":false,\"avatar\":\"https://offload.tnktok.com/generate/pfp/www.cherryfairy.com\",\"avatar_static\":\"https://offload.tnktok.com/generate/pfp/www.cherryfairy.com\",\"header\":null,\"header_static\":null,\"statuses_count\":0,\"hide_collections\":false,\"noindex\":false,\"emojis\":[],\"roles\":[],\"fields\":[]},\"mentions\":[],\"tags\":[],\"emojis\":[],\"card\":null,\"poll\":null}", "application/activity+json; charset=utf-8");
            var model = new ActivityPubModel()
            {
                account = new Account()
                {
                    id = username,
                    display_name = username,
                    username = username,
                    url = "https://instagram.com/" + username,
                    avatar = "https://offload.tnktok.com/generate/pfp/www.cherryfairy.com",
                    avatar_static = "https://offload.tnktok.com/generate/pfp/www.cherryfairy.com",
                    created_at = DateTime.UtcNow,
                    acct = username,

                },
                media_attachments = new List<MediaAttachment>()
                {
                    new MediaAttachment()
                    {
                        id=postId,
                        type = mediaType,
                        url = link,
                        preview_url = link,
                        meta = new Meta()
                        {
                            original = new Original()
                            {
                                width = 576,
                                height = 1024
                            }
                        }
                    }
                },
                content = $"<b>❤️ {likes}</b>",
                id = postId,
                url = "www.instagram.com/p/" + postId + "/",
                application = new Application()
                {
                    name = "vxinstagram",
                    website = "https://github.com/Lainmode/InstagramEmbed-vxinstagram"
                },
                created_at = DateTime.UtcNow,
                visibility = "public",
                uri = "www.instagram.com/p/" + postId + "/"

            };

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            };

            string json = System.Text.Json.JsonSerializer.Serialize(model, options);
            return Content(json, "application/json");
        }

        private async Task<InstagramPostDetails> GetScrapedPostDetailsFromAPIFY(HttpClient client, string postUrl)
        {
            postUrl = postUrl.Replace("reels", "reel");

            var apiUrl = "YOUR_API";

            var jsonPayload = $@"{{
                ""addParentData"": false,
                ""directUrls"": [
                    ""{postUrl}""
                ],
                ""enhanceUserSearchWithFacebookPage"": false,
                ""isUserReelFeedURL"": false,
                ""isUserTaggedFeedURL"": false,
                ""resultsLimit"": 1,
                ""resultsType"": ""details"",
                ""searchLimit"": 1,
                ""searchType"": ""hashtag""
            }}";
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync(apiUrl, content);
                response.EnsureSuccessStatusCode();

                var responseBody = await response.Content.ReadAsStringAsync();
                var scraperDetails = JsonConvert.DeserializeObject<List<ScraperInstagramPost>>(
                     responseBody,
                     new JsonSerializerSettings
                     {
                         ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
                         {
                             NamingStrategy = new Newtonsoft.Json.Serialization.DefaultNamingStrategy()
                         },
                         MissingMemberHandling = MissingMemberHandling.Ignore,
                         NullValueHandling = NullValueHandling.Ignore
                     })!.First();

                return new InstagramPostDetails()
                {
                    Username = scraperDetails.OwnerUsername,
                    DisplayName = scraperDetails.OwnerFullName,
                    Caption = scraperDetails.Caption,
                    Likes = scraperDetails.LikesCount,
                    Comments = scraperDetails.CommentsCount,
                    Views = scraperDetails.VideoViewCount,
                    AudioName = $"{scraperDetails.MusicInfo?.SongName} - {scraperDetails.MusicInfo?.ArtistName}",
                };

            }
            catch (Exception e)
            {
                return new InstagramPostDetails();
            }
        }

        private async Task<InstagramPostDetails> GetPostDetails(HttpClient client, string id)
        {
            try
            {
                var embedUrl = $"https://www.instagram.com/p/{id}/embed/captioned/";
                var html = await client.GetStringAsync(embedUrl);

                // Username
                var usernameMatch = Regex.Match(html, @"<span class=""UsernameText"">(.*?)</span>");
                string? username = usernameMatch.Success ? usernameMatch.Groups[1].Value : null;

                // Likes
                var likesMatch = Regex.Match(html, @"(\d[\d,\.]*) likes");
                string? likes = likesMatch.Success ? likesMatch.Groups[1].Value : null;

                // Profile avatar
                var imgMatch = Regex.Match(html, @"<a class=""Avatar InsideRing"".*?<img src=""(.*?)""", RegexOptions.Singleline);
                string? profileImg = imgMatch.Success ? imgMatch.Groups[1].Value : null;

                // 🆕 Extract description
                string? description = null;
                var captionMatch = Regex.Match(
                    html,
                    @"<div class=""Caption"">(.*?)(<div class=""CaptionComments"">|</div>)",
                    RegexOptions.Singleline
                );
                if (captionMatch.Success)
                {
                    var rawCaptionHtml = captionMatch.Groups[1].Value;

                    rawCaptionHtml = Regex.Replace(rawCaptionHtml, @"<a class=""CaptionUsername"".*?</a>", "", RegexOptions.Singleline);

                    rawCaptionHtml = Regex.Replace(rawCaptionHtml, @"<br\s*/?>", "\n");

                    rawCaptionHtml = Regex.Replace(rawCaptionHtml, @"<.*?>", "");

                    description = System.Net.WebUtility.HtmlDecode(rawCaptionHtml).Trim();
                }

                return new InstagramPostDetails
                {
                    Username = username,
                    Avatar = profileImg,
                    Likes = likes != null ? int.Parse(likes) : 0,
                    Caption = description
                };
            }
            catch
            {
                return new InstagramPostDetails();
            }
        }


        private async Task<InstagramResponse> GetSnapsaveResponse(string link, HttpClient client)
        {

            HttpResponseMessage snapSaveResponse = await client.GetAsync("http://alsauce.com:3200/igdl?url=" + link);
            string snapSaveResponseString = await snapSaveResponse.Content.ReadAsStringAsync();
            InstagramResponse instagramResponse = JsonConvert.DeserializeObject<InstagramResponse>(snapSaveResponseString)!;

            return instagramResponse;

        }

        private IActionResult ProcessSingleItem(InstagramMedia media, HttpClient client, string originalLink)
        {
            var contentUrl = media.url;
            var thumbnailUrl = media.thumbnail;

            bool isPhoto = media.type == "image";

            if (isPhoto)
            {
                //var imageBytes = await client.GetByteArrayAsync(contentUrl);

                ViewBag.IsPhoto = true;
                ViewBag.Files = new List<InstagramMedia>() { media };
                return View(new string[] { contentUrl, thumbnailUrl, originalLink });
                //return File(imageBytes, "image/jpeg");
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
        private static string SanitizeFileName(string input, string replacement = "_")
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                input = input.Replace(c.ToString(), replacement);
            }
            return input;
        }
        private static SKColor GetAverageColor(List<SKBitmap> bitmaps, bool skipTransparent = true, int step = 1)
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



    }







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
        public string? DisplayName { get; set; } = string.Empty;
        public string? Avatar { get; set; } = string.Empty;
        public int Likes { get; set; }
        public int Comments { get; set; }
        public int Views { get; set; }
        public string? Caption { get; set; } = string.Empty;
        public string? AudioName { get; set; } = string.Empty;
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
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
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


    public class ScraperInstagramPost
    {
        public string InputUrl { get; set; }
        public string Id { get; set; }
        public string Type { get; set; }
        public string ShortCode { get; set; }
        public string Caption { get; set; }
        public List<string> Hashtags { get; set; }
        public List<string> Mentions { get; set; }
        public string Url { get; set; }
        public int CommentsCount { get; set; }
        public int DimensionsHeight { get; set; }
        public int DimensionsWidth { get; set; }
        public string DisplayUrl { get; set; }
        public List<string> Images { get; set; }
        public string VideoUrl { get; set; }
        public string Alt { get; set; }
        public int LikesCount { get; set; }
        public int VideoViewCount { get; set; }
        public int VideoPlayCount { get; set; }
        public DateTime Timestamp { get; set; }
        public List<object> ChildPosts { get; set; }
        public string OwnerFullName { get; set; }
        public string OwnerUsername { get; set; }
        public string OwnerId { get; set; }
        public string ProductType { get; set; }
        public double VideoDuration { get; set; }
        public bool IsSponsored { get; set; }
        public MusicInfo MusicInfo { get; set; }
        public bool IsCommentsDisabled { get; set; }
    }

    public class MusicInfo
    {
        public string ArtistName { get; set; }
        public string SongName { get; set; }
        public bool UsesOriginalAudio { get; set; }
        public bool ShouldMuteAudio { get; set; }
        public string ShouldMuteAudioReason { get; set; }
        public string AudioId { get; set; }
    }

}

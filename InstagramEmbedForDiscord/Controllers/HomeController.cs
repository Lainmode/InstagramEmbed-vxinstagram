using Azure;
using InstagramEmbed.Application.Helpers;
using InstagramEmbed.DataAccess;
using InstagramEmbed.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using SkiaSharp;
using System;
using System.Buffers.Text;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
        private readonly HttpClient _regularClient;
        private readonly IDbContextFactory<InstagramContext> _dbContextFactory;

        private InstagramContext Db;

        public HomeController(ILogger<HomeController> logger, IWebHostEnvironment env, IHttpClientFactory factory, IDbContextFactory<InstagramContext> dbContextFactory)
        {
            _regularClient = factory.CreateClient("regular");
            _logger = logger;
            _env = env;
            _dbContextFactory = dbContextFactory;

            Db = _dbContextFactory.CreateDbContext();
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            var httpContext = context.HttpContext;

            Task.Run(() =>
            {
                var db = _dbContextFactory.CreateDbContext();

                var ipAddress = "127.0.0.1";

                var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrEmpty(forwardedFor))
                    ipAddress = forwardedFor.Split(',')[0];

                ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? ipAddress;

                var log = ActionLog.CreateActionLog(httpContext.Request.Method, httpContext.Request.Path + httpContext.Request.QueryString, httpContext.Request.Headers["User-Agent"].ToString(), ipAddress);

                db.ActionLogs.Add(log);
                db.SaveChanges();
            });

        }

        [Route("{**path}")]
        public async Task<IActionResult> Index(string path, [FromQuery(Name = "img_index")] int? imgIndex)
        {
            try
            {

                if (string.IsNullOrWhiteSpace(path))
                    return BadRequest("Invalid Instagram path.");

                if (path.Contains("VerifySnapsaveLink") || path.Contains(".jpg"))
                    return BadRequest("Invalid Instagram path.");

                bool scrapeForPostInfomration = Request.Host.Host.EndsWith("d.vxinstagram.com", StringComparison.OrdinalIgnoreCase);

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
                    await RefreshPostIfNeeded(post, scrapeForPostInfomration);

                    ViewBag.Post = post;
                    return ProcessMedia(post, post.RawUrl, id, orderIndex, orderSpecified);
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


                Post? newPost = null;

                if (scrapeForPostInfomration)
                {
                    newPost = await FetchPostFromGraphQL(link, id);
                }

                else
                {
                    newPost = await FetchPostFromSnapSave(link, id);
                }

                if (newPost == null) return NotFound();

                try
                {
                    Db.Posts.Add(newPost);
                    Db.SaveChanges();
                    post = newPost;
                }
                catch (DbUpdateException f)
                {
                    post = Db.Posts.Find(id);

                    if (post!.AuthorUsername == "NOT_SET" && newPost.AuthorUsername != "NOT_SET")
                    {
                        post.AuthorUsername = newPost.AuthorUsername;
                        post.AvatarUrl = newPost.AvatarUrl;
                        post.AuthorName = newPost.AuthorName;
                        post.Caption = newPost.Caption;
                        post.Comments = newPost.Comments;
                        post.Likes = newPost.Likes;

                        post.Height = newPost.Height;
                        post.Width = newPost.Width;

                        post.DefaultThumbnailUrl = newPost.DefaultThumbnailUrl;
                        post.TrackName = newPost.TrackName;

                        Db.SaveChanges();
                    }
                }

                ViewBag.Post = post;
                return ProcessMedia(post!, post.RawUrl, id, orderIndex, orderSpecified);
            }
            catch (Exception ex)
            {
                //Console.WriteLine(ex.ToString());
                return View("Error");
            }
        }

        public async Task<Post> FetchPostFromGraphQL(string link, string id)
        {
            InstagramResponse? instagramResponse = null;

            _ = Task.Run(async () => instagramResponse = await GetSnapsaveResponse(link));

            InstagramPostDetails postDetails = await GetPostDetails(id);

            Post newPost = new Post()
            {
                RawUrl = link,
                AuthorName = postDetails.Name,
                AvatarUrl = postDetails.Avatar,
                AuthorUsername = postDetails.Username,
                Caption = postDetails.Description,
                Comments = postDetails.Comments,
                Likes = postDetails.Likes,
                ShortCode = id,
                Height = postDetails.VideoHeight ?? 720,
                Width = postDetails.VideoWidth ?? 1280,
                DefaultThumbnailUrl = postDetails.VideoThumbnail,
                TrackName = postDetails.TrackName,
                ExpiresOn = DateTime.UtcNow.AddMinutes(5)
            };

            foreach (var item in postDetails.Media)
            {
                newPost.Media.Add(item);
            }

            if (newPost.Media.IsNullOrEmpty())
            {
                Console.WriteLine($"Emtpy: {postDetails.json}");

                var alternateInstagramResponse = await GetSnapsaveResponse(link);
                var media = alternateInstagramResponse.url?.data?.media;
                InstagramPostDetails alternativePostDetails = new InstagramPostDetails() { Username = "NOT_SET" };

                if (media == null || media.Count == 0)
                    throw new Exception(link);

                newPost.DefaultThumbnailUrl = postDetails.VideoThumbnail;
                foreach (var item in media)
                {
                    newPost.Media.Add(new Media() { RapidSaveUrl = item.url, MediaType = item.type, ThumbnailUrl = item.thumbnail });
                }
            }

            return newPost;

        }

        public async Task<Post> FetchPostFromSnapSave(string link, string id)
        {
            var instagramResponse = await GetSnapsaveResponse(link);
            var media = instagramResponse.url?.data?.media;
            InstagramPostDetails postDetails = new InstagramPostDetails() { Username = "NOT_SET" };

            if (media == null || media.Count == 0)
                throw new Exception(link);

            Post newPost = new Post()
            {
                RawUrl = link,
                AuthorName = postDetails.Name,
                AvatarUrl = postDetails.Avatar,
                AuthorUsername = postDetails.Username,
                Caption = postDetails.Description,
                Comments = postDetails.Comments,
                Likes = postDetails.Likes,
                ShortCode = id,
                Height = postDetails.VideoHeight ?? 720,
                Width = postDetails.VideoWidth ?? 1280,
                DefaultThumbnailUrl = postDetails.VideoThumbnail,
                TrackName = postDetails.TrackName
            };

            foreach (var item in media)
            {
                newPost.Media.Add(new Media() { RapidSaveUrl = item.url, MediaType = item.type, ThumbnailUrl = item.thumbnail });
            }

            return newPost;
        }



        [Route("/")]
        public IActionResult HomePage()
        {
            return View();
        }

        [Route("/offload/{id}/{order?}")]
        [Route("/offload/{id}")]
        public async Task<IActionResult> OffloadPost(string id, int? order, bool? thumbnail = false)
        {
            int orderIndex = (order ?? 0);
            if (orderIndex < 0) orderIndex = 0;


            var post = Db.Posts.Find(id);

            if (post == null)
                return NotFound();

            await RefreshPostIfNeeded(post, false);

            var entry = post.Media.ElementAtOrDefault(orderIndex);

            // if for some reason the order is out of bounds, which can only happen if order > length, take the last entry
            entry ??= post.Media.LastOrDefault();
            if (entry == null)
                return NotFound();

            if (thumbnail ?? false)
            {
                return Redirect(entry.MediaType == "video" ? post.DefaultThumbnailUrl ?? entry.ThumbnailUrl : entry.ThumbnailUrl);
            }

            return Redirect(entry.RapidSaveUrl);

        }


        private IActionResult ProcessMedia(Post dbPost, string link, string id, int orderIndex, bool orderSpecified)
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
                }, link, id);
            }
            return ProcessMultipleItems(
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
                author_name = desc != null ? !desc.IsNullOrEmpty() ? desc : $"{username}" : $"{username}",
                author_url = "https://instagram.com/" + username,
                provider_name = $"vxinstagram {likescomments}",
                provider_url = "https://github.com/Lainmode/InstagramEmbed-vxinstagram",
                title = "",
                type = "video",
                version = "1.0"
            });
        }

        [Route("/api/v1/statuses/{contextBase64}")]
        [Route("/users/{username}/statuses/{contextBase64}")]
        public IActionResult Activity(string contextBase64)
        {
            var base64EncodedBytes = Base64Url.Decode(contextBase64);
            var contextParams = Encoding.UTF8.GetString(base64EncodedBytes).Split("&");

            var postId = contextParams.First();
            var orderString = contextParams.LastOrDefault();

            int order = 0;
            int.TryParse(orderString, out order);


            Post? post = Db.Posts.Find(postId);

            if (post == null)
            {
                return NotFound();
            }

            var media_attachments = new List<MediaAttachment>();

            for (int i = 0; i < post.Media.Count; i++)
            {
                var media = post.Media.ElementAt(i);
                media_attachments.Add(new MediaAttachment()
                {
                    id = postId,
                    type = media.MediaType,
                    url = $"https://{Request.Host}/offload/{postId}?order={i}",
                    preview_url = $"https://{Request.Host}/offload/{postId}?order={i}&thumbnail=true",
                    meta = new Meta()
                    {
                        original = new Original()
                        {
                            height = post.Height,
                            width = post.Width,
                            aspect = post.AspectRatio,
                            size = post.Size
                        }
                    }
                });
            }

            var model = new ActivityPubModel()
            {
                account = new Account()
                {
                    id = post.AuthorUsername ?? string.Empty,
                    display_name = post.AuthorName ?? string.Empty,
                    username = post.AuthorUsername ?? string.Empty,
                    url = "https://instagram.com/" + post.AuthorUsername,
                    uri = "https://instagram.com/" + post.AuthorUsername,
                    avatar = post.AvatarUrl ?? string.Empty,
                    avatar_static = post.AvatarUrl ?? string.Empty,
                    created_at = DateTime.UtcNow,
                    acct = post.AuthorUsername ?? string.Empty,

                },

                media_attachments = media_attachments,

                content = $"<p>{post?.Caption}</p><b>❤️ {post?.Likes}&nbsp;&nbsp;&nbsp;💬 {post?.Comments}</b>",
                id = contextBase64,
                language = "en",

                url = "https://www.instagram.com/p/" + postId + "/",
                application = new Application()
                {
                    name = "vxinstagram",
                    website = "https://github.com/Lainmode/InstagramEmbed-vxinstagram"
                },
                created_at = DateTime.UtcNow,
                visibility = "public",
                uri = "https://www.instagram.com/p/" + postId + "/"

            };

            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            };

            string json = System.Text.Json.JsonSerializer.Serialize(model, options);
            return Content(json, "application/json");
        }


        private async Task<InstagramPostDetails> GetPostDetails(string id, bool skipMediaExtraction = false)
        {
            string response = "";
            try
            {
                response = await FetchInstagramPostAsync(id);
                var post = ExtractInstagramPostDetails(response, skipMediaExtraction);
                return post;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                Console.WriteLine(id);
                Console.WriteLine(response);
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

        private async Task RefreshPostIfNeeded(Post post, bool scrapeForPostInfomration)
        {
            //var response = await _regularClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, post.Media.First().RapidSaveUrl));

            //if (response.IsSuccessStatusCode)
            //{
            //    if (Request.Host.Host.EndsWith("d.vxinstagram.com", StringComparison.OrdinalIgnoreCase) && post.AuthorUsername == "NOT_SET")
            //    {
            //        InstagramPostDetails postDetails = await GetPostDetails(post.ShortCode);
            //        if (postDetails.Username == "NOT_SET") return;
            //        post.AuthorUsername = postDetails.Username;
            //        post.AuthorName = postDetails.Name;
            //        post.Caption = postDetails.Description;
            //        post.Comments = postDetails.Comments;
            //        post.Likes = postDetails.Likes;

            //        Db.SaveChanges();
            //    }
            //    return;
            //}
            if (!post.IsExpired)
            {
                if (scrapeForPostInfomration && post.AuthorUsername == "NOT_SET")
                {
                    InstagramPostDetails postDetails = await GetPostDetails(post.ShortCode, true);
                    if (postDetails.Username == "NOT_SET") return;
                    post.AuthorUsername = postDetails.Username;
                    post.AvatarUrl = postDetails.Avatar;
                    post.AuthorName = postDetails.Name;
                    post.Caption = postDetails.Description;
                    post.Comments = postDetails.Comments;
                    post.Likes = postDetails.Likes;

                    post.Height = postDetails.VideoHeight ?? post.Height;
                    post.Width = postDetails.VideoWidth ?? post.Width;

                    post.DefaultThumbnailUrl = postDetails.VideoThumbnail;
                    post.TrackName = postDetails.TrackName;

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

            post.ExpiresOn = DateTime.UtcNow.AddHours(12);

            Db.SaveChanges();
        }

        private IActionResult ProcessSingleItem(InstagramMedia media, string originalLink, string id)
        {
            //var contentUrl = media.url;
            string contentUrl = $"https://{Request.Host}/offload/{id}";
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

        private IActionResult ProcessMultipleItems(List<InstagramMedia> media, string originalLink, string id)
        {

            string contentUrl = $"https://{Request.Host}/offload/{id}";

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


        public InstagramPostDetails ExtractInstagramPostDetails(string json, bool skipMediaExtraction = false)
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
                       item.TryGetProperty("caption", out var captionObj)
                       && captionObj.ValueKind == JsonValueKind.Object
                       && captionObj.TryGetProperty("text", out var textProp)
                           ? textProp.GetString() ?? string.Empty
                           : string.Empty
            };

            int mediaType = item.GetProperty("media_type").GetInt32();

            JsonElement firstMediaItem = item;

            if (mediaType == 8 && item.TryGetProperty("carousel_media", out var carousel))
            {
                if (carousel.ValueKind == JsonValueKind.Array && carousel.GetArrayLength() > 0)
                    firstMediaItem = carousel[0];
            }

            // Determine if the first media item is video
            int firstType = firstMediaItem.GetProperty("media_type").GetInt32();

            if (firstType == 2)
            {
                details.IsVideo = true;

                details.VideoWidth = firstMediaItem.GetProperty("original_width").GetInt32();
                details.VideoHeight = firstMediaItem.GetProperty("original_height").GetInt32();

                if (firstMediaItem.TryGetProperty("image_versions2", out var imageVersions)
                    && imageVersions.TryGetProperty("candidates", out var candidates)
                    && candidates.ValueKind == JsonValueKind.Array
                    && candidates.GetArrayLength() > 0)
                {
                    details.VideoThumbnail = candidates[0].GetProperty("url").GetString();
                }
            }

            details.TrackName = ExtractTrackName(item);
            if (!skipMediaExtraction)
            {
                details.Media = ExtractMedia(root);
                if (details.Media.Count == 0)
                {
                    Console.WriteLine($"Media Not Found: {root.ToString()}");
                }
            }
            details.json = root.ToString();
            //Console.WriteLine(root.ToString());

            return details;
        }

        public static string ExtractTrackName(JsonElement root)
        {
            if (!root.TryGetProperty("clips_metadata", out var clipsMetadata) || clipsMetadata.ValueKind != JsonValueKind.Object)
            {
                return "vxinstagram";
            }

            if (clipsMetadata.TryGetProperty("music_info", out var musicInfo)
                && musicInfo.ValueKind == JsonValueKind.Object)
            {
                if (musicInfo.TryGetProperty("music_asset_info", out var asset)
                    && asset.ValueKind == JsonValueKind.Object)
                {
                    string artist = asset.TryGetProperty("display_artist", out var a) ? a.GetString() ?? "" : "";
                    string title = asset.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";

                    if (!string.IsNullOrWhiteSpace(artist) || !string.IsNullOrWhiteSpace(title))
                        return $"{artist} - {title}";
                }
            }

            if (clipsMetadata.TryGetProperty("original_sound_info", out var original)
                && original.ValueKind == JsonValueKind.Object)
            {
                if (original.TryGetProperty("original_audio_title", out var oTitle))
                {
                    var name = oTitle.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }

            return "vxinstagram";
        }

        public static List<Media> ExtractMedia(JsonElement root)
        {
            var result = new List<Media>();

            if (!root.TryGetProperty("data", out var data))
                return result;

            if (!data.TryGetProperty("xdt_api__v1__media__shortcode__web_info", out var webInfo))
                return result;

            if (!webInfo.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
                return result;

            var item = items[0];

            // ------------------------------------------
            // CASE 1: CAROUSEL
            // ------------------------------------------
            if (item.TryGetProperty("carousel_media", out var carousel) &&
                carousel.ValueKind == JsonValueKind.Array &&
                carousel.GetArrayLength() > 0)
            {
                int index = 0;
                foreach (var mediaItem in carousel.EnumerateArray())
                {
                    var media = ExtractSingleMedia(mediaItem, index++);
                    if (media != null)
                        result.Add(media);
                }

                return result;
            }

            // ------------------------------------------
            // CASE 2: SINGLE IMAGE / REEL / VIDEO
            // ------------------------------------------
            var single = ExtractSingleMedia(item, 0);
            if (single != null)
                result.Add(single);

            return result;
        }


        private static Media? ExtractSingleMedia(JsonElement item, int index)
        {
            int mediaType = item.TryGetProperty("media_type", out var mt) ? mt.GetInt32() : 1;

            // ==========================
            // VIDEO
            // ==========================
            if (mediaType == 2)
            {
                string videoUrl = "";
                string thumbnail = "";

                if (item.TryGetProperty("video_versions", out var videos) &&
                    videos.ValueKind == JsonValueKind.Array &&
                    videos.GetArrayLength() > 0)
                {
                    // Best video: pick first or highest resolution
                    var best = videos[0];
                    if (best.TryGetProperty("url", out var vu))
                        videoUrl = vu.GetString() ?? "";
                }

                // thumbnail from image_versions2
                if (item.TryGetProperty("image_versions2", out var img) &&
                    img.TryGetProperty("candidates", out var candidates) &&
                    candidates.ValueKind == JsonValueKind.Array &&
                    candidates.GetArrayLength() > 0)
                {
                    thumbnail = candidates[0].GetProperty("url").GetString() ?? "";
                }

                if (string.IsNullOrEmpty(videoUrl))
                    return null;

                return new Media
                {
                    MediaType = "video",
                    RapidSaveUrl = videoUrl,
                    ThumbnailUrl = thumbnail
                };
            }

            // ==========================
            // IMAGE
            // ==========================
            if (item.TryGetProperty("image_versions2", out var img2) &&
                img2.TryGetProperty("candidates", out var imgs) &&
                imgs.ValueKind == JsonValueKind.Array &&
                imgs.GetArrayLength() > 0)
            {
                string url = imgs[0].GetProperty("url").GetString() ?? "";

                return new Media
                {
                    MediaType = "image",
                    RapidSaveUrl = url,
                    ThumbnailUrl = url
                };
            }

            return null;
        }



        public async Task<string> FetchInstagramPostAsync(string shortcode)
        {
            // Pick 3 random sessions
            var sessions = Db.Sessions
                .Where(e => e.ExpiresOn > DateTime.UtcNow)
                .OrderBy(r => Guid.NewGuid())
                .Take(3)
                .ToList();

            if (sessions.Count == 0)
                return string.Empty;

            // Create 3 proxy clients
            var clients = new[]
            {
                CreateProxyClient(),
                CreateProxyClient(),
                CreateProxyClient(),
            };

            var cts = new CancellationTokenSource();

            // Run all fetch tasks
            var tasks = new List<Task<(bool success, string json, Session session)>>();

            for (int i = 0; i < sessions.Count; i++)
            {
                tasks.Add(TryFetchGraphQLAsync(shortcode, sessions[i].ID, clients[i], cts.Token));
            }

            while (tasks.Count > 0)
            {
                var finished = await Task.WhenAny(tasks);
                tasks.Remove(finished);

                var result = await finished;

                if (result.success)
                {
                    // Cancel the rest
                    cts.Cancel();
                    return result.json;
                }
            }
            Console.WriteLine($"Failed All: {tasks.Select(e => e.Result).ToString()}");
            return string.Empty; // all failed
        }

        private HttpClient CreateProxyClient()
        {
            var handler = new HttpClientHandler
            {
                Proxy = Constants.ProxyInformation != null ? new WebProxy(Constants.ProxyInformation.Host)
                {
                    Credentials = new NetworkCredential(Constants.ProxyInformation.Username, (Constants.ProxyInformation.Password))
                } : null,
                UseProxy = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            var client = new HttpClient(handler);

            client.DefaultRequestHeaders.Add("Accept", "*/*");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.Add("Origin", "https://www.instagram.com");
            client.DefaultRequestHeaders.Add("Priority", "u=1, i");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36"
            );

            client.DefaultRequestHeaders.Add("X-Asbd-Id", "129477");
            client.DefaultRequestHeaders.Add("X-Bloks-Version-Id", "e2004666934296f275a5c6b2c9477b63c80977c7cc0fd4b9867cb37e36092b68");
            client.DefaultRequestHeaders.Add("X-Fb-Friendly-Name", "PolarisPostActionLoadPostQueryQuery");
            client.DefaultRequestHeaders.Add("X-Ig-App-Id", "936619743392459");

            return client;
        }

        private async Task<(bool success, string json, Session session)> TryFetchGraphQLAsync(string shortcode, int sessionId, HttpClient client, CancellationToken ct)
        {

            using var db = _dbContextFactory.CreateDbContext();

            var session = db.Sessions.Find(sessionId);
            if (session == null) return (false, "", session);

            try
            {


                var request = new HttpRequestMessage(HttpMethod.Post, "https://www.instagram.com/graphql/query/");

                request.Headers.Add("Cookie", session.CSRFToken);
                request.Headers.Add("x-root-field-name", "xdt_api__v1__web__accounts__get_encrypted_credentials");
                request.Headers.Add("X-Fb-Lsd", "lvKgZqkPPmLKqUfKIBiMFa");
                request.Headers.Add("X-Csrftoken", session.CSRFToken.Split("=").Last());
                request.Headers.Add("Referer", $"https://www.instagram.com/p/{shortcode}");

                var body = Common.GetGraphQLScrapingBody(shortcode);

                request.Content = new FormUrlEncodedContent(body);

                var response = await client.SendAsync(request, ct);
                var content = await response.Content.ReadAsStringAsync(ct);

                // Reject HTML (challenge, login, 429, block)
                if (content.StartsWith("<!DOCTYPE html") || content.Contains("not-logged-in") || (content.Contains("\"status\":\"fail\"") && !content.Contains("Please wait a few minutes before you try again.")))
                {
                    Console.WriteLine($"Expired: {content}");
                    session.ExpireSession();
                    db.SaveChanges();
                    return (false, "", session);
                }

                return (true, content, session);
            }
            catch (TaskCanceledException)
            {
                return (false, "", session);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Expired With Exception: {e.ToString()}");

                session.ExpireSession();
                db.SaveChanges();
                return (false, "", session);
            }
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
        public string? Username { get; set; }
        public string? Name { get; set; }
        public string? Avatar { get; set; }

        public int Likes { get; set; }
        public int Comments { get; set; }
        public string? Description { get; set; }

        public bool IsVideo { get; set; }
        public int? VideoWidth { get; set; }
        public int? VideoHeight { get; set; }
        public string? VideoThumbnail { get; set; }

        public string? TrackName { get; set; }

        public List<Media> Media { get; set; } = [];

        public string json { get; set; } = string.Empty;
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
        public string uri { get; set; }
        public DateTime created_at { get; set; }
        public bool locked { get; set; }
        public bool bot { get; set; }
        public bool discoverable { get; set; }
        public bool indexable { get; set; }
        public bool group { get; set; }
        public string avatar { get; set; }
        public string avatar_static { get; set; }

        // Instagram/tiktok-style accounts may not have headers → keep object

        // Present in your Instagram JSON
        public int followers_count { get; set; }
        public int following_count { get; set; }

        public bool hide_collections { get; set; }
        public bool noindex { get; set; }

        public List<object> emojis { get; set; } = new();
        public List<object> roles { get; set; } = new();
        public List<object> fields { get; set; } = new();
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
        public double aspect { get; set; }

        public int width { get; set; }
        public int height { get; set; }

        public string size { get; set; } // "720x1280"
    }


    public class ActivityPubModel
    {
        public string id { get; set; }
        public string url { get; set; }
        public string uri { get; set; }

        public DateTime created_at { get; set; }
        public DateTime? edited_at { get; set; }

        public string content { get; set; }
        public string spoiler_text { get; set; } = string.Empty;
        public string language { get; set; }

        public string visibility { get; set; }

        public Application application { get; set; }
        public List<MediaAttachment> media_attachments { get; set; } = new();

        public Account account { get; set; }

        public string in_reply_to_id { get; set; }
        public string in_reply_to_account_id { get; set; }

        public List<object> mentions { get; set; } = new();
        public List<object> tags { get; set; } = new();
        public List<object> emojis { get; set; } = new();

        public object card { get; set; }
        public object poll { get; set; }

        public object reblog { get; set; }
    }




    public static class Base64Url
    {
        public static string Encode(byte[] bytes)
        {
            var base64 = Convert.ToBase64String(bytes);     // Standard Base64
            return base64
                .Replace("+", "-")                         // URL safe replacements
                .Replace("/", "_")
                .TrimEnd('=');                              // Remove padding
        }

        public static byte[] Decode(string base64Url)
        {
            string base64 = base64Url
                .Replace("-", "+")                         // Undo URL-safe replacements
                .Replace("_", "/");

            // Add removed padding back
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }

            return Convert.FromBase64String(base64);
        }
    }
}

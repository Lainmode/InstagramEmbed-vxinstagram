using InstagramEmbedForDiscord.DAL;
using InstagramEmbedForDiscord.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json.Serialization;

namespace InstagramEmbedForDiscord.Controllers
{
    [Route("{type}/{id}/{index?}")]
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
                var dbContext = new KitContext();

                var log = ActionLog.CreateActionLog(httpContext);

                dbContext.ActionLogs.Add(log);
                dbContext.SaveChanges();
            });

        }

        public async Task<IActionResult> Index(string type, string id, string? index)
        {
            //try
            //{
                string link = "https://instagram.com/p/" + id;

                string contentUrl = string.Empty;
                string thumbnailUrl = string.Empty;

                using (HttpClient client = new HttpClient())
                {
                    HttpResponseMessage snapSaveResponse = await client.GetAsync("http://alsauce.com:3200/igdl?url=" + link + "/");
                    string snapSaveResponseString = await snapSaveResponse.Content.ReadAsStringAsync();
                    InstagramResponse instagramResponse = JsonConvert.DeserializeObject<InstagramResponse>(snapSaveResponseString)!;

                    var media = instagramResponse.url?.data?.media;
                    if (media==null || media.Count<=0)
                        return BadRequest("No media found.");

                if (media.Count == 0) return await ProcessSingleItem(media.First(), client, link);
                else if (index != null && int.TryParse(index, out int intIndex) && media.Count>=intIndex+1) return await ProcessSingleItem(media[intIndex], client, link);
                else return ProcessMultipleItems(media, link, id);
            }
            //}

            //catch (Exception e)
            //{
            //    Console.WriteLine(e.Message);
            //    return View("Error");
            //}
        }

        [Route("/")]
        public IActionResult HomePage()
        {
            return View();
        }

        private async Task<IActionResult> ProcessSingleItem(InstagramMedia media, HttpClient client, string originalLink) 
        {
            var contentUrl = media.url;
            var thumbnailUrl = media.thumbnail;

            bool isPhoto = media.type == "image";

            if (isPhoto)
            {
                var imageBytes = await client.GetByteArrayAsync(contentUrl);

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

        private IActionResult ProcessMultipleItems(List<InstagramMedia> media, string originalLink, string id)
        {
            List<SKBitmap> bitmaps = GetMultipleImages(media);

            if (bitmaps.Count == 0)
                return BadRequest("No images to process.");

            int columns = 2;
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

            // Create final canvas
            using var finalBitmap = new SKBitmap(canvasWidth, canvasHeight);
            using var canvas = new SKCanvas(finalBitmap);
            canvas.Clear(SKColors.Black); // Optional background

            int yOffset = 0;

            for (int i = 0; i < bitmapRows.Count; i++)
            {
                var row = bitmapRows[i];
                int xOffset = 0;
                int rowHeight = rowHeights[i];

                foreach (var img in row)
                {
                    float offsetY = yOffset + (rowHeight - img.Height) / 2f;
                    canvas.DrawBitmap(img, xOffset, offsetY);
                    xOffset += img.Width;
                }

                yOffset += rowHeight;
            }

            canvas.Flush();

            // Convert to JPEG
            using var image = SKImage.FromBitmap(finalBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);

            // Ensure folder exists
            string folderPath = Path.Combine(_env.WebRootPath, "generated");
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            // Save image
            string fileName = $"{SanitizeFileName(id)}.png";
            string savePath = Path.Combine(folderPath, fileName);

            if (!Path.Exists(savePath))
            {
                using (var stream = System.IO.File.OpenWrite(savePath))
                {
                    data.SaveTo(stream);
                }
            }

            // Build absolute URL
            string contentUrl = $"{Request.Scheme}://{Request.Host}/generated/{fileName}";

            ViewBag.IsPhoto = true;
            ViewBag.Files = media;
            return View(new string[] { contentUrl, null, originalLink });
        }


        private List<SKBitmap> GetMultipleImages(List<InstagramMedia> media)
        {
            List<Task> tasks = [];
            List<SKBitmap?> bitmaps = [];
            foreach(var item in media)
            {
                bool isVideo = item.type == "video";
                string image = isVideo ? item.thumbnail : item.url;
                tasks.Add(Task.Run(async () => bitmaps.Add(await LoadJpegFromUrlAsync(image, isVideo))));
            }
            Task t = Task.WhenAll(tasks);
            t.Wait();

            return bitmaps.Where(e => e != null).ToList() as List<SKBitmap>;
        }

        private async Task<SKBitmap?> LoadJpegFromUrlAsync(string imageUrl, bool isVideo=false)
        {
            using (HttpClient client = new HttpClient())
            {
                try
                {
                    byte[] imageBytes = await client.GetByteArrayAsync(imageUrl);

                    using (var stream = new SKMemoryStream(imageBytes))
                    {
                        var bitmap = SKBitmap.Decode(stream);
                        if(isVideo)
                        {
                            SKCanvas canvas = new SKCanvas(bitmap);
                            SKPaint paint = new SKPaint();

                            float x = 10;
                            float y = bitmap.Height - 10;

                            canvas.DrawText("Video", new SKPoint(x,y), new SKTextAlign(), new SKFont(typeface: SKTypeface.FromFamilyName("Roboto")), paint);

                            canvas.Flush();
                            canvas.Dispose();

                        }
                        return bitmap;
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

}

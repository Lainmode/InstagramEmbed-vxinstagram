using InstagramEmbedForDiscord.DAL;
using InstagramEmbedForDiscord.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json.Serialization;

namespace InstagramEmbedForDiscord.Controllers
{
    [Route("{type}/{id}")]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
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

        public async Task<IActionResult> Index(string type, string id)
        {
            try
            {
                string link = "https://instagram.com/p/" + id;

                string contentUrl = string.Empty;
                string thumbnailUrl = string.Empty;

                using (HttpClient client = new HttpClient())
                {
                    var snapSaveResponse = await client.GetAsync("http://alsauce.com:3200/igdl?url=" + link + "/");
                    var snapSaveResponseString = await snapSaveResponse.Content.ReadAsStringAsync();
                    var instagramResponse = JsonConvert.DeserializeObject<InstagramResponse>(snapSaveResponseString)!;

                    var media = instagramResponse.url?.data?.media?.FirstOrDefault();
                    if (media == null)
                        return BadRequest("No media found.");

                    contentUrl = media.url;
                    thumbnailUrl = media.thumbnail;

                    var contentDispositionHeadRequest = new HttpRequestMessage(HttpMethod.Get, contentUrl);
                    var contentDispositionHeadResponse = await client.SendAsync(contentDispositionHeadRequest);

                    //var contentDisposition = contentDispositionHeadResponse.Content.Headers.ContentDisposition;
                    bool isPhoto = media.type == "image";
                                   //||
                                   //(contentDisposition != null &&
                                   //contentDisposition.FileName != null &&
                                   //!contentDisposition.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));

                    if (isPhoto)
                    {
                        var imageBytes = await client.GetByteArrayAsync(contentUrl);
                        return File(imageBytes, "image/jpeg");
                    }

                }


                string[] data = { contentUrl, thumbnailUrl, link };
                ViewBag.IsPhoto = false;
                return View(data);
            }

            catch (Exception e)
            {
                return View("Error");
            }
        }

        [Route("/")]
        public IActionResult HomePage()
        {
            return View();
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

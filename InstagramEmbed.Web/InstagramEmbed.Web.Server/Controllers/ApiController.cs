using Microsoft.AspNetCore.Mvc;

namespace InstagramEmbed.Web.Server.Controllers
{
    public class ApiController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}

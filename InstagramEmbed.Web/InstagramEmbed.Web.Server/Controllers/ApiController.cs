using InstagramEmbed.DataAccess;
using InstagramEmbed.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace InstagramEmbed.Web.Server.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class ApiController : Controller
    {
        private readonly InstagramContext Db;
        public ApiController(InstagramContext _db)
        {
            Db = _db;
        }

        [HttpGet]
        public List<Session> GetSessions()
        {
            DateTime now = DateTime.Now;
            return [.. Db.Sessions.Where(e => e.ExpiresOn > now)];
        }
    }
}

using InstagramEmbed.DataAccess;
using InstagramEmbed.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace InstagramEmbed.Web.Server.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class AdminController : Controller
    {
        private readonly InstagramContext Db;
        public AdminController(InstagramContext _db)
        {
            Db = _db;
        }

        [HttpGet]
        public ApiResponse<List<Session>> GetSessions()
        {
            DateTime now = DateTime.Now;
            return ApiResponse<List<Session>>.Ok(Db.Sessions.Where(e => e.ExpiresOn > now).ToList());
        }
    }

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }

        public ApiResponse() { }

        public ApiResponse(bool success, string? message = null, T? data = default)
        {
            Success = success;
            Message = message;
            Data = data;
        }

        public static ApiResponse<T> Ok(T data, string? message = null) =>
            new ApiResponse<T>(true, message, data);

        public static ApiResponse<T> Fail(string message) =>
            new ApiResponse<T>(false, message);
    }

}

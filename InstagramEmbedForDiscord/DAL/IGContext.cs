namespace InstagramEmbedForDiscord.DAL
{
    using InstagramEmbedForDiscord.Models.Entities;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.JSInterop;
    using System.Linq.Expressions;

    public class IGContext : DbContext
    {
        private readonly string connectionString = "Server=localhost;Database=vxinstagram;Trusted_Connection=True;TrustServerCertificate=True";
        public DbSet<ActionLog> ActionLogs { get; set; }
        public DbSet<Post> Posts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Use your connection string here

            optionsBuilder
                .UseLazyLoadingProxies() // Enable lazy loading
                .UseSqlServer(connectionString);

            base.OnConfiguring(optionsBuilder);
        }



    }


    public class ActionLog
    {
        public int ID { get; set; }
        public DateTime Date { get; set; }
        public string? IP { get; set; }
        public string? Type { get; set; }
        public string? Url { get; set; }
        public string? UserAgent { get; set; }

        public const string TYPE_GET = "GET";
        public const string TYPE_POST = "POST";
        public const string TYPE_LOGIN_SUCCESS = "LOGIN_SUCCESS";
        public const string TYPE_LOGIN_FAILURE = "LOGIN_FAILURE";
        public const string TYPE_LOGOUT = "LOGOUT";

        public static ActionLog CreateActionLog(HttpContext context)
        {
            return new ActionLog
            {
                Date = DateTime.UtcNow,
                IP = GetClientIpAddress(context),
                Type = context.Request.Method,
                Url = context.Request.Path + context.Request.QueryString,

                UserAgent = context.Request.Headers["User-Agent"].ToString()
            };
        }

        public static string GetClientIpAddress(HttpContext context)
        {
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
                return forwardedFor.Split(',')[0];

            return context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        }
    }
}

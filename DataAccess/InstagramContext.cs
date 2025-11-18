using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace InstagramEmbed.DataAccess
{
    internal class InstagramContext : DbContext
    {
        private readonly string connectionString = "YOUR_CONNECTION_STRING";
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
                // .UseLazyLoadingProxies() // Enable lazy loading
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

        public static ActionLog CreateActionLog(string method, string queryString, string userAgent, string forwardedFor, string? ipAddress)
        {
            return new ActionLog
            {
                Date = DateTime.UtcNow,
                IP = GetClientIpAddress(forwardedFor, ipAddress),
                Type = method,
                Url = queryString,
                UserAgent = userAgent
            };
        }

        public static string GetClientIpAddress(string forwardedFor, string? ipAddress)
        {
            return ipAddress ?? "127.0.0.1";
        }
    }
}


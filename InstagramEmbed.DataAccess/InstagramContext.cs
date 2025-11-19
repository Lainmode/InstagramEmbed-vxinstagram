using InstagramEmbed.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace InstagramEmbed.DataAccess
{
    public class InstagramContext : DbContext
    {
        private readonly string connectionString = "Server=localhost;Database=InstagramEmbed.Web;Trusted_Connection=True;MultipleActiveResultSets=true";
        public DbSet<ActionLog> ActionLogs { get; set; }
        public DbSet<Post> Posts { get; set; }
        public DbSet<Media> Media { get; set; }
        public DbSet<Session> Sessions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Use your connection string here

            optionsBuilder
                // .UseLazyLoadingProxies() // Enable lazy loading
                .UseSqlServer(connectionString).UseLazyLoadingProxies();

            base.OnConfiguring(optionsBuilder);
        }
    }
}


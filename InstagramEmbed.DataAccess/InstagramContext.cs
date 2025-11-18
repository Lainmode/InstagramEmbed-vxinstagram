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
}


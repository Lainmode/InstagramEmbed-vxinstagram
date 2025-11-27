using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace InstagramEmbed.Domain.Entities
{
    public class User : Loggable
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AnonymousName { get; set; } = string.Empty;
        public string? Photo { get; set; }
        public string? PhotoUrl { get; set; }

        public string? ProfileColor { get; set; }

        public string? Bio { get; set; } = string.Empty;

        public UserType UserType { get; set; }
    }

    public enum UserType
    {
        None = 0,
        Member = 1,
        Moderator = 2,
        SystemAdmin = 3,
    }
}

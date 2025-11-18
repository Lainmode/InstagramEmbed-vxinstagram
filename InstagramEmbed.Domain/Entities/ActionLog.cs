using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstagramEmbed.Domain.Entities
{
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

        public static ActionLog CreateActionLog(string method, string queryString, string userAgent, string? ipAddress)
        {
            return new ActionLog
            {
                Date = DateTime.UtcNow,
                IP = ipAddress ?? "127.0.0.1",
                Type = method,
                Url = queryString,
                UserAgent = userAgent
            };
        }
    }
}

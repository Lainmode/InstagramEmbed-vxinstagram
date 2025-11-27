using InstagramEmbed.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InstagramEmbed.Domain
{
    public class Loggable
    {
        public int ID { get; set; }
        public string HashID { get; set; } = Guid.NewGuid().ToString();
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }

        public bool IsDeleted { get; set; }

        public virtual void Fill(User user)
        {
            DateTime now = DateTime.Now;

            if (string.IsNullOrEmpty(this.CreatedBy))
            {
                this.CreatedBy = user.Email;
                this.CreatedAt = now;
            }

            this.UpdatedBy = user.Email;
            this.UpdatedAt = now;
        }

        public virtual void Fill(string identity)
        {
            DateTime now = DateTime.Now;

            if (string.IsNullOrEmpty(this.CreatedBy))
            {
                this.CreatedBy = identity;
                this.CreatedAt = now;
            }

            this.UpdatedBy = identity;
            this.UpdatedAt = now;
        }
    }
}

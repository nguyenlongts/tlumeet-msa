using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AuthService.Domain.Models
{
    public class RefreshToken
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        public Guid Token { get; set; } = Guid.NewGuid();

        public DateTime ExpiredAt{ get; set;}

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? RevokeAt { get; set; }
        public User User { get; set; }
    }
}

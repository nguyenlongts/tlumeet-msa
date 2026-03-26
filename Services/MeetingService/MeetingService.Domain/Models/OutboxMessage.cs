using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetingService.Domain.Models
{
    public class OutboxMessage
    {
        public int Id { get; set; }
        public string EventType { get; set; }
        public string Payload { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? OccuredAt { get; set; }
        public string? ErrorMessage { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetingService.Domain.Enums
{

    public enum MeetingStatus
    {
        Scheduled = 0,
        WaitingForHost = 1,
        Live = 2,
        Ended = 3
    }
}

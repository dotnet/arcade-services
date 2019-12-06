using System;
using System.Collections.Generic;
using System.Text;

namespace Maestro.Data.Models
{
    public class GoalTime
    {
        public int ChannelId { get; set; }
        public Channel Channel { get; set; }
        public int DefinitionId { get; set; }
        public int Minutes { get; set; }
    }
}

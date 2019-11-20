using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Maestro.Data.Migrations;
using Maestro.Data.Models;

namespace Maestro.Web.Api.v2019_01_16.Models
{
    public class Goal
    {
        public Goal([NotNull] Data.Models.GoalTime other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            DefinitionId = other.DefinitionId;
            Minutes = other.Minutes;
            ChannelId = other.ChannelId;
        }

        public int DefinitionId { get; set; }
        public int ChannelId { get; set; }
        public int Minutes { get; set; }
        public class GoalData
        {
            [Required]
            public string ChannelName { get; set; }
            [Required]
            public int DefinitionId { get; set; }
            [Required]
            public int Minutes { get; set; }
        }
    }
}

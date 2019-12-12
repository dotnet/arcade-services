// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
            Channel = other.Channel == null ? null : new v2018_07_16.Models.Channel(other.Channel);
        }

        public int DefinitionId { get; set; }
        public v2018_07_16.Models.Channel Channel { get; set; }
        public int Minutes { get; set; }
        public class GoalRequestJson
        {
            [Required]
            public int Minutes { get; set; }
        }
    }
}

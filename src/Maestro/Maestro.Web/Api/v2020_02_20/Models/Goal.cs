// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using JetBrains.Annotations;
using System;
using System.ComponentModel.DataAnnotations;

namespace Maestro.Web.Api.v2020_02_20.Models
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
            Channel = other.Channel == null ? null : new Channel(other.Channel);
        }

        public int DefinitionId { get; set; }
        public Channel Channel { get; set; }
        public int Minutes { get; set; }
        public class GoalRequestJson
        {
            [Required]
            public int Minutes { get; set; }
        }
    }
}

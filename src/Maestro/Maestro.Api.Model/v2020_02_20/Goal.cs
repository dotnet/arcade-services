// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel.DataAnnotations;

namespace Maestro.Api.Model.v2020_02_20;

public class Goal
{
    public Goal(Data.Models.GoalTime other)
    {
        ArgumentNullException.ThrowIfNull(other);

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

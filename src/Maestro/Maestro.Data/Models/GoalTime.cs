// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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

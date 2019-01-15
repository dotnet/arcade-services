// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.EntityFrameworkCore;

namespace Maestro.Data.Models
{
    [Owned]
    public class AzureDevOpsBuildInfo
    {
        public int BuildId { get; set; }

        public int BuildDefinitionId { get; set; }

        public string Account { get; set; }

        public string Project { get; set; }

        public string BuildNumber { get; set; }

        public string Repository { get; set; }

        public string Branch { get; set; }
    }
}

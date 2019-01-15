// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.EntityFrameworkCore;

namespace Maestro.Data.Models
{
    [Owned]
    public class GitHubBuildInfo
    {
        public string Repository { get; set; }

        public string Branch { get; set; }

        public string Commit { get; set; }
    }
}

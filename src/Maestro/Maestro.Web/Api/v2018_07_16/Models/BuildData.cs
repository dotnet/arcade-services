// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Maestro.Data.Models;

namespace Maestro.Web.Api.v2018_07_16.Models
{
    public class BuildData
    {
        [Required]
        public string Repository { get; set; }

        public string Branch { get; set; }

        [Required]
        public string Commit { get; set; }

        [Required]
        public string BuildNumber { get; set; }

        public List<AssetData> Assets { get; set; }

        public List<int> Dependencies { get; set; }

        public Data.Models.Build ToDb()
        {
            var isGitHubInfo = Repository.IndexOf("github", StringComparison.OrdinalIgnoreCase) >= 0;

            return new Data.Models.Build
            {
                Commit = Commit,
                AzureDevOpsBuildNumber = BuildNumber,
                Assets = Assets.Select(a => a.ToDb()).ToList(),

                GitHubRepository = isGitHubInfo ? Repository : null,
                GitHubBranch = isGitHubInfo ? Branch : null,

                AzureDevOpsRepository = isGitHubInfo ? null : Repository,
                AzureDevOpsBranch = isGitHubInfo ? null : Branch,
            };
        }
    }
}

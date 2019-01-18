// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Maestro.Web.Api.v2018_07_16.Models;
using Microsoft.AspNetCore.ApiVersioning;

namespace Maestro.Web.Api.v2019_01_16.Models
{
    public class BuildData : Maestro.Web.Api.v2018_07_16.Models.BuildData
    {
        public new string Repository { get; set; }

        public new string Branch { get; set; }

        [Required]
        public int AzureDevOpsBuildId { get; set; }

        [Required]
        public int AzureDevOpsBuildDefinitionId { get; set; }

        [Required]
        public string AzureDevOpsAccount { get; set; }

        [Required]
        public string AzureDevOpsProject { get; set; }

        [Required]
        public string AzureDevOpsBuildNumber { get; set; }

        [Required]
        public string AzureDevOpsRepository { get; set; }

        [Required]
        public string AzureDevOpsBranch { get; set; }

        public string GitHubRepository { get; set; }

        public string GitHubBranch { get; set; }

        public new Data.Models.Build ToDb()
        {
            return new Data.Models.Build
            {
                GitHubRepository = GitHubRepository,
                GitHubBranch = GitHubBranch,
                AzureDevOpsBuildId = AzureDevOpsBuildId,
                AzureDevOpsBuildDefinitionId = AzureDevOpsBuildDefinitionId,
                AzureDevOpsAccount = AzureDevOpsAccount,
                AzureDevOpsProject = AzureDevOpsProject,
                AzureDevOpsBuildNumber = AzureDevOpsBuildNumber,
                AzureDevOpsRepository = AzureDevOpsRepository,
                AzureDevOpsBranch = AzureDevOpsBranch,
                Commit = Commit,
                Assets = Assets.Select(a => a.ToDb()).ToList()
            };
        }
    }
}

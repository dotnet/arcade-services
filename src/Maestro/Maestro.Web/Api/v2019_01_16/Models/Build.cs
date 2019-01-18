// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using JetBrains.Annotations;

namespace Maestro.Web.Api.v2019_01_16.Models
{
    public class Build : Maestro.Web.Api.v2018_07_16.Models.Build
    {
        public Build([NotNull] Data.Models.Build other) : base(other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            GitHubRepository = other.GitHubRepository;
            GitHubBranch = other.GitHubBranch;
            AzureDevOpsBuildId = other.AzureDevOpsBuildId;
            AzureDevOpsBuildDefinitionId = other.AzureDevOpsBuildDefinitionId;
            AzureDevOpsAccount = other.AzureDevOpsAccount;
            AzureDevOpsProject = other.AzureDevOpsProject;
            AzureDevOpsBuildNumber = other.AzureDevOpsBuildNumber;
            AzureDevOpsRepository = other.AzureDevOpsRepository;
            AzureDevOpsBranch = other.AzureDevOpsBranch;
        }

        public int AzureDevOpsBuildId { get; set; }

        public int AzureDevOpsBuildDefinitionId { get; set; }

        public string AzureDevOpsAccount { get; set; }

        public string AzureDevOpsProject { get; set; }

        public string AzureDevOpsBuildNumber { get; set; }

        public string AzureDevOpsRepository { get; set; }

        public string AzureDevOpsBranch { get; set; }

        public string AzureDevOpsCommit => Commit;

        public string GitHubRepository { get; set; }

        public string GitHubBranch { get; set; }

        public string GitHubCommit => Commit;
    }
}

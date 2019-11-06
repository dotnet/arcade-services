// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Maestro.Web.Api.v2019_01_16.Models;

namespace Maestro.Web.Api.v2019_01_16.Models
{
    public class Build
    {
        public Build([NotNull] Data.Models.Build other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            Id = other.Id;
            Commit = other.Commit;
            GitHubRepository = other.GitHubRepository;
            GitHubBranch = other.GitHubBranch;
            PublishUsingPipelines = other.PublishUsingPipelines;
            AzureDevOpsBuildId = other.AzureDevOpsBuildId;
            AzureDevOpsBuildDefinitionId = other.AzureDevOpsBuildDefinitionId;
            AzureDevOpsAccount = other.AzureDevOpsAccount;
            AzureDevOpsProject = other.AzureDevOpsProject;
            AzureDevOpsBuildNumber = other.AzureDevOpsBuildNumber;
            AzureDevOpsRepository = other.AzureDevOpsRepository;
            AzureDevOpsBranch = other.AzureDevOpsBranch;
            DateProduced = other.DateProduced;
            Channels = other.BuildChannels?.Select(bc => bc.Channel)
                .Where(c => c != null)
                .Select(c => new v2018_07_16.Models.Channel(c))
                .ToList();
            Assets = other.Assets?.Select(a => new v2018_07_16.Models.Asset(a)).ToList();
            Dependencies = other.DependentBuildIds?.Select(d => new BuildRef(d.DependentBuildId, d.IsProduct, d.TimeToInclusionInMinutes)).ToList();
            Staleness = other.Staleness;
            Released = other.Released;
        }

        public int Id { get; }

        public string Commit { get; }

        public int? AzureDevOpsBuildId { get; set; }

        public int? AzureDevOpsBuildDefinitionId { get; set; }

        public string AzureDevOpsAccount { get; set; }

        public string AzureDevOpsProject { get; set; }

        public string AzureDevOpsBuildNumber { get; set; }

        public string AzureDevOpsRepository { get; set; }

        public string AzureDevOpsBranch { get; set; }

        public string GitHubRepository { get; set; }

        public string GitHubBranch { get; set; }

        public bool PublishUsingPipelines { get; set; }

        public DateTimeOffset DateProduced { get; }

        public List<v2018_07_16.Models.Channel> Channels { get; }

        public List<v2018_07_16.Models.Asset> Assets { get; }

        public List<BuildRef> Dependencies { get; }

        public int Staleness { get; }

        public bool Released { get; }
    }
}

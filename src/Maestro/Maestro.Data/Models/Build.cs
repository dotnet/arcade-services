// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using EntityFrameworkCore.Triggers;
using Microsoft.DotNet.DarcLib;

namespace Maestro.Data.Models
{
    public class Build
    {
        private string _azureDevOpsRepository;
        private string _gitHubRepository;
        private string _azureDevOpsBranch;
        private string _githubBranch;

        static Build()
        {
            Triggers<Build>.Inserted += entry =>
            {
                Build build = entry.Entity;
                var context = (BuildAssetRegistryContext) entry.Context;

                context.BuildChannels.AddRange((
                    from dc in context.DefaultChannels
                    where (dc.Enabled)
                    where (dc.Repository == build.GitHubRepository || dc.Repository == build.AzureDevOpsRepository)
                    where (dc.Branch == build.GitHubBranch || dc.Branch == build.AzureDevOpsBranch)
                    select new BuildChannel
                    {
                        Channel = dc.Channel,
                        Build = build,
                        DateTimeAdded = DateTimeOffset.UtcNow
                    }).Distinct());

                context.SaveChangesWithTriggers(b => context.SaveChanges(b));
            };
        }

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        
        public string Commit { get; set; }

        public int? AzureDevOpsBuildId { get; set; }

        public int? AzureDevOpsBuildDefinitionId { get; set; }

        public string AzureDevOpsAccount { get; set; }

        public string AzureDevOpsProject { get; set; }

        public string AzureDevOpsBuildNumber { get; set; }

        public string AzureDevOpsRepository
        {
            get
            {
                return AzureDevOpsClient.NormalizeUrl(_azureDevOpsRepository);
            }

            set
            {
                _azureDevOpsRepository = AzureDevOpsClient.NormalizeUrl(value);
            }
        }

        public string AzureDevOpsBranch
        {
            get
            {
                return AzureDevOpsClient.NormalizeUrl(_azureDevOpsBranch);
            }

            set
            {
                _azureDevOpsBranch = AzureDevOpsClient.NormalizeUrl(value);
            }
        }

        public string GitHubRepository
        {
            get
            {
                return AzureDevOpsClient.NormalizeUrl(_gitHubRepository);
            }

            set
            {
                _gitHubRepository = AzureDevOpsClient.NormalizeUrl(value);
            }
        }

        public string GitHubBranch
        {
            get
            {
                return IGitRepoExtension.NormalizeBranchName(_githubBranch);
            }
            set
            {
                _githubBranch = IGitRepoExtension.NormalizeBranchName(value);
            }
        }

        public bool PublishUsingPipelines { get; set; }

        public DateTimeOffset DateProduced { get; set; }

        public List<Asset> Assets { get; set; }

        public List<BuildChannel> BuildChannels { get; set; }

        /// <summary>
        /// If true, the build has been released to the public. This can be used to make decisions on whether certain
        /// builds should be included in future release drops.
        /// </summary>
        public bool Released { get; set; } = false;

        [NotMapped]
        public int Staleness { get; set; }

        [NotMapped]
        public List<BuildDependency> DependentBuildIds { get; set; }
    }

    public class BuildChannel
    {
        public int BuildId { get; set; }
        public Build Build { get; set; }
        public int ChannelId { get; set; }
        public Channel Channel { get; set; }
        public DateTimeOffset DateTimeAdded { get; set; }

        public override bool Equals(object obj)
        {
            return obj is BuildChannel buildChannel &&
                   BuildId == buildChannel.BuildId &&
                   ChannelId == buildChannel.ChannelId;
        }

        public override int GetHashCode()
        {
            return (BuildId, ChannelId).GetHashCode();
        }
    }

    public class BuildDependency
    {
        public int BuildId { get; set; }
        public Build Build { get; set; }
        public int DependentBuildId { get; set; }
        public Build DependentBuild { get; set; }
        public bool IsProduct { get; set; }

        // Time between when the dependent build was produced and when it was first added as a dependency
        // To this build's repository and branch
        public double TimeToInclusionInMinutes { get; set; }
    }

    public class GoalTime
    {
        public int ChannelId { get; set; }
        public int DefinitionId { get; set; }
        public int Minutes { get; set; }
    }
}

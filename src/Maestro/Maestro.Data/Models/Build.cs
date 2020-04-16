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
using Microsoft.DotNet.Services.Utility;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Maestro.Data.Models
{
    public class Build
    {
        private string _azureDevOpsRepository;
        private string _gitHubRepository;
        private string _azureDevOpsBranch;
        private string _githubBranch;

        // Used to fetch dynamic configurations from Azure App Configuration
        // These fields are set by Startup.cs at system initialization
        public static IConfiguration s_dynamicConfigs { get; set; }

        static Build()
        {
            Triggers<Build>.Inserted += entry =>
            {
                // The need to check for null here is because these properties are usually set in Startup.cs in Maestro.Web
                // however they aren't set for unit test cases but this class is used there.
                if (s_dynamicConfigs != null)
                {
                    bool.TryParse(s_dynamicConfigs["EnableAutoBuildPromotion"], out var autoBuildPromotion);

                    if (autoBuildPromotion)
                    {
                        Build build = entry.Entity;
                        var context = (BuildAssetRegistryContext)entry.Context;

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
                    }
                }
                else
                {
                    BuildAssetRegistryContext context = entry.Context as BuildAssetRegistryContext;
                    ILogger<Build> logger = context.GetService<ILogger<Build>>();
                    logger.LogInformation("Automatic build promotion is disabled because no App Configuration was available.");
                }
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
                return GitHelpers.NormalizeBranchName(_githubBranch);
            }
            set
            {
                _githubBranch = GitHelpers.NormalizeBranchName(value);
            }
        }

        public DateTimeOffset DateProduced { get; set; }

        public List<Asset> Assets { get; set; }

        public List<BuildChannel> BuildChannels { get; set; }

        /// <summary>
        /// If true, the build has been released to the public. This can be used to make decisions on whether certain
        /// builds should be included in future release drops.
        /// </summary>
        public bool Released { get; set; }

        /// <summary>
        /// If true, the build was marked with `$(DotNetFinalVersionKind) == 'release'`. Which means that it 
        /// produced assets with only (Major).(Minor).(Patch) version.
        /// More info is available here: https://github.com/dotnet/arcade/blob/master/Documentation/CorePackages/Versioning.md#build-kind
        /// </summary>
        public bool Stable { get; set; }

        /// <summary>
        /// This will store the list of dependency incoherencies of the current build.
        /// Basically a summary version of the dependencies in DarcLib->DependencyGraph->IncoherentDependencies
        /// </summary>
        public List<BuildIncoherence> Incoherencies { get; set; }

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
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.DotNet.Maestro.Client.Models;
using Newtonsoft.Json.Linq;

namespace Maestro.Data
{
    public class ModelTranslators
    {
        // TODO: Need null checks for all the properties everywhere - if something 5 layers down is null it's going to blow everything up right now

        public static Subscription DataToClientModel_Subscription(Maestro.Data.Models.Subscription other)
        {
            return new Subscription(other.Id, other.Enabled, other.SourceRepository, other.TargetRepository, other.TargetBranch)
            {
                Channel = DataToClientModel_Channel(other.Channel),
                LastAppliedBuild = DataToClientModel_Build(other.LastAppliedBuild),
                Policy = DataToClientModel_SubscriptionPolicy(other.PolicyObject),
            };
        }

        public static Maestro.Data.Models.Subscription ClientToDataModel_Subscription(Subscription other)
        {
            return new Maestro.Data.Models.Subscription()
            {
                Channel = ClientToDataModel_Channel(other.Channel),
                Enabled = other.Enabled,
                ChannelId = other.Channel.Id,
                Id = other.Id,
                LastAppliedBuild = ClientToDataModel_Build(other.LastAppliedBuild),
                LastAppliedBuildId = other.LastAppliedBuild?.Id??0,
                PolicyObject = ClientToDataModel_PolicyObject(other.Policy),
                // PolicyString = ??
                SourceRepository = other.SourceRepository,
                TargetBranch = other.TargetBranch,
                TargetRepository = other.TargetRepository
            };
        }

        public static Maestro.Data.Models.SubscriptionPolicy ClientToDataModel_PolicyObject(SubscriptionPolicy policy)
        {
            return new Maestro.Data.Models.SubscriptionPolicy()
            {
                Batchable = policy.Batchable,
                MergePolicies = ClientToDataModel_MergePolicies(policy.MergePolicies),
                UpdateFrequency = ClientToDataModel_UpdateFrequency(policy.UpdateFrequency)
            };
        }

        private static Maestro.Data.Models.UpdateFrequency ClientToDataModel_UpdateFrequency(UpdateFrequency updateFrequency)
        {
            return (Maestro.Data.Models.UpdateFrequency)updateFrequency;
        }

        private static List<Maestro.Data.Models.MergePolicyDefinition> ClientToDataModel_MergePolicies(IImmutableList<MergePolicy> mergePolicies)
        {
            List<Maestro.Data.Models.MergePolicyDefinition> policies = new List<Models.MergePolicyDefinition>();

            foreach (MergePolicy policy in mergePolicies)
            {
                policies.Add(ClientToDataModel_MergePolicy(policy));
            }

            return policies;
        }

        private static Maestro.Data.Models.MergePolicyDefinition ClientToDataModel_MergePolicy(MergePolicy other)
        {
            return new Maestro.Data.Models.MergePolicyDefinition()
            {
                Name = other.Name,
                Properties = new Dictionary<string, JToken>(other.Properties)
            };
        }

        public static Maestro.Data.Models.Build ClientToDataModel_Build(Build other)
        {
            // Note: BuildChannels doesn't have an equivalent in the client Build object
            return new Maestro.Data.Models.Build()
            {
                Assets = other.Assets?.Select(a => ClientToDataModel_Asset(a)).ToList(),
                AzureDevOpsAccount = other.AzureDevOpsAccount,
                AzureDevOpsBranch = other.AzureDevOpsBranch,
                AzureDevOpsBuildDefinitionId = other.AzureDevOpsBuildDefinitionId,
                AzureDevOpsBuildId = other.AzureDevOpsBuildId,
                AzureDevOpsBuildNumber = other.AzureDevOpsBuildNumber,
                AzureDevOpsProject = other.AzureDevOpsProject,
                AzureDevOpsRepository = other.AzureDevOpsRepository,
                Commit = other.Commit,
                DateProduced = other.DateProduced,
                DependentBuildIds = ClientToDataModel_DependentBuildIds(other.Dependencies),
                GitHubBranch = other.GitHubBranch,
                GitHubRepository = other.GitHubRepository,
                Id = other.Id,
                PublishUsingPipelines = other.PublishUsingPipelines,
                Released = other.Released,
                Staleness = other.Staleness
            };
        }

        private static List<Maestro.Data.Models.BuildDependency> ClientToDataModel_DependentBuildIds(IImmutableList<BuildRef> other)
        {

            List<Maestro.Data.Models.BuildDependency> dependencies = new List<Maestro.Data.Models.BuildDependency>();

            foreach (BuildRef build in other)
            {
                dependencies.Add(ClientToDataModel_BuildDependency(build));
            }

            return dependencies;
        }

        private static Maestro.Data.Models.BuildDependency ClientToDataModel_BuildDependency(BuildRef other)
        {
            // Note: Build, DependentBuild, and DependentBuildId do not have an equivalent in the BuildRef object
            return new Maestro.Data.Models.BuildDependency()
            {
                BuildId = other.BuildId,
                IsProduct = other.IsProduct,
                TimeToInclusionInMinutes = other.TimeToInclusionInMinutes
            };
        }

        private static Maestro.Data.Models.Asset ClientToDataModel_Asset(Asset other)
        {
            throw new NotImplementedException();
        }

        public static Channel DataToClientModel_Channel(Maestro.Data.Models.Channel other)
        {
            return new Channel(other.Id, other.Name, other.Classification, DataToClientModel_ReleasePipelines(other.ChannelReleasePipelines));
        }

        public static Maestro.Data.Models.Channel ClientToDataModel_Channel(Channel other)
        {
            throw new NotImplementedException();
        }

        public static IImmutableList<ReleasePipeline> DataToClientModel_ReleasePipelines(List<Maestro.Data.Models.ChannelReleasePipeline> other)
        {
            throw new NotImplementedException();

        }

        public static SubscriptionPolicy DataToClientModel_SubscriptionPolicy(Maestro.Data.Models.SubscriptionPolicy other)
        {
            throw new NotImplementedException();
        }

        public static ReleasePipeline DataToClientModel_ChannelReleasePipeline(Maestro.Data.Models.ChannelReleasePipeline other)
        {
            throw new NotImplementedException();
        }

        public static AssetLocation DataToClientModel_AssetLocation(Maestro.Data.Models.AssetLocation other)
        {
            return new AssetLocation(other.Id, (LocationType)other.Type, other.Location);
        }

        public static Asset DataToClientModel_Asset(Maestro.Data.Models.Asset other)
        {
            return new Asset(
                other.Id,
                other.BuildId,
                other.NonShipping,
                other.Name,
                other.Version,
                other.Locations?.Select(l => DataToClientModel_AssetLocation(l)).ToImmutableList());
        }

        public static Build DataToClientModel_Build(Maestro.Data.Models.Build other)
        {
            // Note: Channels doesn't have an equivalent in the Data Build object
            return new Build(other.Id, other.DateProduced, other.Staleness, false, other.PublishUsingPipelines, other.Commit,
                null, other.Assets?.Select(a => DataToClientModel_Asset(a)).ToImmutableList(),
                other.DependentBuildIds?.Select(b => new BuildRef(b.BuildId, b.IsProduct, b.TimeToInclusionInMinutes)).ToImmutableList())
            {
                AzureDevOpsBranch = other.AzureDevOpsBranch,
                GitHubBranch = other.GitHubBranch,
                GitHubRepository = other.GitHubRepository,
                AzureDevOpsRepository = other.AzureDevOpsRepository,
            };
        }
    }
}

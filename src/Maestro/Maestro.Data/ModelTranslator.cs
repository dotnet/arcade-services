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
        #region ClientToData

        private static Maestro.Data.Models.Asset ClientToDataModel_Asset(Asset other)
        {
            if (other is null)
            {
                return null;
            }

            return new Maestro.Data.Models.Asset()
            {
                BuildId = other.BuildId,
                Id = other.Id,
                Locations = other.Locations?.Select(l => ClientToDataModel_AssetLocation(l)).ToList(),
                Name = other.Name,
                NonShipping = other.NonShipping,
                Version = other.Version
            };
        }

        private static Maestro.Data.Models.AssetLocation ClientToDataModel_AssetLocation(AssetLocation other)
        {
            if (other is null)
            {
                return null;
            }

            return new Maestro.Data.Models.AssetLocation()
            {
                Id = other.Id,
                Location = other.Location,
                Type = (Maestro.Data.Models.LocationType)other.Type
            };
        }

        public static Maestro.Data.Models.Build ClientToDataModel_Build(Build other)
        {
            if (other is null)
            {
                return null;
            }

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
                DependentBuildIds = other.Dependencies?.Select(build => ClientToDataModel_BuildDependency(build)).ToList(),
                GitHubBranch = other.GitHubBranch,
                GitHubRepository = other.GitHubRepository,
                Id = other.Id,
                PublishUsingPipelines = other.PublishUsingPipelines,
                Released = other.Released,
                Staleness = other.Staleness
            };
        }

        private static Maestro.Data.Models.BuildDependency ClientToDataModel_BuildDependency(BuildRef other)
        {
            if (other is null)
            {
                return null;
            }

            // Note: Build, DependentBuild, and DependentBuildId do not have an equivalent in the BuildRef object
            return new Maestro.Data.Models.BuildDependency()
            {
                BuildId = other.BuildId,
                IsProduct = other.IsProduct,
                TimeToInclusionInMinutes = other.TimeToInclusionInMinutes
            };
        }

        public static Maestro.Data.Models.Channel ClientToDataModel_Channel(Channel other)
        {
            if (other is null)
            {
                return null;
            }

            return new Maestro.Data.Models.Channel()
            {
                // BuildChannels and DefaultBuildChannels don't have an equivalent
                ChannelReleasePipelines = other.ReleasePipelines?.Select(p => ClientToDataModel_ChannelReleasePipelineFromReleasePipeline(p)).ToList(),
                Classification = other.Classification,
                Id = other.Id,
                Name = other.Name
            };
        }

        private static Maestro.Data.Models.ChannelReleasePipeline ClientToDataModel_ChannelReleasePipelineFromReleasePipeline(ReleasePipeline other)
        {
            if (other is null)
            {
                return null;
            }

            return new Maestro.Data.Models.ChannelReleasePipeline()
            {
                // Channel, ChannelId, and ReleasePipeline don't have an equivalent
                ReleasePipelineId = other.PipelineIdentifier
            };
        }

        private static Maestro.Data.Models.MergePolicyDefinition ClientToDataModel_MergePolicy(MergePolicy other)
        {
            if (other is null)
            {
                return null;
            }

            return new Maestro.Data.Models.MergePolicyDefinition()
            {
                Name = other.Name,
                Properties = MergePolicyDictionary_ImmutableToDictionary(other.Properties)
            };
        }

        // Helper function for ClientToDataModel_MergePolicy - going from ImmutableDictionary to Dictionary requires manual re-creation
        private static Dictionary<string, JToken> MergePolicyDictionary_ImmutableToDictionary(IImmutableDictionary<string, JToken> immutableDict)
        {
            Dictionary<string, JToken> propsDictionary = new Dictionary<string, JToken>();

            foreach (KeyValuePair<string, JToken> pair in immutableDict)
            {
                propsDictionary.Add(pair.Key, pair.Value);
            }

            return propsDictionary;
        }

        public static Maestro.Data.Models.SubscriptionPolicy ClientToDataModel_PolicyObject(SubscriptionPolicy other)
        {
            if (other is null)
            {
                return null;
            }

            return new Maestro.Data.Models.SubscriptionPolicy()
            {
                Batchable = other.Batchable,
                MergePolicies = other.MergePolicies?.Select(mp => ClientToDataModel_MergePolicy(mp)).ToList(),
                UpdateFrequency = (Maestro.Data.Models.UpdateFrequency)other.UpdateFrequency
            };
        }

        public static Maestro.Data.Models.Subscription ClientToDataModel_Subscription(Subscription other)
        {
            if (other is null)
            {
                return null;
            }

            // PolicyString does not have an equivalent in the client Subscription object
            return new Maestro.Data.Models.Subscription()
            {
                Channel = ClientToDataModel_Channel(other.Channel),
                Enabled = other.Enabled,
                ChannelId = other.Channel.Id,
                Id = other.Id,
                LastAppliedBuild = ClientToDataModel_Build(other.LastAppliedBuild),
                LastAppliedBuildId = other.LastAppliedBuild?.Id ?? 0,
                PolicyObject = ClientToDataModel_PolicyObject(other.Policy),
                SourceRepository = other.SourceRepository,
                TargetBranch = other.TargetBranch,
                TargetRepository = other.TargetRepository
            };
        }

        #endregion

        #region Unimplemented ClientToData

        public static Maestro.Data.Models.BuildChannel ClientToDataModel_BuildChannel()
        {
            throw new NotImplementedException();
        }

        public static Maestro.Data.Models.Repository ClientToDataModel_Repository()
        {
            throw new NotImplementedException();
        }

        public static Maestro.Data.Models.RepositoryBranch ClientToDataModel_RepositoryBranch()
        {
            throw new NotImplementedException();
        }

        public static Maestro.Data.Models.RepositoryBranch.Policy ClientToDataModel_RepositoryBranchPolicy()
        {
            throw new NotImplementedException();
        }

        public static Maestro.Data.Models.RepositoryBranchUpdate ClientToDataModel_RepositoryBranchUpdate()
        {
            throw new NotImplementedException();
        }

        public static Maestro.Data.Models.RepositoryBranchUpdateHistory ClientToDataModel_RespositoryBranchUpdateHistory()
        {
            throw new NotImplementedException();
        }

        public static Maestro.Data.Models.SubscriptionPolicy ClientToDataModel_SubscriptionPolicy()
        {
            throw new NotImplementedException();
        }

        public static Maestro.Data.Models.SubscriptionUpdate ClientToDataModel_SubscriptionUpdate()
        {
            throw new NotImplementedException();
        }

        public static Maestro.Data.Models.SubscriptionUpdateHistory ClientToDataModel_SubscriptionUpdateHistory()
        {
            throw new NotImplementedException();
        }

        #endregion

        #region DataToClient

        public static Asset DataToClientModel_Asset(Maestro.Data.Models.Asset other)
        {
            if (other is null)
            {
                return null;
            }

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
            if (other is null)
            {
                return null;
            }

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

        public static Channel DataToClientModel_Channel(Maestro.Data.Models.Channel other)
        {
            if (other is null)
            {
                return null;
            }

            return new Channel(other.Id, other.Name, other.Classification,
                other.ChannelReleasePipelines?.Select(p => DataToClientModel_ReleasePipelineFromChannelReleasePipeline(p)).ToImmutableList());
        }

        private static MergePolicy DataToClientModel_MergePolicy(Maestro.Data.Models.MergePolicyDefinition other)
        {
            if (other is null)
            {
                return null;
            }

            return new MergePolicy()
            {
                Name = other.Name,
                Properties = other.Properties.ToImmutableDictionary()
            };
        }

        public static ReleasePipeline DataToClientModel_ReleasePipelineFromChannelReleasePipeline(Maestro.Data.Models.ChannelReleasePipeline other)
        {
            if (other is null)
            {
                return null;
            }

            return new ReleasePipeline(other.ReleasePipeline.Id, other.ReleasePipeline.PipelineIdentifier)
            {
                Organization = other.ReleasePipeline.Organization,
                Project = other.ReleasePipeline.Project
            };
        }

        public static Subscription DataToClientModel_Subscription(Maestro.Data.Models.Subscription other, Maestro.Data.Models.Channel channel = null)
        {
            if (other is null)
            {
                return null;
            }

            return new Subscription(other.Id, other.Enabled, other.SourceRepository, other.TargetRepository, other.TargetBranch)
            {
                Channel = DataToClientModel_Channel(other.Channel ?? channel),
                LastAppliedBuild = DataToClientModel_Build(other.LastAppliedBuild),
                Policy = DataToClientModel_SubscriptionPolicy(other.PolicyObject),
            };
        }

        public static SubscriptionPolicy DataToClientModel_SubscriptionPolicy(Maestro.Data.Models.SubscriptionPolicy other)
        {
            if (other is null)
            {
                return null;
            }

            return new SubscriptionPolicy(other.Batchable, (UpdateFrequency)other.UpdateFrequency)
            {
                MergePolicies = other.MergePolicies?.Select(p => DataToClientModel_MergePolicy(p)).ToImmutableList()
            };
        }
        #endregion

        #region Unimplemented DataToClient

        public static AssetData DataToClientModel_AssetData()
        {
            throw new NotImplementedException();
        }

        public static AssetLocation DataToClientModel_AssetLocation(Maestro.Data.Models.AssetLocation other)
        {
            return new AssetLocation(other.Id, (LocationType)other.Type, other.Location);
        }

        public static AssetLocationData DataToClientModel_AssetLocationData()
        {
            throw new NotImplementedException();
        }

        public static BuildData DataToClientModel_BuildData()
        {
            throw new NotImplementedException();
        }

        public static BuildGraph DataToClientModel_BuildGraph()
        {
            throw new NotImplementedException();
        }

        public static BuildRef DataToClientModel_BuildRef()
        {
            throw new NotImplementedException();
        }

        public static BuildUpdate DataToClientModel_BuildUpdate()
        {
            throw new NotImplementedException();
        }

        public static DefaultChannel DataToClientModel_DefaultChannel()
        {
            throw new NotImplementedException();
        }

        public static DefaultChannelCreateData DataToClientModel_DefaultChannelCreateData()
        {
            throw new NotImplementedException();
        }

        public static DefaultChannelUpdateData DataToClientModel_DefaultChannelUpdateData()
        {
            throw new NotImplementedException();
        }

        public static RepositoryBranch DataToClientModel_RepositoryBranch()
        {
            throw new NotImplementedException();
        }

        public static RepositoryHistoryItem DataToClientModel_RespositoryHistoryItem()
        {
            throw new NotImplementedException();
        }

        public static SubscriptionData DataToClientModel_SubscriptionData()
        {
            throw new NotImplementedException();
        }

        public static SubscriptionHistoryItem DataToClientModel_SubscriptionHistoryItem()
        {
            throw new NotImplementedException();
        }

        public static SubscriptionUpdate DataToClientModel_SubscriptionUpdate()
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Services;
using Microsoft.Internal.Helix.GitHub.Models;
using Microsoft.Internal.Helix.KnownIssues.Models;
using Microsoft.Internal.Helix.KnownIssues.Services;
using Microsoft.Internal.Helix.Utility;
using Microsoft.Internal.Helix.Utility.Azure;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Providers
{
    public class MergedBuildAnalysisProvider : IMergedBuildAnalysisService
    {
        private readonly IRelatedBuildService _related;
        private readonly IPreviousBuildAnalysisService _previous;
        private readonly IBuildAnalysisService _current;
        private readonly IContextualStorage _storage;
        private readonly IGitHubIssuesService _gitHubIssues;
        private readonly ICheckResultService _checkResult;
        private readonly IBuildAnalysisRepositoryConfigurationService _repoConfiguration;
        private readonly IPipelineRequestedService _pipelineRequestedService;

        private readonly JsonSerializerOptions _options = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public MergedBuildAnalysisProvider(
            IRelatedBuildService related,
            IPreviousBuildAnalysisService previous,
            IBuildAnalysisService current,
            IContextualStorage storage,
            IGitHubIssuesService gitHubIssues,
            ICheckResultService knownIssuesBuildResult,
            IBuildAnalysisRepositoryConfigurationService repoConfiguration,
            IPipelineRequestedService pipelineRequestedService)
        {
            _related = related;
            _previous = previous;
            _current = current;
            _storage = storage;
            _gitHubIssues = gitHubIssues;
            _checkResult = knownIssuesBuildResult;
            _repoConfiguration = repoConfiguration;
            _pipelineRequestedService = pipelineRequestedService;
        }

        public async Task<MergedBuildResultAnalysis> GetMergedAnalysisAsync(
            NamedBuildReference referenceBuild,
            MergeBuildAnalysisAction action,
            CancellationToken cancellationToken)
        {
            AggregateRelatedBuild buildStatus;
            {
                await using Stream relatedBuildStream = await _storage.TryGetAsync("related-builds.json", cancellationToken);
                if (relatedBuildStream == null)
                {
                    buildStatus = new AggregateRelatedBuild {ByDefinition = new Dictionary<int, RelatedBuild>()};
                }
                else
                {
                    buildStatus = await JsonSerializer.DeserializeAsync<AggregateRelatedBuild>(relatedBuildStream, _options, cancellationToken);
                }
            }

            buildStatus.ByDefinition[referenceBuild.DefinitionId] = new RelatedBuild
            {
                BuildId = referenceBuild.BuildId,
                ProjectId = referenceBuild.Project,
                Name = referenceBuild.Name,
                Included = action == MergeBuildAnalysisAction.Include,
                Url = referenceBuild.WebUrl,
            };

            await Helpers.StreamDataAsync(
                s => JsonSerializer.SerializeAsync(s, buildStatus, _options, cancellationToken),
                s => _storage.PutAsync("related-builds.json", s, cancellationToken)
            );

            var builds = ImmutableList.CreateBuilder<BuildResultAnalysis>();
            if (action == MergeBuildAnalysisAction.Include)
            {
                builds.Add(await _current.GetBuildResultAnalysisAsync(referenceBuild, cancellationToken));
            }

            RelatedBuilds relatedBuildIds = await _related.GetRelatedBuilds(referenceBuild, cancellationToken);
            BuildsByPipelineConfiguration relatedBuildsByPipelineConfiguration = await _pipelineRequestedService.GetBuildsByPipelineConfiguration(relatedBuildIds.RelatedBuildsList,
                    referenceBuild);

            BuildResultAnalysis[] relatedBuilds = await Task.WhenAll(
                relatedBuildsByPipelineConfiguration.IncludedPipelinesBuilds
                    .Where(rb => !buildStatus.ByDefinition.TryGetValue(rb.DefinitionId, out var build) || build.Included)
                    .Select(b => _previous.GetBuildResultAnalysisAsync(b, cancellationToken))
            );

            builds.AddRange(relatedBuilds.Where(b => b != null));

            ImmutableList<BuildResultAnalysis> analyses = builds.ToImmutable();
            ImmutableList<Link> pendingBuildNames = relatedBuildsByPipelineConfiguration.IncludedPipelinesBuilds
                .Select(rb => buildStatus.ByDefinition.TryGetValue(rb.DefinitionId, out var build) ? build : null)
                .Where(bs => bs != null && !bs.Included)
                .Select(bs => new Link(bs.Name, bs.Url))
                .ToImmutableList();

            ImmutableList<Link> filteredPipelinesBuilds = relatedBuildsByPipelineConfiguration.FilteredPipelinesBuilds
                .Select(bs => new Link(bs.DefinitionName, bs.BuildUrl))
                .ToImmutableList();

            if (action == MergeBuildAnalysisAction.Exclude)
            {
                // We we are excluding a build, we know it's related to this build (it IS this build)
                // Since "RelatedBuildsList" won't return it, we need to add it to the pending list
                pendingBuildNames = pendingBuildNames.Add(new Link(referenceBuild.Name, referenceBuild.WebUrl));
            }

            bool shouldMergeOnFailureWithKnownIssues = await GetMergeOnKnownIssuesConfiguration(referenceBuild.RepositoryId, analyses.FirstOrDefault()?.TargetBranch?.BranchName, cancellationToken);

            CheckResult status = _checkResult.GetCheckResult(referenceBuild, analyses, pendingBuildNames.Count, shouldMergeOnFailureWithKnownIssues);

            ImmutableList<KnownIssue> criticalIssues = await _gitHubIssues.GetCriticalInfrastructureIssuesAsync();

            return new MergedBuildResultAnalysis(
                referenceBuild.SourceSha,
                analyses,
                status,
                pendingBuildNames,
                filteredPipelinesBuilds,
                criticalIssues
            );
        }

        private async Task<bool> GetMergeOnKnownIssuesConfiguration(string repository, string branchName, CancellationToken cancellationToken)
        {
            BuildAnalysisRepositoryConfiguration repoConfig = await _repoConfiguration.GetRepositoryConfiguration(
                repository,
                branchName,
                cancellationToken);

            return repoConfig?.ShouldMergeOnFailureWithKnownIssues ?? false;
        }

        public class AggregateRelatedBuild
        {
            public Dictionary<int, RelatedBuild> ByDefinition { get; set; }
        }

        public class RelatedBuild
        {
            public string Name { get; set; }
            public string Url { get; set; }
            public string ProjectId { get; set; }
            public int BuildId { get; set; }
            public bool Included { get; set; }
        }
    }
}

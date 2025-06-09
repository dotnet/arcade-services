// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Maestro.Data;
using Maestro.MergePolicies;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.WorkItems;

using BuildDTO = Microsoft.DotNet.ProductConstructionService.Client.Models.Build;

namespace ProductConstructionService.DependencyFlow;

internal interface IPullRequestBuilder
{
    /// <summary>
    /// Commit a dependency update to a target branch and calculate the PR description
    /// </summary>
    /// <param name="requiredUpdates">Version updates to apply</param>
    /// <param name="currentDescription">
    ///     A string writer that the PR description should be written to. If this an update
    ///     to an existing PR, this will contain the existing PR description.
    /// </param>
    /// <param name="targetRepository">Target repository that the updates should be applied to</param>
    /// <param name="newBranchName">Target branch the updates should be to</param>
    Task<string> CalculatePRDescriptionAndCommitUpdatesAsync(
        List<(SubscriptionUpdateWorkItem update, List<DependencyUpdate> deps)> requiredUpdates,
        string? currentDescription,
        string targetRepository,
        string newBranchName);

    /// <summary>
    ///     Compute the title for a pull request.
    /// </summary>
    /// <returns>Pull request title</returns>
    Task<string> GeneratePRTitleAsync(
        List<SubscriptionPullRequestUpdate> subscriptions,
        string targetBranch);


    /// <summary>
    /// Creates a CodeFlow PR title based on source repo names and the target branch
    /// <param name="targetBranch">Name of the target branch</param>
    /// <param name="repoNames">List of repository names to be included in the title</param>
    string GenerateCodeFlowPRTitle(
        string targetBranch,
        List<string> repoNames);

    /// <summary>
    ///    Generate the description for a code flow PR.
    /// </summary>
    string GenerateCodeFlowPRDescription(
        SubscriptionUpdateWorkItem update,
        BuildDTO build,
        string? previousSourceCommit,
        List<DependencyUpdateSummary> dependencyUpdates,
        IReadOnlyCollection<UpstreamRepoDiff>? upstreamRepoDiffs,
        string? currentDescription,
        bool isForwardFlow);
}

internal class PullRequestBuilder : IPullRequestBuilder
{
    public const int GitHubComparisonShaLength = 10;
    public const string CodeFlowPrFaqUri = "https://github.com/dotnet/dotnet/tree/main/docs/Codeflow-PRs.md";

    // PR description markers
    private const string DependencyUpdateBegin = "[DependencyUpdate]: <> (Begin)";
    private const string DependencyUpdateEnd = "[DependencyUpdate]: <> (End)";

    private const string FooterStartMarker = "[marker]: <> (Start:Footer:CodeFlow PR)";
    private const string FooterEndMarker = "[marker]: <> (End:Footer:CodeFlow PR)";

    private const string CommitDiffNotAvailableMsg = "Not available";

    private readonly BuildAssetRegistryContext _context;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IBasicBarClient _barClient;
    private readonly ILogger<PullRequestBuilder> _logger;

    private record DependencyCategories(
        IReadOnlyCollection<DependencyUpdateSummary> NewDependencies,
        IReadOnlyCollection<DependencyUpdateSummary> RemovedDependencies,
        IReadOnlyCollection<DependencyUpdateSummary> UpdatedDependencies
    );

    public PullRequestBuilder(
        BuildAssetRegistryContext context,
        IRemoteFactory remoteFactory,
        IBasicBarClient barClient,
        ILogger<PullRequestBuilder> logger)
    {
        _context = context;
        _remoteFactory = remoteFactory;
        _barClient = barClient;
        _logger = logger;
    }

    public async Task<string> GeneratePRTitleAsync(List<SubscriptionPullRequestUpdate> subscriptions, string targetBranch)
    {
        // Get the unique subscription IDs. It may be possible for a coherency update
        // to not have any contained subscription.  In this case
        // we return a different title.
        var uniqueSubscriptionIds = subscriptions
            .Select(subscription => subscription.SubscriptionId)
            .Distinct()
            .ToArray();

        if (uniqueSubscriptionIds.Length == 0)
        {
            return $"[{targetBranch}] Update dependencies to ensure coherency";
        }

        List<string> repoNames = await _context.Subscriptions
            .Where(s => uniqueSubscriptionIds.Contains(s.Id) && !string.IsNullOrEmpty(s.SourceRepository))
            .Select(s => s.SourceRepository!)
            .ToListAsync();

        return GeneratePRTitle($"[{targetBranch}] Update dependencies from", repoNames);
    }

    public async Task<string> CalculatePRDescriptionAndCommitUpdatesAsync(
        List<(SubscriptionUpdateWorkItem update, List<DependencyUpdate> deps)> requiredUpdates,
        string? currentDescription,
        string targetRepository,
        string newBranchName)
    {
        StringBuilder description = new StringBuilder(currentDescription ?? "This pull request updates the following dependencies")
            .AppendLine()
            .AppendLine();
        var startingReferenceId = GetStartingReferenceId(description.ToString());

        // First run through non-coherency and then do a coherency
        // message if one exists.
        var nonCoherencyUpdates =
            requiredUpdates.Where(u => !u.update.IsCoherencyUpdate).ToList();
        // Should max one coherency update
        (SubscriptionUpdateWorkItem update, List<DependencyUpdate> deps) coherencyUpdate =
            requiredUpdates.Where(u => u.update.IsCoherencyUpdate).SingleOrDefault();

        IRemote remote = await _remoteFactory.CreateRemoteAsync(targetRepository);
        var locationResolver = new AssetLocationResolver(_barClient);

        // To keep a PR to as few commits as possible, if the number of
        // non-coherency updates is 1 then combine coherency updates with those.
        // Otherwise, put all coherency updates in a separate commit.
        var combineCoherencyWithNonCoherency = nonCoherencyUpdates.Count == 1;

        foreach ((SubscriptionUpdateWorkItem update, List<DependencyUpdate> deps) in nonCoherencyUpdates)
        {
            var message = new StringBuilder();
            List<DependencyUpdate> dependenciesToCommit = deps;
            await CalculateCommitMessage(update, deps, message);
            var build = await _barClient.GetBuildAsync(update.BuildId)
                ?? throw new Exception($"Failed to find build {update.BuildId} for subscription {update.SubscriptionId}");

            if (combineCoherencyWithNonCoherency && coherencyUpdate.update != null)
            {
                await CalculateCommitMessage(coherencyUpdate.update, coherencyUpdate.deps, message);
                AppendCoherencyUpdateDescription(description, coherencyUpdate.deps);
                dependenciesToCommit.AddRange(coherencyUpdate.deps);
            }

            var itemsToUpdate = dependenciesToCommit
                .Select(du => du.To)
                .ToList();

            await locationResolver.AddAssetLocationToDependenciesAsync(itemsToUpdate);

            List<GitFile> committedFiles = await remote.CommitUpdatesAsync(
                targetRepository,
                newBranchName,
                itemsToUpdate,
                message.ToString());

            AppendBuildDescription(description, ref startingReferenceId, update, deps, committedFiles, build);
        }

        // If the coherency update wasn't combined, then
        // add it now
        if (!combineCoherencyWithNonCoherency && coherencyUpdate.update != null)
        {
            var message = new StringBuilder();
            var build = await _barClient.GetBuildAsync(coherencyUpdate.update.BuildId);
            await CalculateCommitMessage(coherencyUpdate.update, coherencyUpdate.deps, message);
            AppendCoherencyUpdateDescription(description, coherencyUpdate.deps);

            var itemsToUpdate = coherencyUpdate.deps
                .Select(du => du.To)
                .ToList();

            await locationResolver.AddAssetLocationToDependenciesAsync(itemsToUpdate);
            await remote.CommitUpdatesAsync(
                targetRepository,
                newBranchName,
                itemsToUpdate,
                message.ToString());
        }

        // If the coherency algorithm failed and there are no non-coherency updates and
        // we create an empty commit that describes an issue.
        if (requiredUpdates.Count == 0)
        {
            var message = "Failed to perform coherency update for one or more dependencies.";
            await remote.CommitUpdatesAsync(targetRepository, newBranchName, [], message);
            return $"Coherency update: {message} Please review the GitHub checks or run `darc update-dependencies --coherency-only` locally against {newBranchName} for more information.";
        }

        return description.ToString();
    }

    public string GenerateCodeFlowPRTitle(
        string targetBranch,
        List<string> repoNames)
    {
        return GeneratePRTitle($"[{targetBranch}] Source code updates from", repoNames);
    }

    public string GenerateCodeFlowPRDescription(
        SubscriptionUpdateWorkItem update,
        BuildDTO build,
        string? previousSourceCommit,
        List<DependencyUpdateSummary> dependencyUpdates,
        IReadOnlyCollection<UpstreamRepoDiff>? upstreamRepoDiffs,
        string? currentDescription,
        bool isForwardFlow)
    {
        string description = GenerateCodeFlowPRDescriptionInternal(
            update,
            build,
            previousSourceCommit,
            dependencyUpdates,
            currentDescription,
            isForwardFlow);

        description = CompressRepeatedLinksInDescription(description);

        return AddOrUpdateFooterInDescription(description, upstreamRepoDiffs);
    }

    private static string GenerateCodeFlowPRDescriptionInternal(
        SubscriptionUpdateWorkItem update,
        BuildDTO build,
        string? previousSourceCommit,
        List<DependencyUpdateSummary> dependencyUpdates,
        string? currentDescription,
        bool isForwardFlow)
    {
        if (string.IsNullOrEmpty(currentDescription))
        {
            // if PR is new, create the new subscription update section along with the PR header
            return $"""
                
                > [!NOTE]
                > This is a codeflow update. It may contain both source code changes from [{(isForwardFlow ? "the source repo" : "the VMR")}]({update.SourceRepo}) as well as dependency updates. Learn more [here]({CodeFlowPrFaqUri}).

                This pull request brings the following source code changes
                {GenerateCodeFlowDescriptionForSubscription(update.SubscriptionId, previousSourceCommit, build, update.SourceRepo, dependencyUpdates)}
                """;
        }
        else
        {
            // if PR description already exists, update only the section relevant to the current subscription
            int startIndex = currentDescription.IndexOf(GetStartMarker(update.SubscriptionId));
            int endIndex = currentDescription.IndexOf(GetEndMarker(update.SubscriptionId));

            int startCutoff = startIndex == -1 ?
                currentDescription.Length :
                startIndex;
            int endCutoff = endIndex == -1 ?
                currentDescription.Length :
                endIndex + GetEndMarker(update.SubscriptionId).Length;

            return string.Concat(
                currentDescription.AsSpan(0, startCutoff),
                GenerateCodeFlowDescriptionForSubscription(update.SubscriptionId, previousSourceCommit, build, update.SourceRepo, dependencyUpdates),
                currentDescription.AsSpan(endCutoff, currentDescription.Length - endCutoff));
        }
    }

    private static string AddOrUpdateFooterInDescription(string description, IReadOnlyCollection<UpstreamRepoDiff>? upstreamRepoDiffs)
    {
        int footerStartIndex = description.IndexOf(FooterStartMarker);
        int footerEndIndex = description.IndexOf(FooterEndMarker);

        // Remove footer if exists
        if (footerStartIndex != -1 && footerEndIndex != -1)
        {
            description = description.Remove(footerStartIndex, footerEndIndex - footerStartIndex + FooterEndMarker.Length);
        }

        if (upstreamRepoDiffs == null || !upstreamRepoDiffs.Any())
        {
            return description;
        }
        else
        {
            description += $"""
            {FooterStartMarker}

            ---

            ## Changes in other repos since the last backflow PR:
            {GenerateUpstreamRepoDiffs(upstreamRepoDiffs)}
            {FooterEndMarker}
            """;
            return description;
        }
    }

    private static string GenerateUpstreamRepoDiffs(IReadOnlyCollection<UpstreamRepoDiff> upstreamRepoDiffs)
    {
        StringBuilder sb = new StringBuilder();
        foreach (UpstreamRepoDiff upstreamRepoDiff in upstreamRepoDiffs)
        {
            if (!string.IsNullOrEmpty(upstreamRepoDiff.RepoUri)
                && !string.IsNullOrEmpty(upstreamRepoDiff.OldCommitSha)
                && !string.IsNullOrEmpty(upstreamRepoDiff.NewCommitSha))
            {
                sb.AppendLine($"- {upstreamRepoDiff.RepoUri}/compare/{upstreamRepoDiff.OldCommitSha}...{upstreamRepoDiff.NewCommitSha}");
            }
        }
        return sb.ToString();
    }

    private static string GenerateCodeFlowDescriptionForSubscription(
        Guid subscriptionId,
        string? previousSourceCommit,
        BuildDTO build,
        string repoUri,
        List<DependencyUpdateSummary> dependencyUpdates)
    {
        string sourceDiffText = CreateSourceDiffLink(build, previousSourceCommit);

        string dependencyUpdateBlock = CreateDependencyUpdateBlock(dependencyUpdates, repoUri);
        return
            $"""

            {GetStartMarker(subscriptionId)}
            
            ## From {build.GetRepository()}
            - **Subscription**: {GetSubscriptionLink(subscriptionId)}
            - **Build**: [{build.AzureDevOpsBuildNumber}]({build.GetBuildLink()})
            - **Date Produced**: {build.DateProduced.ToUniversalTime():MMMM d, yyyy h:mm:ss tt UTC}
            - **Commit Diff**: {sourceDiffText}
            - **Commit**: [{build.Commit}]({build.GetCommitLink()})
            - **Branch**: {build.GetBranch()}
            {dependencyUpdateBlock}
            {GetEndMarker(subscriptionId)}

            """;
    }

    internal static string CreateDependencyUpdateBlock(
        List<DependencyUpdateSummary> dependencyUpdateSummaries,
        string repoUri)
    {
        if (dependencyUpdateSummaries.Count == 0)
        {
            return string.Empty;
        }

        DependencyCategories dependencyCategories = CreateDependencyCategories(dependencyUpdateSummaries);

        StringBuilder stringBuilder = new StringBuilder();

        if (dependencyCategories.NewDependencies.Count > 0)
        {
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("**New Dependencies**");
            foreach (DependencyUpdateSummary depUpdate in dependencyCategories.NewDependencies)
            {
                string? diffLink = GetLinkForDependencyItem(repoUri, depUpdate);
                stringBuilder.AppendLine($"- **{depUpdate.DependencyName}**: [{depUpdate.ToVersion}]({diffLink})");
            }
        }

        if (dependencyCategories.RemovedDependencies.Count > 0)
        {
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("**Removed Dependencies**");
            foreach (DependencyUpdateSummary depUpdate in dependencyCategories.RemovedDependencies)
            {
                stringBuilder.AppendLine($"- **{depUpdate.DependencyName}**: {depUpdate.FromVersion}");
            }
        }

        if (dependencyCategories.UpdatedDependencies.Count > 0)
        {
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("**Updated Dependencies**");
            foreach (DependencyUpdateSummary depUpdate in dependencyCategories.UpdatedDependencies)
            {
                string? diffLink = GetLinkForDependencyItem(repoUri, depUpdate);
                stringBuilder.AppendLine($"- **{depUpdate.DependencyName}**: [from {depUpdate.FromVersion} to {depUpdate.ToVersion}]({diffLink})");
            }
        }
        return stringBuilder.ToString();
    }

    private static DependencyCategories CreateDependencyCategories(List<DependencyUpdateSummary> dependencyUpdateSummaries)
    {
        List<DependencyUpdateSummary> newDependencies = new();
        List<DependencyUpdateSummary> removedDependencies = new();
        List<DependencyUpdateSummary> updatedDependencies = new();

        foreach (DependencyUpdateSummary depUpdate in dependencyUpdateSummaries)
        {
            if (string.IsNullOrEmpty(depUpdate.FromVersion))
            {
                newDependencies.Add(depUpdate);
            }
            else if (string.IsNullOrEmpty(depUpdate.ToVersion))
            {
                removedDependencies.Add(depUpdate);
            }
            else
            {
                updatedDependencies.Add(depUpdate);
            }
        }

        return new DependencyCategories(newDependencies, removedDependencies, updatedDependencies);
    }

    private static string? GetLinkForDependencyItem(string repoUri, DependencyUpdateSummary dependencyUpdateSummary)
    {
        if (!string.IsNullOrEmpty(dependencyUpdateSummary.FromCommitSha) &&
            !string.IsNullOrEmpty(dependencyUpdateSummary.ToCommitSha))
        {
            return GetChangesURI(repoUri, dependencyUpdateSummary.FromCommitSha, dependencyUpdateSummary.ToCommitSha);
        }

        if (!string.IsNullOrEmpty(dependencyUpdateSummary.ToCommitSha))
        {
            return GetCommitURI(repoUri, dependencyUpdateSummary.ToCommitSha);
        }

        return null;
    }

    internal static string? GetCommitURI(string repoUri, string commitSha)
    {
        if (repoUri.Contains("github.com"))
        {
            return $"{repoUri}/commit/{commitSha}";
        }
        if (repoUri.Contains("dev.azure.com"))
        {
            return $"{repoUri}?_a=history&version=GC{commitSha}";
        }
        return null;
    }

    private static string CreateSourceDiffLink(BuildDTO build, string? previousSourceCommit)
    {
        // previous source commit may be null in the case of the first code flow between a repo and the VMR ?
        if (string.IsNullOrEmpty(previousSourceCommit))
        {
            return CommitDiffNotAvailableMsg;
        }

        string sourceDiffText = $"{Commit.GetShortSha(previousSourceCommit)}...{Commit.GetShortSha(build.Commit)}";

        if (!string.IsNullOrEmpty(build.GitHubRepository))
        {
            return $"[{sourceDiffText}]({build.GitHubRepository}/compare/{previousSourceCommit}...{build.Commit})";
        }
        else if (!string.IsNullOrEmpty(build.AzureDevOpsRepository))
        {
            return $"[{sourceDiffText}]({build.AzureDevOpsRepository}/branchCompare?" +
                $"baseVersion=GC{previousSourceCommit}&targetVersion=GC{build.Commit}&_a=files)";
        }
        else
        {
            return CommitDiffNotAvailableMsg;
        }
    }

    /// <summary>
    /// Returns a description where links that appear multiple times are replaced by reference-style links.
    /// Example:
    /// [commitA](http://github.com/foo/bar/commit-A-SHA)
    /// [commitA](http://github.com/foo/bar/commit-A-SHA)
    /// is transformed into:
    /// [commitA][1]
    /// [commitA][1]
    /// [1]: http://github.com/foo/bar/commit-A-SHA
    /// </summary>
    /// <param name="description"></param>
    /// <returns></returns>
    private static string CompressRepeatedLinksInDescription(string description)
    {
        string pattern = "\\((https?://\\S+|www\\.\\S+)\\)";

        var matches = Regex.Matches(description, pattern).Select(m => m.Value).ToList();

        var linkGroups = matches.GroupBy(link => link)
                                .Where(group => group.Count() >= 2)
                                .Select((group, index) => new { Link = group.Key, Index = index + 1 })
                                .ToDictionary(x => x.Link, x => x.Index);

        if (linkGroups.Count == 0)
        {
            return description;
        }

        foreach (var entry in linkGroups)
        {
            description = Regex.Replace(description, $"{Regex.Escape(entry.Key)}", $"[{entry.Value}]");
        }

        StringBuilder linkReferencesSection = new StringBuilder();
        linkReferencesSection.AppendLine();

        foreach (var entry in linkGroups)
        {
            linkReferencesSection.AppendLine($"[{entry.Value}]: {entry.Key.TrimStart('(').TrimEnd(')')}");
        }

        return description + linkReferencesSection.ToString();
    }

    /// <summary>
    ///     Append build description to the PR description
    /// </summary>
    /// <param name="description">Description to extend</param>
    /// <param name="startingReferenceId">Counter for references</param>
    /// <param name="update">Update</param>
    /// <param name="deps">Dependencies updated</param>
    /// <param name="committedFiles">List of commited files</param>
    /// <param name="build">Build</param>
    /// <remarks>
    ///     Because PRs tend to be live for short periods of time, we can put more information
    ///     in the description than the commit message without worrying that links will go stale.
    /// </remarks>
    private void AppendBuildDescription(StringBuilder description, ref int startingReferenceId, SubscriptionUpdateWorkItem update, List<DependencyUpdate> deps, List<GitFile>? committedFiles, Microsoft.DotNet.ProductConstructionService.Client.Models.Build build)
    {
        var changesLinks = new List<string>();

        var sourceRepository = update.SourceRepo;
        Guid updateSubscriptionId = update.SubscriptionId;
        var sectionStartMarker = GetStartMarker(updateSubscriptionId);
        var sectionEndMarker = GetEndMarker(updateSubscriptionId);
        var sectionStartIndex = RemovePRDescriptionSection(description, sectionStartMarker, sectionEndMarker);

        var subscriptionSection = new StringBuilder()
            .AppendLine(sectionStartMarker)
            .AppendLine($"## From {sourceRepository}")
            .AppendLine($"- **Subscription**: {GetSubscriptionLink(updateSubscriptionId)}")
            .AppendLine($"- **Build**: [{build.AzureDevOpsBuildNumber}]({build.GetBuildLink()})")
            .AppendLine($"- **Date Produced**: {build.DateProduced.ToUniversalTime():MMMM d, yyyy h:mm:ss tt UTC}")
            // This is duplicated from the files changed, but is easier to read here.
            .AppendLine($"- **Commit**: [{build.Commit}]({build.GetCommitLink()})");

        var branch = build.AzureDevOpsBranch ?? build.GitHubBranch;
        if (!string.IsNullOrEmpty(branch))
        {
            subscriptionSection.AppendLine($"- **Branch**: {branch}");
        }

        subscriptionSection
            .AppendLine()
            .AppendLine(DependencyUpdateBegin)
            .AppendLine()
            .AppendLine($"- **Updates**:");

        var shaRangeToLinkId = new Dictionary<(string from, string to), int>();

        foreach (DependencyUpdate dep in deps)
        {
            if (!shaRangeToLinkId.ContainsKey((dep.From.Commit, dep.To.Commit)))
            {
                var changesUri = string.Empty;
                try
                {
                    changesUri = GetChangesURI(dep.To.RepoUri, dep.From.Commit, dep.To.Commit);
                }
                catch (ArgumentNullException e)
                {
                    _logger.LogError(e, $"Failed to create SHA comparison link for dependency {dep.To.Name} during asset update for subscription {update.SubscriptionId}");
                }
                shaRangeToLinkId.Add((dep.From.Commit, dep.To.Commit), startingReferenceId + changesLinks.Count);
                changesLinks.Add(changesUri);
            }
            subscriptionSection.AppendLine($"  - **{dep.To.Name}**: [from {dep.From.Version} to {dep.To.Version}][{shaRangeToLinkId[(dep.From.Commit, dep.To.Commit)]}]");
        }

        subscriptionSection.AppendLine();
        for (var i = 0; i < changesLinks.Count; i++)
        {
            subscriptionSection.AppendLine($"[{i + startingReferenceId}]: {changesLinks[i]}");
        }

        subscriptionSection
            .AppendLine()
            .AppendLine(DependencyUpdateEnd)
            .AppendLine();

        UpdatePRDescriptionDueConfigFiles(committedFiles, subscriptionSection);

        subscriptionSection
            .AppendLine()
            .AppendLine(sectionEndMarker);

        description.Insert(sectionStartIndex, subscriptionSection.ToString());
        description.AppendLine();

        startingReferenceId += changesLinks.Count;
    }

    /// <summary>
    ///     Append coherency update description to the PR description
    /// </summary>
    /// <param name="description">Description to extend</param>
    /// <param name="dependencies">Dependencies updated</param>
    /// <remarks>
    ///     Because PRs tend to be live for short periods of time, we can put more information
    ///     in the description than the commit message without worrying that links will go stale.
    /// </remarks>
    private static void AppendCoherencyUpdateDescription(StringBuilder description, List<DependencyUpdate> dependencies)
    {
        var sectionStartMarker = "[marker]: <> (Begin:Coherency Updates)";
        var sectionEndMarker = "[marker]: <> (End:Coherency Updates)";
        var sectionStartIndex = RemovePRDescriptionSection(description, sectionStartMarker, sectionEndMarker);

        var coherencySection = new StringBuilder()
            .AppendLine(sectionStartMarker)
            .AppendLine("## Coherency Updates")
            .AppendLine()
            .AppendLine("The following updates ensure that dependencies with a *CoherentParentDependency*")
            .AppendLine("attribute were produced in a build used as input to the parent dependency's build.")
            .AppendLine("See [Dependency Description Format](https://github.com/dotnet/arcade/blob/master/Documentation/DependencyDescriptionFormat.md#dependency-description-overview)")
            .AppendLine()
            .AppendLine(DependencyUpdateBegin)
            .AppendLine()
            .AppendLine("- **Coherency Updates**:");

        foreach (DependencyUpdate dep in dependencies)
        {
            coherencySection.AppendLine($"  - **{dep.To.Name}**: from {dep.From.Version} to {dep.To.Version} (parent: {dep.To.CoherentParentDependencyName})");
        }

        coherencySection
            .AppendLine()
            .AppendLine(DependencyUpdateEnd)
            .AppendLine()
            .AppendLine(sectionEndMarker);

        description.Insert(sectionStartIndex, coherencySection.ToString());
        description.AppendLine();
    }

    private async Task CalculateCommitMessage(SubscriptionUpdateWorkItem update, List<DependencyUpdate> deps, StringBuilder message)
    {
        if (update.IsCoherencyUpdate)
        {
            message.AppendLine("Dependency coherency updates");
            message.AppendLine();
            message.AppendLine(string.Join(",", deps.Select(p => p.To.Name)));
            message.AppendLine($" From Version {deps[0].From.Version} -> To Version {deps[0].To.Version} (parent: {deps[0].To.CoherentParentDependencyName}");
        }
        else
        {
            var sourceRepository = update.SourceRepo;
            var build = await _barClient.GetBuildAsync(update.BuildId);
            message.AppendLine($"Update dependencies from {sourceRepository} build {build?.AzureDevOpsBuildNumber}");
            message.AppendLine();
            message.AppendLine(string.Join(" , ", deps.Select(p => p.To.Name)));
            message.AppendLine($" From Version {deps[0].From.Version} -> To Version {deps[0].To.Version}");
        }

        message.AppendLine();
    }

    private static void UpdatePRDescriptionDueConfigFiles(List<GitFile>? committedFiles, StringBuilder globalJsonSection)
    {
        GitFile? globalJsonFile = committedFiles?.
            Where(gf => gf.FilePath.Equals("global.json", StringComparison.OrdinalIgnoreCase)).
            FirstOrDefault();

        // The list of committedFiles can contain the `global.json` file (and others) 
        // even though no actual change was made to the file and therefore there is no 
        // metadata for it.
        if (globalJsonFile?.Metadata != null)
        {
            var hasSdkVersionUpdate = globalJsonFile.Metadata.ContainsKey(GitFileMetadataName.SdkVersionUpdate);
            var hasToolsDotnetUpdate = globalJsonFile.Metadata.ContainsKey(GitFileMetadataName.ToolsDotNetUpdate);

            globalJsonSection.AppendLine("- **Updates to .NET SDKs:**");

            if (hasSdkVersionUpdate)
            {
                globalJsonSection.AppendLine($"  - Updates sdk.version to " +
                    $"{globalJsonFile.Metadata[GitFileMetadataName.SdkVersionUpdate]}");
            }

            if (hasToolsDotnetUpdate)
            {
                globalJsonSection.AppendLine($"  - Updates tools.dotnet to " +
                    $"{globalJsonFile.Metadata[GitFileMetadataName.ToolsDotNetUpdate]}");
            }
        }
    }

    private static int RemovePRDescriptionSection(StringBuilder description, string sectionStartMarker, string sectionEndMarker)
    {
        var descriptionString = description.ToString();
        var sectionStartIndex = descriptionString.IndexOf(sectionStartMarker);
        var sectionEndIndex = descriptionString.IndexOf(sectionEndMarker);

        if (sectionStartIndex != -1 && sectionEndIndex != -1)
        {
            sectionEndIndex += sectionEndMarker.Length;
            description.Remove(sectionStartIndex, sectionEndIndex - sectionStartIndex);
            return sectionStartIndex;
        }

        // if either marker is missing, just append at end and don't remove anything
        // from the description
        return description.Length;
    }

    /// <summary>
    /// Goes through the description and finds the biggest reference id. This is needed when updating an exsiting PR.
    /// </summary>
    internal static int GetStartingReferenceId(string description)
    {
        //The regex is matching numbers surrounded by square brackets that have a colon and something after it.
        //The regex captures these numbers
        //example: given [23]:sometext as input, it will attempt to capture "23"
        var regex = new Regex("(?<=^\\[)\\d+(?=\\]:.+)", RegexOptions.Multiline);

        return regex.Matches(description.ToString())
            .Select(m => int.Parse(m.ToString()))
            .DefaultIfEmpty(0)
            .Max() + 1;
    }

    internal static string GetChangesURI(string repoURI, string fromSha, string toSha)
    {
        ArgumentNullException.ThrowIfNull(repoURI);
        ArgumentNullException.ThrowIfNull(fromSha);
        ArgumentNullException.ThrowIfNull(toSha);

        if (repoURI.Contains("github.com"))
        {
            var fromShortSha = fromSha.Length > GitHubComparisonShaLength ? fromSha.Substring(0, GitHubComparisonShaLength) : fromSha;
            var toShortSha = toSha.Length > GitHubComparisonShaLength ? toSha.Substring(0, GitHubComparisonShaLength) : toSha;

            return $"{repoURI}/compare/{fromShortSha}...{toShortSha}";
        }

        // Azdo commit comparison doesn't work with short shas
        return $"{repoURI}/branches?baseVersion=GC{fromSha}&targetVersion=GC{toSha}&_a=files";
    }

    /// <summary>
    /// Creates a title from the list of involved repos (in a shortened form)
    /// or just with the number of repos if the title would otherwise be too long.
    /// <param name="baseTitle">Start of the title to append the list to</param>
    /// <param name="repoNames">List of repository names to be included in the title</param>
    private static string GeneratePRTitle(string baseTitle, List<string> repoNames)
    {
        // Github title limit - 348 
        // Azdo title limit - 419 
        // maxTitleLength = 150 to fit 2/3 repo names in the title
        const int titleLengthLimit = 150;
        const string delimiter = ", ";

        if (repoNames == null || !repoNames.Any())
            return "";

        List<string> simpleNames = repoNames
            .Select(name => name
                .Replace("https://github.com/", string.Empty)
                .Replace("https://dev.azure.com/", string.Empty)
                .Replace("_git/", string.Empty))
            .ToList();

        int totalLength = simpleNames.Sum(name => name.Length)
            + delimiter.Length * (simpleNames.Count - 1)
            + baseTitle.Length;

        if (totalLength > titleLengthLimit)
            return $"{baseTitle} {simpleNames.Count} repositories";

        return $"{baseTitle} {string.Join(delimiter, simpleNames.OrderBy(s => s))}";
    }

    private static string GetStartMarker(Guid subscriptionId)
        => $"[marker]: <> (Begin:{subscriptionId})";

    private static string GetEndMarker(Guid subscriptionId)
        => $"[marker]: <> (End:{subscriptionId})";

    private static string GetSubscriptionLink(Guid subscriptionId)
        => $"[{subscriptionId}](https://maestro.dot.net/subscriptions?search={subscriptionId})";
}

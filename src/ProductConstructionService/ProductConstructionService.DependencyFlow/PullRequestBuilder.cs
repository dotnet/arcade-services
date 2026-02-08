// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Maestro.Common;
using Maestro.Data;
using Maestro.MergePolicies;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.Model;
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
        TargetRepoDependencyUpdates requiredUpdates,
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
    Task<string> GenerateCodeFlowPRDescription(
        BuildDTO build,
        Subscription subscription,
        string headBranch,
        string? previousSourceCommit,
        List<DependencyUpdateSummary> dependencyUpdates,
        IReadOnlyCollection<UpstreamRepoDiff>? upstreamRepoDiffs,
        string? currentDescription);
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

    /// <summary>
    /// The regex is matching numbers surrounded by square brackets that have a colon and something after it.
    /// Example: given [23]:sometext as input, it will attempt to capture "23"
    /// </summary>
    private static readonly Regex ReferenceIdRegex = new("(?<=^\\[)\\d+(?=\\]:.+)", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex LinkRegex = new(@"\((https?://\S+|www\.\S+)\)", RegexOptions.Compiled);

    private readonly BuildAssetRegistryContext _context;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IBasicBarClient _barClient;
    private readonly ILogger<PullRequestBuilder> _logger;

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
        TargetRepoDependencyUpdates requiredUpdates,
        string? currentDescription,
        string targetRepository,
        string newBranchName)
    {
        StringBuilder description = new StringBuilder(currentDescription ?? "This pull request updates the following dependencies")
            .AppendLine()
            .AppendLine();
        var startingReferenceId = GetStartingReferenceId(description.ToString());
        var locationResolver = new AssetLocationResolver(_barClient);
        IRemote remote = await _remoteFactory.CreateRemoteAsync(targetRepository);
        var update = requiredUpdates.SubscriptionUpdate;
        var build = await _barClient.GetBuildAsync(update.BuildId);

        StringBuilder nonCoherencyCommitMessage = new();
        StringBuilder coherencyCommitMessage = new();
        List<GitFile> updatedDependencies = [];
        Dictionary<UnixPath, List<DependencyUpdate>> nonCoherencyUpdatesPerDirectory = [];
        Dictionary<UnixPath, List<DependencyUpdate>> coherencyUpdatesPerDirectory = [];

        // Go through each target directory and get the updated git files
        foreach (var (targetDirectory, targetRepoDirectoryUpdates) in requiredUpdates.DirectoryUpdates)
        {
            List<GitFile> targetDirectoryUpdatedDependencies = [];
            var nonCoherencyUpdates = targetRepoDirectoryUpdates.NonCoherencyUpdates;
            var coherencyUpdates = targetRepoDirectoryUpdates.CoherencyUpdates;

            List<DependencyDetail> itemsToUpdate = [];

            if (nonCoherencyUpdates.Count > 0)
            {
                AppendNonCoherencyCommitMessage(targetDirectory, nonCoherencyUpdates, nonCoherencyCommitMessage);
                nonCoherencyUpdatesPerDirectory[targetDirectory] = [.. nonCoherencyUpdates];
                itemsToUpdate.AddRange(nonCoherencyUpdates
                    .Select(du => du.To));
            }           

            if (coherencyUpdates != null && coherencyUpdates.Count > 0)
            {
                AppendCoherencyCommitMessage(targetDirectory, coherencyUpdates, coherencyCommitMessage);
                coherencyUpdatesPerDirectory[targetDirectory] = [.. coherencyUpdates];
                itemsToUpdate.AddRange(coherencyUpdates
                    .Select(du => du.To));
            }

            if (itemsToUpdate.Count > 0)
            {
                await locationResolver.AddAssetLocationToDependenciesAsync(itemsToUpdate);
                targetDirectoryUpdatedDependencies = await remote.GetUpdatedDependencyFiles(
                        targetRepository,
                        newBranchName,
                        itemsToUpdate,
                        targetDirectory);
                updatedDependencies.AddRange(targetDirectoryUpdatedDependencies);
            }
        }

        if (updatedDependencies.Count > 0)
        {
            if (nonCoherencyCommitMessage.Length > 0)
            {
                nonCoherencyCommitMessage.Insert(0, $"Update dependencies from {update.SourceRepo} build {build.AzureDevOpsBuildNumber}" + Environment.NewLine);
                nonCoherencyCommitMessage.AppendLine();
            }
            if (coherencyCommitMessage.Length > 0)
            {
                coherencyCommitMessage.Insert(0, "Dependency coherency updates" + Environment.NewLine);
                coherencyCommitMessage.AppendLine();
            }
            await remote.CommitUpdatesAsync(
                updatedDependencies,
                targetRepository,
                newBranchName,
                nonCoherencyCommitMessage.Append(coherencyCommitMessage).ToString());

            if (coherencyUpdatesPerDirectory.Count > 0)
            {
                AppendCoherencyUpdateDescription(description, coherencyUpdatesPerDirectory);
            }
            if (nonCoherencyUpdatesPerDirectory.Count > 0)
            {
                await AppendBuildDescriptionAsync(
                    description,
                    startingReferenceId,
                    requiredUpdates.SubscriptionUpdate,
                    nonCoherencyUpdatesPerDirectory,
                    updatedDependencies,
                    build);
            }
            return description.ToString();
        }
        else
        {
            // If the coherency algorithm failed and there are no non-coherency updates and
            // we create an empty commit that describes an issue.
            var message = "Failed to perform coherency update for one or more dependencies.";
            await remote.CommitUpdatesAsync(filesToCommit: [], targetRepository, newBranchName, message);
            return $"Coherency update: {message} Please review the GitHub checks or run `darc update-dependencies --coherency-only` locally against {newBranchName} for more information.";
        }

    }

    public string GenerateCodeFlowPRTitle(
        string targetBranch,
        List<string> repoNames)
    {
        return GeneratePRTitle($"[{targetBranch}] Source code updates from", repoNames);
    }

    public async Task<string> GenerateCodeFlowPRDescription(
        BuildDTO build,
        Subscription subscription,
        string headBranch,
        string? previousSourceCommit,
        List<DependencyUpdateSummary> dependencyUpdates,
        IReadOnlyCollection<UpstreamRepoDiff>? upstreamRepoDiffs,
        string? currentDescription)
    {
        string description = await GenerateCodeFlowPRDescriptionInternal(
            subscription,
            build,
            previousSourceCommit,
            dependencyUpdates,
            currentDescription);

        description = CompressRepeatedLinksInDescription(description);

        return AddOrUpdateFooterInDescription(build, subscription, headBranch, description, upstreamRepoDiffs);
    }

    private async Task<string> GenerateCodeFlowPRDescriptionInternal(
        Subscription subscription,
        BuildDTO build,
        string? previousSourceCommit,
        List<DependencyUpdateSummary> dependencyUpdates,
        string? currentDescription)
    {
        if (string.IsNullOrEmpty(currentDescription))
        {
            // if PR is new, create the new subscription update section along with the PR header
            return $"""
                
                > [!NOTE]
                > This is a codeflow update. It may contain both source code changes from
                > [{(subscription.IsForwardFlow() ? "the source repo" : "the VMR")}]({build.GetRepository()})
                > as well as dependency updates. Learn more [here]({CodeFlowPrFaqUri}).

                This pull request brings the following source code changes
                {await GenerateCodeFlowDescriptionForSubscription(subscription.Id, previousSourceCommit, build, dependencyUpdates)}
                """;
        }
        else
        {
            // if PR description already exists, update only the section relevant to the current subscription
            int startIndex = currentDescription.IndexOf(GetStartMarker(subscription.Id));
            int endIndex = currentDescription.IndexOf(GetEndMarker(subscription.Id));

            int startCutoff = startIndex == -1 ?
                currentDescription.Length :
                startIndex;
            int endCutoff = endIndex == -1 ?
                currentDescription.Length :
                endIndex + GetEndMarker(subscription.Id).Length;

            var beforeSpan = currentDescription.Substring(0, startCutoff);
            var afterSpan = currentDescription.Substring(endCutoff);
            var generatedDescription = await GenerateCodeFlowDescriptionForSubscription(
                subscription.Id,
                previousSourceCommit,
                build,
                dependencyUpdates);

            return string.Concat(beforeSpan, generatedDescription, afterSpan);
        }
    }

    private static string AddOrUpdateFooterInDescription(
        BuildDTO build,
        Subscription subscription,
        string headBranch,
        string description,
        IReadOnlyCollection<UpstreamRepoDiff>? upstreamRepoDiffs)
    {
        int footerStartIndex = description.IndexOf(FooterStartMarker);
        int footerEndIndex = description.IndexOf(FooterEndMarker);

        // Remove footer if exists
        if (footerStartIndex != -1 && footerEndIndex != -1)
        {
            description = description.Remove(footerStartIndex, footerEndIndex - footerStartIndex + FooterEndMarker.Length);
        }

        var footerBuilder = new StringBuilder();
        footerBuilder.AppendLine(FooterStartMarker);
        footerBuilder.AppendLine();

        var upstreamRepoDiffsSection = GenerateUpstreamRepoDiffsSection(upstreamRepoDiffs ?? []);
        if (!string.IsNullOrEmpty(upstreamRepoDiffsSection))
        {
            footerBuilder.AppendLine(upstreamRepoDiffsSection);
        }

        footerBuilder.AppendLine(GenerateDarcDiffHelpSection(build, subscription.TargetRepository, headBranch));
        footerBuilder.Append(FooterEndMarker);

        description += footerBuilder.ToString();
        return description;
    }

    private static string GenerateUpstreamRepoDiffsSection(IReadOnlyCollection<UpstreamRepoDiff> upstreamRepoDiffs)
    {
        if (upstreamRepoDiffs.Count == 0)
        {
            return string.Empty;
        }
        
        StringBuilder sb = new();
        sb.AppendLine("## Associated changes in source repos");
        foreach (UpstreamRepoDiff upstreamRepoDiff in upstreamRepoDiffs
            .Where(repoDiff => !string.IsNullOrEmpty(repoDiff.RepoUri)
                && !string.IsNullOrEmpty(repoDiff.OldCommitSha)
                && !string.IsNullOrEmpty(repoDiff.NewCommitSha)))
        {
            string cleanRepoUri = upstreamRepoDiff.RepoUri.TrimEnd('/');
            sb.AppendLine($"- {cleanRepoUri}/compare/{upstreamRepoDiff.OldCommitSha}...{upstreamRepoDiff.NewCommitSha}");
        }
        return sb.ToString();
    }

    private static string GenerateDarcDiffHelpSection(BuildDTO build, string targetRepository, string headBranch) =>
        $"""
        <details>
        <summary>Diff the source with this PR branch</summary>

        ```bash
        darc vmr diff --name-only {build.GetRepository()}:{build.Commit}..{targetRepository}:{headBranch}
        ```
        </details>

        """;

    private async Task<string> GenerateCodeFlowDescriptionForSubscription(
        Guid subscriptionId,
        string? previousSourceCommit,
        BuildDTO build,
        List<DependencyUpdateSummary> dependencyUpdates)
    {
        var sourceRepoUri = build.GetRepository();
        var sourceBranch = build.GetBranch();

        string sourceDiffText = CreateSourceDiffLink(build, previousSourceCommit);
        string dependencyUpdateBlock = CreateDependencyUpdateBlock(dependencyUpdates, sourceRepoUri);
        return
            $"""

            {GetStartMarker(subscriptionId)}
            
            ## From {sourceRepoUri}
            - **Subscription**: {GetSubscriptionLink(subscriptionId)}
            - **Build**: {await GetBuildLinkAsync(build, subscriptionId)}
            - **Date Produced**: {build.DateProduced.ToUniversalTime():MMMM d, yyyy h:mm:ss tt UTC}
            - **Commit**: [{build.Commit}]({GitRepoUrlUtils.GetCommitUri(sourceRepoUri, build.Commit)})
            - **Commit Diff**: {sourceDiffText}
            - **Branch**: [{sourceBranch}]({GitRepoUrlUtils.GetRepoAtBranchUri(sourceRepoUri, sourceBranch)})
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

        StringBuilder stringBuilder = new();

        // Group all dependencies by FromCommitSha, FromVersion, ToCommitSha, and ToVersion
        var dependencyGroups = dependencyUpdateSummaries
            .GroupBy(dep => new
            {
                FromCommitSha = dep.FromCommitSha,
                FromVersion = dep.FromVersion,
                ToCommitSha = dep.ToCommitSha,
                ToVersion = dep.ToVersion
            })
            .ToList();

        // Separate groups by dependency type
        var newDependencyGroups = dependencyGroups.Where(g => string.IsNullOrEmpty(g.Key.FromVersion) && !string.IsNullOrEmpty(g.Key.ToVersion)).ToList();
        var removedDependencyGroups = dependencyGroups.Where(g => string.IsNullOrEmpty(g.Key.ToVersion) && !string.IsNullOrEmpty(g.Key.FromVersion)).ToList();
        var updatedDependencyGroups = dependencyGroups.Where(g => !string.IsNullOrEmpty(g.Key.FromVersion) && !string.IsNullOrEmpty(g.Key.ToVersion)).ToList();

        if (newDependencyGroups.Count > 0)
        {
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("**New Dependencies**");
            foreach (var group in newDependencyGroups)
            {
                var representative = group.First();
                string? diffLink = GetLinkForDependencyItem(repoUri, representative.FromCommitSha, representative.ToCommitSha);
                
                stringBuilder.AppendLine($"- Added [{representative.ToVersion}]({diffLink})");
                foreach (var dep in group.OrderBy(dep => dep.DependencyName))
                {
                    stringBuilder.AppendLine($"  - {dep.DependencyName}");
                }
            }
        }

        if (removedDependencyGroups.Count > 0)
        {
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("**Removed Dependencies**");
            foreach (var group in removedDependencyGroups)
            {
                var representative = group.First();
                
                stringBuilder.AppendLine($"- Removed {representative.FromVersion}");
                foreach (var dep in group.OrderBy(dep => dep.DependencyName))
                {
                    stringBuilder.AppendLine($"  - {dep.DependencyName}");
                }
            }
        }

        if (updatedDependencyGroups.Count > 0)
        {
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("**Updated Dependencies**");

            foreach (var group in updatedDependencyGroups)
            {
                var representative = group.First();
                string? diffLink = GetLinkForDependencyItem(repoUri, representative.FromCommitSha, representative.ToCommitSha);
                
                stringBuilder.AppendLine($"- From [{representative.FromVersion} to {representative.ToVersion}]({diffLink})");
                foreach (var dep in group.OrderBy(dep => dep.DependencyName))
                {
                    stringBuilder.AppendLine($"  - {dep.DependencyName}");
                }
            }
        }
        return stringBuilder.ToString();
    }



    private static string? GetLinkForDependencyItem(string repoUri, string? fromCommitSha, string? toCommitSha)
    {
        if (!string.IsNullOrEmpty(fromCommitSha) && !string.IsNullOrEmpty(toCommitSha))
        {
            return GetChangesURI(repoUri, fromCommitSha, toCommitSha);
        }

        if (!string.IsNullOrEmpty(toCommitSha))
        {
            return GetCommitURI(repoUri, toCommitSha);
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

        string sourceDiffText = $"{Microsoft.DotNet.DarcLib.Commit.GetShortSha(previousSourceCommit)}...{Microsoft.DotNet.DarcLib.Commit.GetShortSha(build.Commit)}";

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
    private static string CompressRepeatedLinksInDescription(string description)
    {
        List<string> matches = LinkRegex.Matches(description)
            .Select(m => m.Value)
            .ToList();

        Dictionary<string, int> linkGroups = matches
            .GroupBy(link => link)
            .Where(group => group.Count() >= 2)
            .Select((group, index) => new { Link = group.Key, Index = index })
            .ToDictionary(x => x.Link, x => x.Index);

        if (linkGroups.Count == 0)
        {
            return description;
        }

        var existingGroupCount = GetStartingReferenceId(description);

        foreach (var entry in linkGroups)
        {
            description = Regex.Replace(description, $"{Regex.Escape(entry.Key)}", $"[{entry.Value + existingGroupCount}]");
        }

        StringBuilder linkReferencesSection = new();
        linkReferencesSection.AppendLine();

        foreach (var entry in linkGroups)
        {
            linkReferencesSection.AppendLine($"[{entry.Value + existingGroupCount}]: {entry.Key.TrimStart('(').TrimEnd(')')}");
        }

        return description + linkReferencesSection.ToString();
    }

    /// <summary>
    ///     Append build description to the PR description
    /// </summary>
    /// <param name="description">Description to extend</param>
    /// <param name="startingReferenceId">Counter for references</param>
    /// <param name="update">Update</param>
    /// <param name="updatedDependenciesPerPath">Dependencies updated, per relative path</param>
    /// <param name="committedFiles">List of commited files</param>
    /// <param name="build">Build</param>
    /// <remarks>
    ///     Because PRs tend to be live for short periods of time, we can put more information
    ///     in the description than the commit message without worrying that links will go stale.
    /// </remarks>
    private async Task AppendBuildDescriptionAsync(
        StringBuilder description,
        int startingReferenceId,
        SubscriptionUpdateWorkItem update,
        Dictionary<UnixPath, List<DependencyUpdate>> updatedDependenciesPerPath,
        List<GitFile> committedFiles,
        BuildDTO build)
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
            .AppendLine($"- **Build**: {await GetBuildLinkAsync(build, update.SubscriptionId)}")
            .AppendLine($"- **Date Produced**: {build.DateProduced.ToUniversalTime():MMMM d, yyyy h:mm:ss tt UTC}")
            // This is duplicated from the files changed, but is easier to read here.
            .AppendLine($"- **Commit**: [{build.Commit}]({GitRepoUrlUtils.GetCommitUri(build.GetRepository(), build.Commit)})");

        var branch = build.GetBranch();
        if (!string.IsNullOrEmpty(branch))
        {
            subscriptionSection.AppendLine($"- **Branch**: [{branch}]({GitRepoUrlUtils.GetRepoAtBranchUri(build.GetRepository(), branch)})");
        }

        subscriptionSection
            .AppendLine()
            .AppendLine(DependencyUpdateBegin)
            .AppendLine()
            .AppendLine($"- **Dependency Updates**:");

        var shaRangeToLinkId = new Dictionary<(string from, string to), int>();

        bool shouldAddDirectory = true;
        string padding = "  ";
        if (updatedDependenciesPerPath.Keys.Count == 1 && updatedDependenciesPerPath.Keys.First() == UnixPath.Empty)
        {
            shouldAddDirectory = false;
            padding = string.Empty;
        }
        foreach (var (relativePath, updatedDependencies) in updatedDependenciesPerPath)
        {
            if (shouldAddDirectory)
            {
                subscriptionSection.AppendLine($"  - 📂 `{(UnixPath.IsEmptyPath(relativePath) ? "root" : relativePath)}`");
            }
            // Group dependencies by version range and commit range
            var dependencyGroups = updatedDependencies
                .GroupBy(dep => new
                {
                    FromVersion = dep.From.Version,
                    ToVersion = dep.To.Version,
                    FromCommit = dep.From.Commit,
                    ToCommit = dep.To.Commit,
                    RepoUri = dep.To.RepoUri
                })
                .ToList();

            foreach (var group in dependencyGroups)
            {
                var representative = group.First();

                if (!shaRangeToLinkId.ContainsKey((representative.From.Commit, representative.To.Commit)))
                {
                    var changesUri = string.Empty;
                    try
                    {
                        changesUri = GetChangesURI(representative.To.RepoUri, representative.From.Commit, representative.To.Commit);
                    }
                    catch (ArgumentNullException e)
                    {
                        _logger.LogError(e, $"Failed to create SHA comparison link for dependency {representative.To.Name} during asset update for subscription {update.SubscriptionId}");
                    }
                    shaRangeToLinkId.Add((representative.From.Commit, representative.To.Commit), startingReferenceId + changesLinks.Count);
                    changesLinks.Add(changesUri);
                }

                // Write the group header with version range and link
                subscriptionSection.AppendLine($"  {padding}- From [{representative.From.Version} to {representative.To.Version}][{shaRangeToLinkId[(representative.From.Commit, representative.To.Commit)]}]");

                // Write each dependency in the group
                foreach (var dep in group)
                {
                    subscriptionSection.Append($"     {padding}- ");
                    subscriptionSection.AppendLine(dep.To.Name);
                }
            }
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
    }

    /// <summary>
    ///     Append coherency update description to the PR description
    /// </summary>
    /// <param name="description">Description to extend</param>
    /// <param name="coherencyUpdatesPerDirectory">Dependencies updated, per directory</param>
    /// <remarks>
    ///     Because PRs tend to be live for short periods of time, we can put more information
    ///     in the description than the commit message without worrying that links will go stale.
    /// </remarks>
    private static void AppendCoherencyUpdateDescription(
        StringBuilder description,
        Dictionary<UnixPath, List<DependencyUpdate>> coherencyUpdatesPerDirectory)
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

        bool shouldAddDirectory = true;
        string padding = string.Empty;
        if (coherencyUpdatesPerDirectory.Keys.Count == 1 && coherencyUpdatesPerDirectory.Keys.First() == UnixPath.Empty)
        {
            shouldAddDirectory = false;
            padding = "  ";
        }
        foreach (var (targetDirectory, dependencies) in coherencyUpdatesPerDirectory)
        {
            if (shouldAddDirectory)
            {
                coherencySection.AppendLine($"  - 📂 `{(UnixPath.IsEmptyPath(targetDirectory) ? "root" : targetDirectory)}`");
            }
            foreach (DependencyUpdate dep in dependencies)
            {
                coherencySection.AppendLine($"  {padding}- **{dep.To.Name}**: from {dep.From.Version} to {dep.To.Version} (parent: {dep.To.CoherentParentDependencyName})");
            }
        }

        coherencySection
            .AppendLine()
            .AppendLine(DependencyUpdateEnd)
            .AppendLine()
            .AppendLine(sectionEndMarker);

        description.Insert(sectionStartIndex, coherencySection.ToString());
        description.AppendLine();
    }

    private static void AppendNonCoherencyCommitMessage(string relativeBasePath, List<DependencyUpdate> deps, StringBuilder message)
    {
        message.AppendLine($"On relative base path {(UnixPath.IsEmptyPath(relativeBasePath) ? "root" : relativeBasePath)}");
        
        // Group dependencies by their version changes
        var versionGroups = deps
            .GroupBy(dep => $"From Version {dep.From.Version} -> To Version {dep.To.Version}")
            .ToList();

        foreach (var group in versionGroups)
        {
            message.AppendLine($"{string.Join(" , ", group.Select(p => p.To.Name))} {group.Key}");
        }

        message.AppendLine();
    }

    private static void AppendCoherencyCommitMessage(string relativeBasePath, List<DependencyUpdate> deps, StringBuilder message)
    {
        message.AppendLine($"On relative base path {(UnixPath.IsEmptyPath(relativeBasePath) ? "root" : relativeBasePath)}");
        
        // Group dependencies by their version changes
        var versionGroups = deps
            .GroupBy(dep => $"From Version {dep.From.Version} -> To Version {dep.To.Version} (parent: {dep.To.CoherentParentDependencyName})")
            .ToList();

        foreach (var group in versionGroups)
        {
            message.AppendLine($"{string.Join(",", group.Select(p => p.To.Name))} {group.Key}");
            message.AppendLine();
        }

        message.AppendLine();
    }

    private static void UpdatePRDescriptionDueConfigFiles(List<GitFile> committedFiles, StringBuilder globalJsonSection)
    {
        List<GitFile> globalJsonFiles = committedFiles
            .Where(gf => gf.FilePath.Contains("global.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // The list of committedFiles can contain the `global.json` file (and others) 
        // even though no actual change was made to the file and therefore there is no 
        // metadata for it.
        var globalJsonFilesWithMetadata = globalJsonFiles
            .Where(gf => gf.Metadata != null)
            .ToList();

        if (globalJsonFilesWithMetadata.Count == 0)
        {
            return;
        }

        // Capture all changes first
        var configFileChanges = new List<ConfigFileChange>();

        foreach (var globalJsonFile in globalJsonFilesWithMetadata)
        {
            var hasSdkVersionUpdate = globalJsonFile.Metadata.ContainsKey(GitFileMetadataName.SdkVersionUpdate);
            var hasToolsDotnetUpdate = globalJsonFile.Metadata.ContainsKey(GitFileMetadataName.ToolsDotNetUpdate);
            var relativeBasePath = globalJsonFile.FilePath.Replace("global.json", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(relativeBasePath))
            {
                relativeBasePath = "root";
            }

            if (hasSdkVersionUpdate)
            {
                configFileChanges.Add(new ConfigFileChange
                {
                    DirectoryPath = relativeBasePath,
                    UpdateType = "sdk.version",
                    ToValue = globalJsonFile.Metadata[GitFileMetadataName.SdkVersionUpdate]
                });
            }

            if (hasToolsDotnetUpdate)
            {
                configFileChanges.Add(new ConfigFileChange
                {
                    DirectoryPath = relativeBasePath,
                    UpdateType = "tools.dotnet",
                    ToValue = globalJsonFile.Metadata[GitFileMetadataName.ToolsDotNetUpdate]
                });
            }
        }

        // If there are any changes, format them according to the requested structure
        if (configFileChanges.Count > 0)
        {
            // Group changes by directory
            var changesByDirectory = configFileChanges
                .GroupBy(c => c.DirectoryPath)
                .OrderBy(g => g.Key == "root" ? "" : g.Key); // Put root first

            // Special case: if there are only updates in the root directory, use simplified format
            if (changesByDirectory.Count() == 1 && changesByDirectory.First().Key == "root")
            {
                globalJsonSection.AppendLine("- **Updates to .NET SDKs:**");
                
                foreach (var change in changesByDirectory.First().OrderBy(c => c.UpdateType))
                {
                    globalJsonSection.AppendLine($"  - Updates **{change.UpdateType}** to {change.ToValue}");
                }
            }
            else
            {
                globalJsonSection.AppendLine("- **Updates to .NET SDKs:**");

                
                foreach (var directoryGroup in changesByDirectory)
                {
                    globalJsonSection.AppendLine($"  - 📂 `{directoryGroup.Key.TrimEnd('/')}`");

                    foreach (var change in directoryGroup.OrderBy(c => c.UpdateType))
                    {
                        globalJsonSection.AppendLine($"    - Updates **{change.UpdateType}** to {change.ToValue}");
                    }
                }
            }
        }
    }
    
    private class ConfigFileChange
    {
        public string DirectoryPath { get; set; } = string.Empty;
        public string UpdateType { get; set; } = string.Empty;
        public string ToValue { get; set; } = string.Empty;
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
        return ReferenceIdRegex.Matches(description.ToString())
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

        if (repoNames == null || repoNames.Count == 0)
        {
            return string.Empty;
        }

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

    /// <summary>
    /// Generates a build link that to the Azure DevOps build and tries to add a BAR build link.
    /// </summary>
    /// <param name="build">The build object containing build information</param>
    /// <param name="subscriptionId">The subscription ID to get channel information</param>
    /// <returns>Enhanced build link string with BAR build details</returns>
    private async Task<string> GetBuildLinkAsync(BuildDTO build, Guid subscriptionId)
    {
        var originalBuildLink = $"[{build.AzureDevOpsBuildNumber}]({build.GetBuildLink()})";
        
        try
        {
            int? channelId;
            if (build.Channels.Count == 1)
            {
                channelId = build.Channels[0].Id;
            }
            else
            {

                // Get the subscription to retrieve the channel ID
                var subscription = await _context.Subscriptions
                    .Where(s => s.Id == subscriptionId)
                    .Select(s => new { s.ChannelId })
                    .FirstOrDefaultAsync();

                if (subscription == null)
                {
                    // If the subscription is not found, return the original link
                    return originalBuildLink;
                }

                channelId = subscription.ChannelId;
            }
            
            // Generate repository slug for BarViz URL
            var repoSlug = ConvertRepoUrlToSlug(build.GetRepository());
            if (string.IsNullOrEmpty(repoSlug))
            {
                return originalBuildLink;
            }
            
            // Create the BarViz link
            var barBuildLink = $"https://maestro.dot.net/channel/{channelId}/{repoSlug}/build/{build.Id}";
            return $"{originalBuildLink} ([{build.Id}]({barBuildLink}))";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate enhanced build link for build {BuildId} and subscription {SubscriptionId}", build.Id, subscriptionId);
            return originalBuildLink;
        }
    }

    /// <summary>
    /// Converts a repository URL to a slug format used by BarViz.
    /// </summary>
    /// <param name="repoUrl">The repository URL</param>
    /// <returns>Repository slug in format github:org:repo or azdo:org:project:repo</returns>
    private static string? ConvertRepoUrlToSlug(string? repoUrl)
    {
        if (repoUrl == null)
        {
            return null;
        }

        var repoType = GitRepoUrlUtils.ParseTypeFromUri(repoUrl);
        
        switch (repoType)
        {
            case GitRepoType.GitHub:
                var (repoName, org) = GitRepoUrlUtils.GetRepoNameAndOwner(repoUrl);
                return $"github:{org}:{repoName}";
                
            case GitRepoType.AzureDevOps:
                // For Azure DevOps, we need to extract the project name from the URL
                // Format: https://dev.azure.com/{org}/{project}/_git/{repo}
                const string azureDevOpsPrefix = "https://dev.azure.com/";
                if (repoUrl.StartsWith(azureDevOpsPrefix))
                {
                    string[] urlParts = repoUrl.Substring(azureDevOpsPrefix.Length).Split('/');
                    if (urlParts.Length >= 4 && urlParts[2] == "_git")
                    {
                        string orgName = urlParts[0];
                        string projectName = urlParts[1];
                        string repoNamePart = urlParts[3].Split('?')[0]; // Remove query parameters
                        return $"azdo:{orgName}:{projectName}:{repoNamePart}";
                    }
                }
                break;
        }

        return null;
    }
}


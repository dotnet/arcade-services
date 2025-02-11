// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.Extensions.Logging;
using ProductConstructionService.DependencyFlow.WorkItems;

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
    /// <param name="remoteFactory">Remote factory for generating remotes based on repo uri</param>
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
    /// <param name="inProgressPr">Current in progress pull request information</param>
    /// <returns>Pull request title</returns>
    Task<string> GeneratePRTitleAsync(
        List<SubscriptionPullRequestUpdate> subscriptions,
        string targetBranch);

    /// <summary>
    ///    Generate the title for a code flow PR.
    /// </summary>
    Task<string> GenerateCodeFlowPRTitleAsync(
        SubscriptionUpdateWorkItem update,
        string targetBranch);

    /// <summary>
    ///    Generate the description for a code flow PR.
    /// </summary>
    Task<string> GenerateCodeFlowPRDescriptionAsync(
        SubscriptionUpdateWorkItem update,
        string previousSourceCommit);
}

internal class PullRequestBuilder : IPullRequestBuilder
{
    public const int GitHubComparisonShaLength = 10;

    // PR description markers
    private const string DependencyUpdateBegin = "[DependencyUpdate]: <> (Begin)";
    private const string DependencyUpdateEnd = "[DependencyUpdate]: <> (End)";

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

        return await CreateTitleWithRepositories($"[{targetBranch}] Update dependencies from", uniqueSubscriptionIds);
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
                _remoteFactory,
                _barClient,
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
                _remoteFactory,
                _barClient,
                itemsToUpdate,
                message.ToString());
        }

        // If the coherency algorithm failed and there are no non-coherency updates and
        // we create an empty commit that describes an issue.
        if (requiredUpdates.Count == 0)
        {
            var message = "Failed to perform coherency update for one or more dependencies.";
            await remote.CommitUpdatesAsync(targetRepository, newBranchName, _remoteFactory, _barClient, [], message);
            return $"Coherency update: {message} Please review the GitHub checks or run `darc update-dependencies --coherency-only` locally against {newBranchName} for more information.";
        }

        return description.ToString();
    }

    public async Task<string> GenerateCodeFlowPRTitleAsync(
        SubscriptionUpdateWorkItem update,
        string targetBranch)
    {
        return await CreateTitleWithRepositories($"[{targetBranch}] Source code changes from ", [update.SubscriptionId]);
    }

    public async Task<string> GenerateCodeFlowPRDescriptionAsync(
        SubscriptionUpdateWorkItem update,
        string previousSourceCommit)
    {

        var build = await _barClient.GetBuildAsync(update.BuildId);

        string sourceDiffText;
        if (previousSourceCommit != null && !string.IsNullOrEmpty(build.GitHubRepository))
        {
            sourceDiffText = $"[View Source Diff]({build.GitHubRepository}/compare/{previousSourceCommit}..{build.Commit})";
        }
        else
        {
            sourceDiffText = "Not available";
        }

        return
            $"""
            {GetStartMarker(update.SubscriptionId)}

            This pull request is bringing source changes from **{update.SourceRepo}**.
            
            - **Subscription**: {update.SubscriptionId}
            - **Build**: [{build.AzureDevOpsBuildNumber}]({build.GetBuildLink()})
            - **Date Produced**: {build.DateProduced.ToUniversalTime():MMMM d, yyyy h:mm:ss tt UTC}
            - **Source Diff**: {sourceDiffText}
            - **Commit**: [{build.Commit}]({build.GetCommitLink()})
            - **Branch**: {build.GetBranch()}

            {GetEndMarker(update.SubscriptionId)}
            """;
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
            .AppendLine($"- **Subscription**: {updateSubscriptionId}")
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
    public static int GetStartingReferenceId(string description)
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

    public static string GetChangesURI(string repoURI, string fromSha, string toSha)
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

    private async Task<string?> GetSourceRepositoryAsync(Guid subscriptionId)
    {
        Subscription? subscription = await _context.Subscriptions.FindAsync(subscriptionId);
        return subscription?.SourceRepository;
    }

    /// <summary>
    /// Either inserts a full list of the repos involved (in a shortened form)
    /// or just the number of repos that are involved if title is too long.
    /// </summary>
    /// <param name="baseTitle">Start of the title to append the list to</param>
    private async Task<string> CreateTitleWithRepositories(string baseTitle, Guid[] subscriptionIds)
    {
        // Github title limit - 348 
        // Azdo title limit - 419 
        // maxTitleLength = 150 to fit 2/3 repo names in the title
        const int maxTitleLength = 150;
        var maxRepoListLength = maxTitleLength - baseTitle.Length;
        const string delimiter = ", ";

        var repoNames = new List<string>();
        var titleLength = 0;
        foreach (Guid subscriptionId in subscriptionIds)
        {
            var repoName = await GetSourceRepositoryAsync(subscriptionId);
            if (repoName == null)
            {
                continue;
            }

            // Strip down repo name.
            repoName = repoName
                .Replace("https://github.com/", null)
                .Replace("https://dev.azure.com/", null)
                .Replace("_git/", null);

            repoNames.Add(repoName);

            titleLength += repoName.Length + delimiter.Length;
            if (titleLength > maxRepoListLength)
            {
                return $"{baseTitle} {subscriptionIds.Length} repositories";
            }
        }

        return $"{baseTitle} {string.Join(delimiter, repoNames.OrderBy(s => s))}";
    }

    private static string GetStartMarker(Guid subscriptionId)
        => $"[marker]: <> (Begin:{subscriptionId})";

    private static string GetEndMarker(Guid subscriptionId)
        => $"[marker]: <> (End:{subscriptionId})";
}

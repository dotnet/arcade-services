// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using Maestro.Common;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// This class extends the local git client with operations that are hard to express without LibGit2Sharp.
/// </summary>
public class LocalLibGit2Client : LocalGitClient, ILocalLibGit2Client
{
    private readonly IRemoteTokenProvider _remoteTokenProvider;
    private readonly IProcessManager _processManager;
    private readonly ExponentialRetry _exponentialRetry;
    private readonly ILogger _logger;
    private readonly Dictionary<(string, string, string?), string> _gitRefCommitCache;

    public LocalLibGit2Client(
        IRemoteTokenProvider remoteTokenProvider,
        ITelemetryRecorder telemetryRecorder,
        IProcessManager processManager,
        IFileSystem fileSystem,
        ILogger logger)
        : base(remoteTokenProvider, telemetryRecorder, processManager, fileSystem, logger)
    {
        _remoteTokenProvider = remoteTokenProvider;
        _processManager = processManager;
        _exponentialRetry = new ExponentialRetry(Options.Create(new ExponentialRetryOptions
        {
            RetryCount = 3,
            RetryBackOffFactor = 1.3,
        }));
        _logger = logger;
        _gitRefCommitCache = [];
    }

    public async Task CommitFilesAsync(List<GitFile> filesToCommit, string repoPath, string branch, string commitMessage)
    {
        repoPath = await GetRootDirAsync(repoPath);
        try
        {
            using (var localRepo = new Repository(repoPath))
            {
                var filesToAdd = filesToCommit
                    .Where(f => f.Operation == GitFileOperation.Add)
                    .Select(f => f.FilePath)
                    .ToArray();

                foreach (var relativePath in filesToAdd)
                {
                    EnsureParentDirectoryExists(Path.Combine(repoPath, relativePath));
                }

                Dictionary<string, EolAttribute> eolByPath = await GetEolAttributesAsync(repoPath, filesToAdd);

                foreach (var file in filesToCommit)
                {
                    Debug.Assert(file != null, $"Passed in a null {nameof(GitFile)} in {nameof(filesToCommit)}");
                    switch (file.Operation)
                    {
                        case GitFileOperation.Add:
                        {
                            string fullPath = Path.Combine(repoPath, file.FilePath);
                            string finalContent;
                            switch (file.ContentEncoding)
                            {
                                case ContentEncoding.Utf8:
                                    finalContent = file.Content;
                                    break;
                                case ContentEncoding.Base64:
                                    byte[] bytes = Convert.FromBase64String(file.Content);
                                    finalContent = Encoding.UTF8.GetString(bytes);
                                    break;
                                default:
                                    throw new DarcException($"Unknown file content encoding {file.ContentEncoding}");
                            }

                            if (!eolByPath.TryGetValue(file.FilePath, out EolAttribute eolAttr))
                            {
                                throw new DarcException($"Could not find eol attribute for file {file.FilePath} in repo {repoPath}");
                            }

                            finalContent = NormalizeLineEndings(eolAttr, finalContent);
                            await File.WriteAllTextAsync(fullPath, finalContent);

                            AddFileToIndex(localRepo, file, fullPath);
                            break;
                        }
                        case GitFileOperation.Delete:
                            if (File.Exists(file.FilePath))
                            {
                                File.Delete(file.FilePath);
                            }
                            break;
                    }
                }
            }
        }
        catch (Exception exc)
        {
            throw new DarcException($"Something went wrong when checking out {repoPath} in {repoPath}", exc);
        }
    }

    private enum EolAttribute
    {
        UnspecifiedOrAuto,
        Crlf,
        Lf,
    }

    private static void EnsureParentDirectoryExists(string fullPath)
    {
        string? parentDirectory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parentDirectory))
        {
            return;
        }

        Directory.CreateDirectory(parentDirectory);
    }

    private static string NormalizeLineEndings(EolAttribute eolAttr, string content)
    {
        const string crlf = "\r\n";
        const string lf = "\n";

        if (eolAttr == EolAttribute.UnspecifiedOrAuto)
        {
            if (Environment.NewLine != crlf)
            {
                return content.Replace(crlf, Environment.NewLine);
            }
            else if (Environment.NewLine == crlf && !content.Contains(crlf))
            {
                return content.Replace(lf, Environment.NewLine);
            }
        }
        else if (eolAttr == EolAttribute.Crlf)
        {
            // Test to avoid adding extra \r.
            if (!content.Contains(crlf))
            {
                return content.Replace(lf, crlf);
            }
        }
        else if (eolAttr == EolAttribute.Lf)
        {
            return content.Replace(crlf, lf);
        }

        return content;
    }

    private async Task<Dictionary<string, EolAttribute>> GetEolAttributesAsync(string repoPath, IEnumerable<string> relativePaths)
    {
        // `git check-attr --stdin` expects NUL-delimited paths by default.
        string[] paths = relativePaths
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (paths.Length == 0)
        {
            return new Dictionary<string, EolAttribute>(StringComparer.Ordinal);
        }

        string stdin = string.Join("\0", paths) + "\0";
        var result = await _processManager.ExecuteGit(repoPath, ["check-attr", "eol", "--stdin", "-z"], stdin);
        result.ThrowIfFailed("Failed to determine eol attributes");

        return ParseEolCheckAttrOutput(result.StandardOutput);
    }

    private static Dictionary<string, EolAttribute> ParseEolCheckAttrOutput(string standardOutput)
    {
        // Output format with -z: <path>\0<attr>\0<value>\0
        // Example value is "unspecified", "auto", "crlf", "lf", or "unset".
        var tokens = standardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var map = new Dictionary<string, EolAttribute>(StringComparer.Ordinal);

        for (int i = 0; i + 2 < tokens.Length; i += 3)
        {
            string path = tokens[i];
            string value = tokens[i + 2];

            map[path] = value switch
            {
                "crlf" => EolAttribute.Crlf,
                "lf" => EolAttribute.Lf,
                "auto" => EolAttribute.UnspecifiedOrAuto,
                "unspecified" => EolAttribute.UnspecifiedOrAuto,
                "unset" => EolAttribute.UnspecifiedOrAuto,
                _ => EolAttribute.UnspecifiedOrAuto,
            };
        }

        return map;
    }

    /// <summary>
    ///     Checkout the repo to the specified state.
    /// </summary>
    /// <param name="commit">Tag, branch, or commit to checkout.</param>
    public void Checkout(string repoPath, string commit, bool force = false)
    {
        _logger.LogDebug($"Checking out {commit}", commit ?? "default commit");
        var checkoutOptions = new CheckoutOptions
        {
            CheckoutModifiers = force ? CheckoutModifiers.Force : CheckoutModifiers.None,
        };
        try
        {
            _logger.LogDebug($"Reading local repo from {repoPath}");
            using (var localRepo = new Repository(repoPath))
            {
                if (commit == null)
                {
                    commit = localRepo.Head.Reference.TargetIdentifier;
                    _logger.LogInformation($"Repo {localRepo.Info.WorkingDirectory} default commit to checkout is {commit}");
                }
                try
                {
                    _logger.LogDebug($"Attempting to check out {commit} in {repoPath}");
                    SafeCheckout(localRepo, commit, checkoutOptions);
                    if (force)
                    {
                        CleanRepoAndSubmodules(localRepo);
                    }
                }
                catch (NotFoundException)
                {
                    _logger.LogWarning($"Couldn't find commit {commit} in {repoPath} locally.  Attempting fetch.");
                    try
                    {
                        foreach (LibGit2Sharp.Remote r in localRepo.Network.Remotes)
                        {
                            IEnumerable<string> refSpecs = r.FetchRefSpecs.Select(x => x.Specification);
                            _logger.LogDebug($"Fetching {string.Join(";", refSpecs)} from {r.Url} in {repoPath}");
                            try
                            {
                                Commands.Fetch(localRepo, r.Name, refSpecs, new FetchOptions(), $"Fetching from {r.Url}");
                            }
                            catch
                            {
                                _logger.LogWarning($"Fetching failed, are you offline or missing a remote?");
                            }
                        }
                        _logger.LogDebug($"After fetch, attempting to checkout {commit} in {repoPath}");
                        SafeCheckout(localRepo, commit, checkoutOptions);

                        if (force)
                        {
                            CleanRepoAndSubmodules(localRepo);
                        }
                    }
                    catch   // Most likely network exception, could also be no remotes.  We can't do anything about any error here.
                    {
                        _logger.LogError($"After fetch, still couldn't find commit or treeish {commit} in {repoPath}.  Are you offline or missing a remote?");
                        throw;
                    }
                }
            }
        }
        catch (Exception exc)
        {
            throw new Exception($"Something went wrong when checking out {commit} in {repoPath}", exc);
        }
    }

    private void CleanRepoAndSubmodules(Repository repo)
    {
        using (_logger.BeginScope($"Beginning clean of {repo.Info.WorkingDirectory} and {repo.Submodules.Count()} submodules"))
        {
            _logger.LogDebug($"Beginning clean of {repo.Info.WorkingDirectory} and {repo.Submodules.Count()} submodules");
            var options = new StatusOptions
            {
                IncludeUntracked = true,
                RecurseUntrackedDirs = true,
            };
            var count = 0;
            foreach (StatusEntry item in repo.RetrieveStatus(options))
            {
                if (item.State == FileStatus.NewInWorkdir)
                {
                    File.Delete(Path.Combine(repo.Info.WorkingDirectory, item.FilePath));
                    ++count;
                }
            }
            _logger.LogDebug($"Deleted {count} untracked files");

            foreach (Submodule sub in repo.Submodules)
            {
                string normalizedSubPath = sub.Path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                string subRepoPath = Path.Combine(repo.Info.WorkingDirectory, normalizedSubPath);
                string subRepoGitFilePath = Path.Combine(subRepoPath, ".git");
                if (!File.Exists(subRepoGitFilePath))
                {
                    _logger.LogDebug($"Submodule {sub.Name} in {subRepoPath} does not appear to be initialized (no file at {subRepoGitFilePath}), attempting to initialize now.");
                    // hasn't been initialized yet, can happen when different hashes have new or moved submodules
                    try
                    {
                        repo.Submodules.Update(sub.Name, new SubmoduleUpdateOptions { Init = true });
                    }
                    catch
                    {
                        _logger.LogDebug($"Submodule {sub.Name} in {subRepoPath} is already initialized, trying to adopt from super-repo {repo.Info.Path}");

                        // superrepo thinks it is initialized, but it's orphaned.  Go back to the master repo to find out where this is supposed to point.
                        using (var masterRepo = new Repository(repo.Info.WorkingDirectory))
                        {
                            Submodule masterSubModule = masterRepo.Submodules.Single(s => s.Name == sub.Name);
                            string masterSubPath = Path.Combine(repo.Info.Path, "modules", masterSubModule.Path);
                            _logger.LogDebug($"Writing .gitdir redirect {masterSubPath} to {subRepoGitFilePath}");
                            Directory.CreateDirectory(Path.GetDirectoryName(subRepoGitFilePath) ?? throw new Exception($"Cannot get directory name of {subRepoGitFilePath}"));
                            File.WriteAllText(subRepoGitFilePath, $"gitdir: {masterSubPath}");
                        }
                    }
                }

                using (_logger.BeginScope($"Beginning clean of submodule {sub.Name}"))
                {
                    _logger.LogDebug($"Beginning clean of submodule {sub.Name} in {subRepoPath}");

                    // The worktree is stored in the .gitdir/config file, so we have to change it
                    // to get it to check out to the correct place.
                    ConfigurationEntry<string>? oldWorkTree = null;
                    using (var subRepo = new Repository(subRepoPath))
                    {
                        oldWorkTree = subRepo.Config.Get<string>("core.worktree");
                        if (oldWorkTree != null)
                        {
                            _logger.LogDebug($"{subRepoPath} old worktree is {oldWorkTree.Value}, setting to {subRepoPath}");
                            subRepo.Config.Set("core.worktree", subRepoPath);
                        }
                        // This branch really shouldn't happen but just in case.
                        else
                        {
                            _logger.LogDebug($"{subRepoPath} has default worktree, leaving unchanged");
                        }
                    }

                    using (var subRepo = new Repository(subRepoPath))
                    {
                        _logger.LogDebug($"Resetting {sub.Name} to {sub.HeadCommitId.Sha}");
                        subRepo.Reset(ResetMode.Hard, subRepo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = subRepo.Refs }).Single(c => c.Sha == sub.HeadCommitId.Sha));
                        // Now we reset the worktree back so that when we can initialize a Repository
                        // from it, instead of having to figure out which hash of the repo was most recently checked out.
                        if (oldWorkTree != null)
                        {
                            _logger.LogDebug($"resetting {subRepoPath} worktree to {oldWorkTree.Value}");
                            subRepo.Config.Set("core.worktree", oldWorkTree.Value);
                        }
                        else
                        {
                            _logger.LogDebug($"leaving {subRepoPath} worktree as default");
                        }
                        _logger.LogDebug($"Done resetting {subRepoPath}, checking submodules");
                        CleanRepoAndSubmodules(subRepo);
                    }
                }

                if (File.Exists(subRepoGitFilePath))
                {
                    _logger.LogDebug($"Deleting {subRepoGitFilePath} to orphan submodule {sub.Name}");
                    File.Delete(subRepoGitFilePath);
                }
                else
                {
                    _logger.LogDebug($"{sub.Name} doesn't have a .gitdir redirect at {subRepoGitFilePath}, skipping delete");
                }
            }
        }
    }

    public async Task Push(
        string repoPath,
        string branchName,
        string remoteUrl,
        LibGit2Sharp.Identity? identity = null,
        bool force = false)
    {
        identity ??= new LibGit2Sharp.Identity(Constants.DarcBotName, Constants.DarcBotEmail);

        using var repo = new Repository(
            repoPath,
            new RepositoryOptions { Identity = identity });

        var remoteName = await AddRemoteIfMissingAsync(repoPath, remoteUrl);
        var remote = repo.Network.Remotes[remoteName];

        var branch = repo.Branches[branchName]
            ?? throw new Exception($"No branch {branchName} found in repo. {repo.Info.Path}");

        var pushOptions = new PushOptions
        {
            CredentialsProvider = (url, user, cred) =>
                new UsernamePasswordCredentials
                {
                    Username = Constants.GitHubBotUserName,
                    Password = _remoteTokenProvider.GetTokenForRepository(remoteUrl),
                }
        };

        var refSpec = force
            ? $"+{branch.CanonicalName}"
            : branch.CanonicalName;

        await _exponentialRetry.RetryAsync(
            () =>
            {
                repo.Network.Push(remote, refSpec, pushOptions);
                return Task.CompletedTask;
            },
            ex => _logger.LogWarning(ex, "An exception occurred during `git push`: {exceptionMessage}. Retrying...", ex.Message),
            ex => ex is LibGit2SharpException);
        repo.Branches.Update(branch, b => b.TrackedBranch = $"refs/remotes/{remote.Name}/{branch.FriendlyName}");

        _logger.LogInformation("Pushed branch {branch} to {remote}{forceIndicator}", branch, remote.Url, force ? " (force)" : "");
    }

    /// <summary>
    /// Adds a file to the repo's index respecting the original file's mode.
    /// </summary>
    /// <param name="repo">Repo to add the files to</param>
    /// <param name="file">Original GitFile to add</param>
    /// <param name="fullPath">Final path for the file to be added</param>
    private void AddFileToIndex(Repository repo, GitFile file, string fullPath)
    {
        var fileMode = (Mode)Convert.ToInt32(file.Mode, 8);
        if (!Enum.IsDefined(typeof(Mode), fileMode) || fileMode == Mode.Nonexistent)
        {
            _logger.LogInformation($"Could not detect file mode {file.Mode} for file {file.FilePath}. Assigning non-executable mode.");
            fileMode = Mode.NonExecutableFile;
        }
        Blob fileBlob = repo.ObjectDatabase.CreateBlob(fullPath);
        repo.Index.Add(fileBlob, file.FilePath, fileMode);
    }

    public void SafeCheckout(Repository repo, string commit, CheckoutOptions options)
    {
        try
        {
            _logger.LogDebug($"Trying safe checkout of {repo.Info.WorkingDirectory} at {commit}");
            Commands.Checkout(repo, commit, options);
        }
        catch (Exception e) when (e is InvalidSpecificationException
                                  || e is NameConflictException
                                  || e is LibGit2SharpException
                                  || e is NotFoundException)
        {
            _logger.LogInformation($"Checkout of {repo.Info.WorkingDirectory} at {commit} failed: {e}");
            _logger.LogInformation($"Fetching before attempting individual files.");

            FetchRepo(repo);

            try
            {
                _logger.LogDebug($"Post-fetch, trying to checkout {repo.Info.WorkingDirectory} at {commit} again");
                Commands.Checkout(repo, commit, options);
            }
            catch (NotFoundException ex)
            {
                throw new Exception($"Failed to find commit {commit} when checking out {repo.Info.WorkingDirectory}", ex);
            }
            catch (Exception ex)
            {
                var isDueToPathLength = ex is InvalidSpecificationException
                    || ex is NameConflictException
                    || ex is LibGit2SharpException;

                _logger.LogWarning($"Couldn't check out one or more files{(isDueToPathLength ? ", possibly due to path length limitations" : "")} ({ex})." +
                               "  Attempting to checkout by individual files.");
                SafeCheckoutByIndividualFiles(repo, commit, options);
            }
        }
        catch
        {
            _logger.LogInformation($"Couldn't checkout {commit}, attempting fetch.");
            FetchRepo(repo);
            try
            {
                _logger.LogDebug($"Post-fetch, trying to checkout {repo.Info.WorkingDirectory} at {commit} again");
                Commands.Checkout(repo, commit, options);
            }
            catch
            {
                _logger.LogDebug($"Couldn't checkout {commit} as a commit.  Attempting to resolve as a treeish.");
                string? resolvedReference = ParseReference(repo, commit);
                if (resolvedReference == null)
                {
                    _logger.LogError($"Couldn't resolve {commit} as a commit or treeish.  Checkout of {repo.Info.WorkingDirectory} failed.");
                    throw new ArgumentException($"Couldn't resolve {commit} as a commit or treeish.  Checkout of {repo.Info.WorkingDirectory} failed.");
                }

                _logger.LogDebug($"Resolved {commit} to {resolvedReference}, attempting to check out");
                try
                {
                    _logger.LogDebug($"Trying checkout of {repo.Info.WorkingDirectory} at {resolvedReference}");
                    Commands.Checkout(repo, resolvedReference, options);
                }
                catch (Exception e) when (e is InvalidSpecificationException
                                            || e is NameConflictException
                                            || e is LibGit2SharpException)
                {
                    _logger.LogWarning($"Couldn't check out one or more files, possibly due to path length limitations ({e})." +
                                    "  Attempting to checkout by individual files.");
                    SafeCheckoutByIndividualFiles(repo, resolvedReference, options);
                }
            }
        }
    }

    private void FetchRepo(Repository repo)
    {
        foreach (LibGit2Sharp.Remote remote in repo.Network.Remotes)
        {
            try
            {
                IEnumerable<string> refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
                _logger.LogDebug($"Fetching {string.Join(";", refSpecs)} from {remote.Url} in {repo.Info.Path}");
                Commands.Fetch(repo, remote.Name, refSpecs, new FetchOptions(), $"Fetching {repo.Info.Path} from {remote.Url}");
            }
            catch (Exception e)
            {
                _logger.LogWarning($"Fetch of {remote.Url} for {repo.Info.Path} failed: {e}.  Are you offline?");
            }
        }
    }

    /// <summary>
    /// This is just a base function to recurse from.
    /// </summary>
    private void SafeCheckoutByIndividualFiles(Repository repo, string commit, CheckoutOptions options)
    {
        _logger.LogDebug($"Beginning individual file checkout for {repo.Info.WorkingDirectory} at {commit}");
        SafeCheckoutTreeByIndividualFiles(repo, repo.Lookup(commit).Peel<Tree>(), "", commit, options);

    }

    /// <summary>
    /// Checkout a tree, and if it fails, recursively attempt to checkout all of its members.
    /// </summary>
    /// <param name="repo">The repo to check files out in</param>
    /// <param name="tree">The current tree we're looking at (starts at root)</param>
    /// <param name="treePath">The built-up path of the tree we are currently recursively in</param>
    /// <param name="commit">The commit we are trying to checkout at.  This should be resolved already if it needs to be (i.e. normal whole-repo checkout failed)</param>
    /// <param name="options">Options for checkout - mostly used to force checkout</param>
    /// <param name="log">Logger</param>
    private void SafeCheckoutTreeByIndividualFiles(
        Repository repo,
        Tree tree,
        string treePath,
        string commit,
        CheckoutOptions options)
    {
        foreach (TreeEntry f in tree)
        {
            try
            {
                repo.CheckoutPaths(commit, new[] { Path.Combine(treePath, f.Path) }, options);
            }
            catch (Exception e) when (e is InvalidSpecificationException
                                      || e is NameConflictException
                                      || e is LibGit2SharpException)
            {
                _logger.LogWarning($"Failed to checkout {Path.Combine(treePath, f.Path)} in {repo.Info.WorkingDirectory} at {commit}, skipping.  Exception: {e}");
                if (f.TargetType == TreeEntryTargetType.Tree)
                {
                    SafeCheckoutTreeByIndividualFiles(repo, f.Target.Peel<Tree>(), Path.Combine(treePath, f.Path), commit, options);
                }
            }
        }
    }

    /// <summary>
    /// Resolve a treeish in a repo's context.  This is sometimes needed for, e.g. a local branch name being supplied instead of a repo-context branch name.
    /// </summary>
    /// <param name="repo">The repo to resolve the treeish in</param>
    /// <param name="treeish">The treeish to resolve</param>
    /// <param name="log">Logger</param>
    /// <returns>A resolved treeish, or null if one could not be found</returns>
    private string? ParseReference(Repository repo, string treeish)
    {
        Reference? reference = null;
        GitObject dummy;
        try
        {
            repo.RevParse(treeish, out reference, out dummy);
        }
        catch
        {
            // nothing we can do
        }

        _logger.LogDebug($"Parsed {treeish} to mean {reference?.TargetIdentifier ?? "<invalid>"}");

        if (reference == null)
        {
            try
            {
                repo.RevParse($"origin/{treeish}", out reference, out dummy);
            }
            catch
            {
                // nothing we can do
            }

            _logger.LogDebug($"Parsed origin/{treeish} to mean {reference?.TargetIdentifier ?? "<invalid>"}");
        }
        return reference?.TargetIdentifier;
    }

    public Task<List<GitTreeItem>> LsTreeAsync(string repoPath, string gitRef, string? path = null)
    {
        using var repository = new Repository(repoPath);

        string treeSha;
        Tree rootTree;

        if (_gitRefCommitCache.TryGetValue((repoPath, gitRef, path), out var cachedSha))
        {
            treeSha = cachedSha;
            rootTree = repository.Lookup<Tree>(treeSha);
        }
        else
        {
            // Resolve the reference to get a commit
            var commit = repository.Lookup<LibGit2Sharp.Commit>(gitRef)
                ?? throw new ArgumentException($"Could not find commit for reference: {gitRef}");

            // Get the root tree from the commit
            rootTree = commit.Tree;

            // If a path is specified, navigate to that tree
            if (!string.IsNullOrEmpty(path))
            {
                var pathParts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
                var currentTree = rootTree;

                foreach (var part in pathParts)
                {
                    var treeEntry = currentTree.FirstOrDefault(e => e.Name == part)
                        ?? throw new DirectoryNotFoundException($"Path '{path}' not found in the repository.");

                    if (treeEntry.TargetType != TreeEntryTargetType.Tree)
                    {
                        throw new ArgumentException($"Path '{path}' is not a directory.");
                    }

                    currentTree = treeEntry.Target.Peel<Tree>();
                }

                // Set the tree to the one at the specified path
                rootTree = currentTree;
            }
        }

        List<GitTreeItem> gitTreeItems = [];

        foreach (var t in rootTree)
        {
            var type = t.TargetType == TreeEntryTargetType.GitLink ? "commit" : t.TargetType.ToString();
            var newPath = $"{path}/{t.Path}";
            if (t.TargetType == TreeEntryTargetType.Tree)
            {
                _gitRefCommitCache[(repoPath, gitRef, newPath)] = t.Target.Sha;
            }

            gitTreeItems.Add(new GitTreeItem
            {
                Type = type,
                Sha = t.Target.Sha,
                Path = $"{path}/{t.Path}"
            });
        }

        return Task.FromResult(gitTreeItems);
    }

    public async Task<bool> RepoExistsAsync(string repoPath)
    {
        try
        {
            await GetRootDirAsync(repoPath);
            return true;
        }
        catch (ProcessFailedException)
        {
            return false;
        }
    }
}

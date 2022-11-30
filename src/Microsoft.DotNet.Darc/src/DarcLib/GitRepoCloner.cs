// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Helpers;
using System.IO;
using System.Linq;
using System;
using Microsoft.Extensions.Logging;
using LibGit2Sharp;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public class GitRepoCloner : IGitRepoCloner
{
    private readonly ILogger _logger;
    private readonly string? _personalAccessToken;

    public GitRepoCloner(string? personalAccessToken, ILogger logger)
    {
        _logger = logger;
        _personalAccessToken = personalAccessToken;
    }

    /// <summary>
    ///     Clone a remote git repo.
    /// </summary>
    /// <param name="repoUri">Repository uri to clone</param>
    /// <param name="commit">Branch, commit, or tag to checkout</param>
    /// <param name="targetDirectory">Target directory to clone to</param>
    /// <param name="checkoutSubmodules">Indicates whether submodules should be checked out as well</param>
    /// <param name="gitDirectory">Location for the .git directory, or null for default</param>
    /// <returns></returns>
    public void Clone(string repoUri, string commit, string targetDirectory, bool checkoutSubmodules, string? gitDirectory)
    {
        string dotnetMaestro = "dotnet-maestro"; // lgtm [cs/hardcoded-credentials] Value is correct for this service
        CloneOptions cloneOptions = new()
        {
            Checkout = false,
            CredentialsProvider = (url, user, cred) =>
                new UsernamePasswordCredentials
                {
                    // The PAT is actually the only thing that matters here, the username
                    // will be ignored.
                    Username = dotnetMaestro,
                    Password = _personalAccessToken
                },
        };
        _logger.LogInformation("Cloning {repoUri} to {targetDirectory}", repoUri, targetDirectory);
        try
        {
            _logger.LogDebug($"Cloning {repoUri} to {targetDirectory}");
            string repoPath = Repository.Clone(
                repoUri,
                targetDirectory,
                cloneOptions);

            CheckoutOptions checkoutOptions = new()
            {
                CheckoutModifiers = CheckoutModifiers.Force,
            };

            _logger.LogDebug($"Reading local repo from {repoPath}");
            using (Repository localRepo = new(repoPath))
            {
                if (commit == null)
                {
                    commit = localRepo.Head.Reference.TargetIdentifier;
                    _logger.LogInformation($"Repo {localRepo.Info.WorkingDirectory} has no commit to clone at, assuming it's {commit}");
                }
                _logger.LogDebug($"Attempting to checkout {commit} as commit in {localRepo.Info.WorkingDirectory}");
                LibGit2SharpHelpers.SafeCheckout(localRepo, commit, checkoutOptions, _logger);
            }
            // LibGit2Sharp doesn't support a --git-dir equivalent yet (https://github.com/libgit2/libgit2sharp/issues/1467), so we do this manually
            if (gitDirectory != null)
            {
                Directory.Move(repoPath, gitDirectory);
                File.WriteAllText(repoPath.TrimEnd('\\', '/'), $"gitdir: {gitDirectory}");
            } 
            else
            {
                gitDirectory = repoPath;
            }

            if (checkoutSubmodules)
            {
                using var localRepo = new Repository(targetDirectory);
                CheckoutSubmodules(localRepo, cloneOptions, gitDirectory, _logger);
            }
        }
        catch (Exception exc)
        {
            throw new Exception($"Something went wrong when cloning repo {repoUri} at {commit ?? "<default branch>"} into {targetDirectory}", exc);
        }
    }

    private static void CheckoutSubmodules(Repository repo, CloneOptions submoduleCloneOptions, string gitDirParentPath, ILogger log)
    {
        foreach (Submodule sub in repo.Submodules)
        {
            log.LogDebug($"Updating submodule {sub.Name} at {sub.Path} for {repo.Info.WorkingDirectory}.  GitDirParent: {gitDirParentPath}");
            repo.Submodules.Update(sub.Name, new SubmoduleUpdateOptions { CredentialsProvider = submoduleCloneOptions.CredentialsProvider, Init = true });

            string normalizedSubPath = sub.Path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            string subRepoPath = Path.Combine(repo.Info.WorkingDirectory, normalizedSubPath);
            string relativeGitDirPath = File.ReadAllText(Path.Combine(subRepoPath, ".git")).Substring(8);

            log.LogDebug($"Submodule {sub.Name} has .gitdir {relativeGitDirPath}");
            string absoluteGitDirPath = Path.GetFullPath(Path.Combine(subRepoPath, relativeGitDirPath));
            string relocatedGitDirPath = absoluteGitDirPath.Replace(repo.Info.Path.TrimEnd(new[] { '/', '\\' }), gitDirParentPath.TrimEnd(new[] { '/', '\\' }));
            string subRepoGitFilePath = Path.Combine(subRepoPath, ".git");

            log.LogDebug($"Writing new .gitdir path {relocatedGitDirPath} to submodule at {subRepoPath}");

            // File.WriteAllText gets access denied for some reason
            using (FileStream s = File.OpenWrite(subRepoGitFilePath))
            using (StreamWriter w = new(s))
            {
                w.Write($"gitdir: {relocatedGitDirPath}");
                w.Flush();
                s.SetLength(s.Position);
            }

            // The worktree is stored in the .gitdir/config file, so we have to change it
            // to get it to check out to the correct place.
            ConfigurationEntry<string>? oldWorkTree = null;
            using (Repository subRepo = new(subRepoPath))
            {
                oldWorkTree = subRepo.Config.Get<string>("core.worktree");
                if (oldWorkTree != null)
                {
                    log.LogDebug($"{subRepoPath} old worktree is {oldWorkTree.Value}, setting to {subRepoPath}");
                    subRepo.Config.Set("core.worktree", subRepoPath);
                }
                else
                {
                    log.LogDebug($"{subRepoPath} has default worktree, leaving unchanged");
                }
            }

            using (Repository subRepo = new(subRepoPath))
            {
                log.LogDebug($"Resetting {sub.Name} to {sub.HeadCommitId.Sha}");
                subRepo.Reset(ResetMode.Hard, subRepo.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = subRepo.Refs }).Single(c => c.Sha == sub.HeadCommitId.Sha));

                // Now we reset the worktree back so that when we can initialize a Repository
                // from it, instead of having to figure out which hash of the repo was most recently checked out.
                if (oldWorkTree != null)
                {
                    log.LogDebug($"resetting {subRepoPath} worktree to {oldWorkTree.Value}");
                    subRepo.Config.Set("core.worktree", oldWorkTree.Value);
                }
                else
                {
                    log.LogDebug($"leaving {subRepoPath} worktree as default");
                }

                log.LogDebug($"Done checking out {subRepoPath}, checking submodules");
                CheckoutSubmodules(subRepo, submoduleCloneOptions, absoluteGitDirPath, log);
            }

            if (File.Exists(subRepoGitFilePath))
            {
                log.LogDebug($"Deleting {subRepoGitFilePath} to orphan submodule {sub.Name}");
                File.Delete(subRepoGitFilePath);
            }
            else
            {
                log.LogDebug($"{sub.Name} doesn't have a .gitdir redirect at {subRepoGitFilePath}, skipping delete");
            }
        }
    }
}

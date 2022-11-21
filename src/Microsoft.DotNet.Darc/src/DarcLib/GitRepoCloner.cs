// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Helpers;
using System.IO;
using System.Linq;
using System;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public class GitRepoCloner : IGitRepoCloner
{
    private readonly ILogger _logger;
    private readonly string _personalAccessToken;

    public GitRepoCloner(string personalAccessToken, ILogger logger)
    {
        _logger = logger;
        _personalAccessToken = personalAccessToken;
    }

    public void Clone(string repoUri, string commit, string targetDirectory, bool checkoutSubmodules, string? gitDirectory = null)
    {
        Clone(repoUri, commit, targetDirectory, checkoutSubmodules, _logger, _personalAccessToken, gitDirectory);
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
    protected static void Clone(string repoUri, string commit, string targetDirectory, bool checkoutSubmodules, ILogger logger, string pat, string? gitDirectory)
    {
        string dotnetMaestro = "dotnet-maestro"; // lgtm [cs/hardcoded-credentials] Value is correct for this service
        LibGit2Sharp.CloneOptions cloneOptions = new LibGit2Sharp.CloneOptions
        {
            Checkout = false,
            CredentialsProvider = (url, user, cred) =>
                new LibGit2Sharp.UsernamePasswordCredentials
                {
                    // The PAT is actually the only thing that matters here, the username
                    // will be ignored.
                    Username = dotnetMaestro,
                    Password = pat
                },
        };
        logger.LogInformation("Cloning {repoUri} to {targetDirectory}", repoUri, targetDirectory);
        try
        {
            logger.LogDebug($"Cloning {repoUri} to {targetDirectory}");
            string repoPath = LibGit2Sharp.Repository.Clone(
                repoUri,
                targetDirectory,
                cloneOptions);

            LibGit2Sharp.CheckoutOptions checkoutOptions = new LibGit2Sharp.CheckoutOptions
            {
                CheckoutModifiers = LibGit2Sharp.CheckoutModifiers.Force,
            };

            logger.LogDebug($"Reading local repo from {repoPath}");
            using (LibGit2Sharp.Repository localRepo = new LibGit2Sharp.Repository(repoPath))
            {
                if (commit == null)
                {
                    commit = localRepo.Head.Reference.TargetIdentifier;
                    logger.LogInformation($"Repo {localRepo.Info.WorkingDirectory} has no commit to clone at, assuming it's {commit}");
                }
                logger.LogDebug($"Attempting to checkout {commit} as commit in {localRepo.Info.WorkingDirectory}");
                LibGit2SharpHelpers.SafeCheckout(localRepo, commit, checkoutOptions, logger);
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
                using var localRepo = new LibGit2Sharp.Repository(targetDirectory);
                CheckoutSubmodules(localRepo, cloneOptions, gitDirectory, logger);
            }
        }
        catch (Exception exc)
        {
            throw new Exception($"Something went wrong when cloning repo {repoUri} at {commit ?? "<default branch>"} into {targetDirectory}", exc);
        }
    }

    private static void CheckoutSubmodules(LibGit2Sharp.Repository repo, LibGit2Sharp.CloneOptions submoduleCloneOptions, string gitDirParentPath, ILogger log)
    {
        foreach (LibGit2Sharp.Submodule sub in repo.Submodules)
        {
            log.LogDebug($"Updating submodule {sub.Name} at {sub.Path} for {repo.Info.WorkingDirectory}.  GitDirParent: {gitDirParentPath}");
            repo.Submodules.Update(sub.Name, new LibGit2Sharp.SubmoduleUpdateOptions { CredentialsProvider = submoduleCloneOptions.CredentialsProvider, Init = true });

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
            using (StreamWriter w = new StreamWriter(s))
            {
                w.Write($"gitdir: {relocatedGitDirPath}");
                w.Flush();
                s.SetLength(s.Position);
            }

            // The worktree is stored in the .gitdir/config file, so we have to change it
            // to get it to check out to the correct place.
            LibGit2Sharp.ConfigurationEntry<string>? oldWorkTree = null;
            using (LibGit2Sharp.Repository subRepo = new LibGit2Sharp.Repository(subRepoPath))
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

            using (LibGit2Sharp.Repository subRepo = new LibGit2Sharp.Repository(subRepoPath))
            {
                log.LogDebug($"Resetting {sub.Name} to {sub.HeadCommitId.Sha}");
                subRepo.Reset(LibGit2Sharp.ResetMode.Hard, subRepo.Commits.QueryBy(new LibGit2Sharp.CommitFilter { IncludeReachableFrom = subRepo.Refs }).Single(c => c.Sha == sub.HeadCommitId.Sha));

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

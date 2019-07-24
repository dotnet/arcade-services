// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib
{
    public class RemoteRepoBase
    {
        protected RemoteRepoBase(string temporaryRepositoryPath)
        {
            TemporaryRepositoryPath = temporaryRepositoryPath;
        }

        protected string TemporaryRepositoryPath { get; set; }

        /// <summary>
        /// Cloning big repos takes a considerable amount of time when checking out the files. When
        /// working on batched subscription, the operation could take more than an hour causing the
        /// GitHub token to expire. By doing sparse and shallow checkout, we only deal with the files
        /// we need avoiding to check the complete repo shaving time from the overall push process
        /// </summary>
        /// <param name="filesToCommit">Collection of files to update.</param>
        /// <param name="repoUri">The repository to push the files to.</param>
        /// <param name="branch">The branch to push the files to.</param>
        /// <param name="commitMessage">The commmit message.</param>
        /// <returns></returns>
        protected async Task CommitFilesAsync(
            List<GitFile> filesToCommit,
            string repoUri,
            string branch,
            string commitMessage,
            ILogger _logger,
            string pat)
        {
            string dotnetMaestro = "dotnet-maestro";
            using (_logger.BeginScope("Pushing files to {branch}", branch))
            {
                string tempRepoFolder = Path.Combine(TemporaryRepositoryPath, Path.GetRandomFileName());
                string remote = "origin";

                try
                {
                    string clonedRepo = null;

                    using (_logger.BeginScope("Sparse and shallow checkout of branch {branch} in {repoUri}...", branch, repoUri))
                    {
                        clonedRepo = LocalHelpers.SparseAndShallowCheckout(repoUri, branch, tempRepoFolder, _logger, remote, dotnetMaestro, pat);
                    }

                    if (clonedRepo == null)
                    {
                        throw new DarcException($"Something failed while trying to sparse and shalllow checkout branch {branch} in {repoUri}");
                    }

                    foreach (GitFile file in filesToCommit)
                    {
                        string filePath = Path.Combine(clonedRepo, file.FilePath);

                        if (file.Operation == GitFileOperation.Add)
                        {
                            if (!File.Exists(filePath))
                            {
                                string parentFolder = Directory.GetParent(filePath).FullName;

                                Directory.CreateDirectory(parentFolder);
                            }

                            using (FileStream stream = File.Create(filePath))
                            {
                                byte[] contentBytes = GetUtf8ContentBytes(file.Content, file.ContentEncoding);
                                await stream.WriteAsync(contentBytes, 0, contentBytes.Length);
                            }
                        }
                        else
                        {
                            File.Delete(Path.Combine(tempRepoFolder, file.FilePath));
                        }
                    }

                    LocalHelpers.ExecuteCommand("git", "add -A", _logger, clonedRepo);
                    LocalHelpers.ExecuteCommand("git", $"commit -m \"{commitMessage}\"", _logger, clonedRepo);
                    LocalHelpers.ExecuteCommand("git", $"push {remote} {branch}", _logger, clonedRepo);
                }
                catch (LibGit2Sharp.EmptyCommitException)
                {
                    _logger.LogInformation("There was nothing to commit...");
                }
                catch (Exception exc)
                {
                    // This was originally a DarcException. Making it an actual Exception so we get to see in AppInsights if something failed while
                    // commiting the changes
                    throw new Exception($"Something went wrong when pushing the files to repo {repoUri} in branch {branch}", exc);
                }
                finally
                {

                    try
                    {
                        // Libgit2Sharp behaves similarly to git and marks files under the .git/objects hierarchy as read-only, 
                        // thus if the read-only attribute is not unset an UnauthorizedAccessException is thrown.
                        GitFileManager.NormalizeAttributes(tempRepoFolder);

                        Directory.Delete(tempRepoFolder, true);
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // If the directory wasn't found, that means that the clone operation above failed
                        // but this error isn't interesting at all.
                    }
                }
            }
        }

        /// <summary>
        ///     Clone a remote git repo.
        /// </summary>
        /// <param name="repoUri">Repository uri to clone</param>
        /// <param name="commit">Branch, commit, or tag to checkout</param>
        /// <param name="targetDirectory">Target directory to clone to</param>
        /// <param name="gitDirectory">Location for the .git directory, or null for default</param>
        /// <returns></returns>
        protected void Clone(string repoUri, string commit, string targetDirectory, ILogger _logger, string pat, string gitDirectory)
        {
            string dotnetMaestro = "dotnet-maestro";
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
            using (_logger.BeginScope("Cloning {repoUri} to {targetDirectory}", repoUri, targetDirectory))
            {
                try
                {
                    _logger.LogDebug($"Cloning {repoUri} to {targetDirectory}");
                    string repoPath = LibGit2Sharp.Repository.Clone(
                        repoUri,
                        targetDirectory,
                        cloneOptions);

                    LibGit2Sharp.CheckoutOptions checkoutOptions = new LibGit2Sharp.CheckoutOptions
                    {
                        CheckoutModifiers = LibGit2Sharp.CheckoutModifiers.Force,
                    };

                    _logger.LogDebug($"Reading local repo from {repoPath}");
                    using (LibGit2Sharp.Repository localRepo = new LibGit2Sharp.Repository(repoPath))
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
                    using (LibGit2Sharp.Repository localRepo = new LibGit2Sharp.Repository(targetDirectory))
                    {
                        CheckoutSubmodules(localRepo, cloneOptions, gitDirectory, _logger);
                    }
                }
                catch (Exception exc)
                {
                    throw new Exception($"Something went wrong when cloning repo {repoUri} at {commit ?? "<default branch>"} into {targetDirectory}", exc);
                }
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
                LibGit2Sharp.ConfigurationEntry<string> oldWorkTree = null;
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

        private byte[] GetUtf8ContentBytes(string content, ContentEncoding encoding)
        {
            switch (encoding)
            {
                case ContentEncoding.Base64:
                    return Convert.FromBase64String(content);
                case ContentEncoding.Utf8:
                    return Encoding.UTF8.GetBytes(content);
                default:
                    throw new NotImplementedException("Unexpected content encoding.");
            }
        }
    }
}

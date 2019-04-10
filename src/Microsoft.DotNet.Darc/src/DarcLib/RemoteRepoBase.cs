// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
        /// We used to group commits in a tree object so there would be only one commit per 
        /// change but this doesn't work for trees that end up being too big (around 20K files).
        /// By using LibGit2Sharp we still group changes in one and we don't need to create a new
        /// tree. Everything happens locally in the host executing the push.
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

                try
                {
                    string repoPath = LibGit2Sharp.Repository.Clone(
                        repoUri,
                        tempRepoFolder,
                        new LibGit2Sharp.CloneOptions
                        {
                            BranchName = branch,
                            Checkout = true,
                            CredentialsProvider = (url, user, cred) =>
                            new LibGit2Sharp.UsernamePasswordCredentials
                            {
                                // The PAT is actually the only thing that matters here, the username
                                // will be ignored.
                                Username = dotnetMaestro,
                                Password = pat
                            }
                        });

                    using (LibGit2Sharp.Repository localRepo = new LibGit2Sharp.Repository(repoPath))
                    {
                        foreach (GitFile file in filesToCommit)
                        {
                            string filePath = Path.Combine(tempRepoFolder, file.FilePath);

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

                        LibGit2Sharp.Commands.Stage(localRepo, "*");

                        LibGit2Sharp.Signature author = new LibGit2Sharp.Signature(dotnetMaestro, $"@{dotnetMaestro}", DateTime.Now);
                        LibGit2Sharp.Signature commiter = author;
                        localRepo.Commit(commitMessage, author, commiter, new LibGit2Sharp.CommitOptions
                        {
                            AllowEmptyCommit = false,
                            PrettifyMessage = true
                        });

                        localRepo.Network.Push(localRepo.Branches[branch], new LibGit2Sharp.PushOptions
                        {
                            CredentialsProvider = (url, user, cred) =>
                            new LibGit2Sharp.UsernamePasswordCredentials
                            {
                                // The PAT is actually the only thing that matters here, the username
                                // will be ignored.
                                Username = dotnetMaestro,
                                Password = pat
                            }
                        });
                    }
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

                    _logger.LogDebug($"Reading local repo from {repoPath}");
                    using (LibGit2Sharp.Repository localRepo = new LibGit2Sharp.Repository(repoPath))
                    {
                        if (commit == null)
                        {
                            commit = localRepo.Head.Reference.TargetIdentifier;
                        }
                        _logger.LogDebug($"Checking out {commit} in {repoPath}");
                        LibGit2Sharp.Commands.Checkout(localRepo, commit);
                    }
                    // LibGit2Sharp doesn't support a --git-dir equivalent yet (https://github.com/libgit2/libgit2sharp/issues/1467), so we do this manually
                    if (gitDirectory != null)
                    {
                        Directory.Move(repoPath, gitDirectory);
                        File.WriteAllText(repoPath.TrimEnd('\\', '/'), $"gitdir: {gitDirectory}");
                    }
                    using (LibGit2Sharp.Repository localRepo = new LibGit2Sharp.Repository(repoPath))
                    {
                        CheckoutSubmodules(localRepo, cloneOptions, gitDirectory);
                    }
                }
                catch (Exception exc)
                {
                    throw new Exception($"Something went wrong when cloning repo {repoUri} at {commit ?? "<default branch>"} into {targetDirectory}", exc);
                }
            }
        }

        private static void CheckoutSubmodules(LibGit2Sharp.Repository repo, LibGit2Sharp.CloneOptions submoduleCloneOptions, string gitDirParentPath)
        {
            foreach (LibGit2Sharp.Submodule sub in repo.Submodules)
            {
                repo.Submodules.Update(sub.Name, new LibGit2Sharp.SubmoduleUpdateOptions { CredentialsProvider = submoduleCloneOptions.CredentialsProvider, Init = true });
                string subRepoPath = Path.Combine(repo.Info.WorkingDirectory, sub.Path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
                string relativeGitDirPath = File.ReadAllText(Path.Combine(subRepoPath, ".git")).Substring(8);
                //using (FileStream s = File.OpenRead(Path.Combine(subRepoPath, ".git")))
                //using (StreamReader r = new StreamReader(s))
                //{
                //    relativeGitDirPath = r.ReadToEnd().Substring("gitdir: ".Length + 1);
                //}
                string absoluteGitDirPath = Path.GetFullPath(Path.Combine(subRepoPath, relativeGitDirPath));
                string relocatedGitDirPath = absoluteGitDirPath.Replace(repo.Info.Path.TrimEnd(new[] { '/', '\\' }), gitDirParentPath.TrimEnd(new[] { '/', '\\' }));
                // File.WriteAllText gets access denied for some reason
                using (FileStream s = File.OpenWrite(Path.Combine(subRepoPath, ".git")))
                using (StreamWriter w = new StreamWriter(s))
                {
                    w.Write($"gitdir: {relocatedGitDirPath}");
                }
                using (LibGit2Sharp.Repository subRepo = new LibGit2Sharp.Repository(subRepoPath))
                {
                    subRepo.Reset(LibGit2Sharp.ResetMode.Hard, subRepo.Commits.Single(c => c.Sha == sub.HeadCommitId.Sha));
                    CheckoutSubmodules(subRepo, submoduleCloneOptions, gitDirParentPath);
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

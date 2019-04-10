// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib
{
    public class LocalGitClient : IGitRepo
    {
        private ILogger _logger;

        /// <summary>
        ///     Construct a new local git client
        /// </summary>
        /// <param name="path">Current path</param>
        public LocalGitClient(ILogger logger)
        {
            _logger = logger;
        }

        public Task<string> CheckIfFileExistsAsync(string repoUri, string filePath, string branch)
        {
            throw new NotImplementedException();
        }

        public Task CreateBranchAsync(string repoUri, string newBranch, string baseBranch)
        {
            throw new InvalidOperationException();
        }

        public Task DeleteBranchAsync(string repoUri, string branch)
        {
            throw new NotImplementedException();
        }

        public HttpClient CreateHttpClient(string versionOverride = null)
        {
            throw new InvalidOperationException();
        }

        public Task<string> GetFileContentsAsync(string ownerAndRepo, string path)
        {
            return GetFileContentsAsync(path, null, null);
        }

        public async Task<string> GetFileContentsAsync(string relativeFilePath, string repoUri, string branch)
        {
            string fullPath = Path.Combine(repoUri, relativeFilePath);
            using (var streamReader = new StreamReader(fullPath))
            {
                return await streamReader.ReadToEndAsync();
            }
        }

        public Task CreateOrUpdatePullRequestCommentAsync(string pullRequestUrl, string message)
        {
            throw new NotImplementedException();
        }

        public Task<List<GitFile>> GetFilesAtCommitAsync(string repoUri, string commit, string path)
        {
            string repoDir = LocalHelpers.GetRootDir(_logger);
            string sourceFolder = Path.Combine(repoDir, path);
            return Task.Run(() => Directory.GetFiles(
                sourceFolder,
                "*.*",
                SearchOption.AllDirectories).Select(
                    file =>
                    {
                        return new GitFile(
                            file.Remove(0, repoDir.Length + 1).Replace("\\", "/"),
                            File.ReadAllText(file)
                            );
                    }
                ).ToList());
        }

        public Task<string> GetLastCommitShaAsync(string ownerAndRepo, string branch)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetPullRequestBaseBranch(string pullRequestUrl)
        {
            throw new NotImplementedException();
        }

        public Task<IList<Check>> GetPullRequestChecksAsync(string pullRequestUrl)
        {
            throw new NotImplementedException();
        }

        public Task<IList<Review>> GetPullRequestReviewsAsync(string pullRequestUrl)
        {
            throw new NotImplementedException();
        }

        public Task<IList<Commit>> GetPullRequestCommitsAsync(string pullRequestUrl)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetPullRequestRepo(string pullRequestUrl)
        {
            throw new NotImplementedException();
        }

        public Task<PullRequest> GetPullRequestAsync(string pullRequestUrl)
        {
            throw new NotImplementedException();
        }

        public Task<string> CreatePullRequestAsync(string repoUri, PullRequest pullRequest)
        {
            throw new NotImplementedException();
        }

        public Task UpdatePullRequestAsync(string pullRequestUri, PullRequest pullRequest)
        {
            throw new NotImplementedException();
        }

        public Task<PrStatus> GetPullRequestStatusAsync(string pullRequestUrl)
        {
            throw new NotImplementedException();
        }

        public Task MergePullRequestAsync(string pullRequestUrl, MergePullRequestParameters parameters)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     Updates local copies of the files.
        /// </summary>
        /// <param name="filesToCommit">Files to update locally</param>
        /// <param name="repoUri">Base path of the repo</param>
        /// <param name="branch">Unused</param>
        /// <param name="commitMessage">Unused</param>
        /// <returns></returns>
        public async Task CommitFilesAsync(List<GitFile> filesToCommit, string repoUri, string branch, string commitMessage)
        {
            foreach (GitFile file in filesToCommit.Where(f => f.Operation != GitFileOperation.Delete))
            {
                switch (file.Operation)
                {
                    case GitFileOperation.Add:
                        string parentDirectory = Directory.GetParent(file.FilePath).FullName;

                        if (!Directory.Exists(parentDirectory))
                        {
                            Directory.CreateDirectory(parentDirectory);
                        }

                        string fullPath = Path.Combine(repoUri, file.FilePath);
                        using (var streamWriter = new StreamWriter(fullPath))
                        {
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
                            finalContent = NormalizeLineEndings(fullPath, finalContent);
                            await streamWriter.WriteAsync(finalContent);
                        }
                        break;
                    case GitFileOperation.Delete:
                        if (File.Exists(file.FilePath))
                        {
                            File.Delete(file.FilePath);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Normalize line endings of content.
        /// </summary>
        /// <param name="filePath">Path of file</param>
        /// <param name="content">Content to normalize</param>
        /// <returns>Normalized content</returns>
        /// <remarks>
        ///     Normalize based on the following rules:
        ///     - Auto CRLF is assumed.
        ///     - Check the git attributes the file to determine whether it has a specific setting for the file.  If so, use that.
        ///     - If no setting, or if auto, then determine whether incoming content differs in line ends vs. the
        ///       OS setting, and replace if needed.
        /// </remarks>
        private string NormalizeLineEndings(string filePath, string content)
        {
            const string crlf = "\r\n";
            const string lf = "\n";
            // Check gitAttributes to determine whether the file has eof handling set.
            string eofAttr = LocalHelpers.ExecuteCommand("git", $"check-attr eol -- {filePath}", _logger);
            if (string.IsNullOrEmpty(eofAttr) ||
                eofAttr.Contains("eol: unspecified") ||
                eofAttr.Contains("eol: auto"))
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
            else if (eofAttr.Contains("eol: crlf"))
            {
                // Test to avoid adding extra \r.
                if (!content.Contains(crlf))
                {
                    return content.Replace(lf, crlf);
                }
            }
            else if (eofAttr.Contains("eol: lf"))
            {
                return content.Replace(crlf, lf);
            }
            else
            {
                throw new DarcException($"Unknown eof setting '{eofAttr}' for file '{filePath};");
            }
            return content;
        }

        public string GetOwnerAndRepoFromRepoUri(string repoUri)
        {
            throw new NotImplementedException();
        }

        public Task<IEnumerable<int>> SearchPullRequestsAsync(
            string repoUri,
            string pullRequestBranch,
            PrStatus status,
            string keyword = null,
            string author = null)
        {
            throw new NotImplementedException();
        }

        public Task<GitDiff> GitDiffAsync(string repoUri, string baseVersion, string targetVersion)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     Clone a remote repository at the specified commit.
        /// </summary>
        /// <param name="repoUri">Remote git repo to clone</param>
        /// <param name="commit">Tag, branch, or commit to clone at</param>
        /// <param name="targetDirectory">Directory to clone into</param>
        /// <param name="gitDirectory">Directory for the .git folder, or null for default</param>
        public void Clone(string repoUri, string commit, string targetDirectory, string gitDirectory)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///     Checkout the repo to the specified state.
        /// </summary>
        /// <param name="commit">Tag, branch, or commit to checkout.</param>
        public void Checkout(string repoDir, string commit, bool force = false)
        {
            using (_logger.BeginScope("Checking out {commit}", commit))
            {
                LibGit2Sharp.CheckoutOptions checkoutOptions = new LibGit2Sharp.CheckoutOptions
                {
                    CheckoutModifiers = force ? LibGit2Sharp.CheckoutModifiers.Force : LibGit2Sharp.CheckoutModifiers.None,
                };
                try
                {
                    _logger.LogDebug($"Checking out {commit}");

                    _logger.LogDebug($"Reading local repo from {repoDir}");
                    using (LibGit2Sharp.Repository localRepo = new LibGit2Sharp.Repository(repoDir))
                    {
                        try
                        {
                            _logger.LogDebug($"Attempting to check out {commit} in {repoDir}");
                            LibGit2Sharp.Commands.Checkout(localRepo, commit, checkoutOptions);
                            if (force)
                            {
                                CleanRepoAndSubmodules(localRepo, _logger);
                            }
                        }
                        catch (LibGit2Sharp.NotFoundException)
                        {
                            _logger.LogWarning($"Couldn't find commit {commit} in {repoDir} locally.  Attempting fetch.");
                            try
                            {
                                foreach (LibGit2Sharp.Remote r in localRepo.Network.Remotes)
                                {
                                    IEnumerable<string> refSpecs = r.FetchRefSpecs.Select(x => x.Specification);
                                    _logger.LogDebug($"Fetching {string.Join(";", refSpecs)} from {r.Url} in {repoDir}");
                                    try
                                    {
                                        LibGit2Sharp.Commands.Fetch(localRepo, r.Name, refSpecs, new LibGit2Sharp.FetchOptions(), $"Fetching from {r.Url}");
                                    }
                                    catch
                                    {
                                        _logger.LogWarning($"Fetching failed, are you offline or missing a remote?");
                                    }
                                }
                                _logger.LogDebug($"After fetch, attempting to checkout {commit} in {repoDir}");
                                try
                                {
                                    LibGit2Sharp.Commands.Checkout(localRepo, commit, checkoutOptions);
                                }
                                catch
                                {
                                    _logger.LogDebug($"Couldn't checkout {commit} as a commit after fetch.  Attempting to resolve as a treeish.");
                                    string resolvedReference = ParseReference(localRepo, commit, _logger);
                                    if (resolvedReference != null)
                                    {
                                        _logger.LogDebug($"Resolved {commit} to {resolvedReference}, attempting to check out");
                                        LibGit2Sharp.Commands.Checkout(localRepo, resolvedReference, checkoutOptions);
                                    }
                                }

                                if (force)
                                {
                                    CleanRepoAndSubmodules(localRepo, _logger);
                                }
                            }
                            catch   // Most likely network exception, could also be no remotes.  We can't do anything about any error here.
                            {
                                _logger.LogError($"After fetch, still couldn't find commit or treeish {commit} in {repoDir}.  Are you offline or missing a remote?");
                                throw;
                            }
                        }
                    }
                }
                catch (Exception exc)
                {
                    throw new Exception($"Something went wrong when checking out {commit} in {repoDir}", exc);
                }
            }
        }

        private static void CleanRepoAndSubmodules(LibGit2Sharp.Repository repo, ILogger log)
        {
            log.LogDebug($"Beginning clean of {repo.Info.WorkingDirectory} and {repo.Submodules.Count()} submodules");

            LibGit2Sharp.StatusOptions options = new LibGit2Sharp.StatusOptions
            {
                IncludeUntracked = true,
                RecurseUntrackedDirs = true,
            };
            int count = 0;
            foreach (LibGit2Sharp.StatusEntry item in repo.RetrieveStatus(options))
            {
                if (item.State == LibGit2Sharp.FileStatus.NewInWorkdir)
                {
                    File.Delete(Path.Combine(repo.Info.WorkingDirectory, item.FilePath));
                    ++count;
                }
            }
            log.LogDebug($"Deleted {count} untracked files");

            foreach (LibGit2Sharp.Submodule sub in repo.Submodules)
            {
                string normalizedSubPath = sub.Path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                string subRepoPath = Path.Combine(repo.Info.WorkingDirectory, normalizedSubPath);
                string subRepoGitFilePath = Path.Combine(subRepoPath, ".git");
                if (!File.Exists(subRepoGitFilePath))
                {
                    log.LogDebug($"Submodule {sub.Name} in {subRepoPath} does not appear to be initialized, attempting to initialize now.");
                    // hasn't been initialized yet, can happen when differest hashes have new or moved submodules
                    repo.Submodules.Update(sub.Name, new LibGit2Sharp.SubmoduleUpdateOptions { Init = true });
                }

                log.LogDebug($"Beginning clean of submodule {sub.Name}");
                using (LibGit2Sharp.Repository subRepo = new LibGit2Sharp.Repository(subRepoPath))
                {
                    subRepo.Reset(LibGit2Sharp.ResetMode.Hard, subRepo.Commits.Single(c => c.Sha == sub.HeadCommitId.Sha));
                    CleanRepoAndSubmodules(subRepo, log);
                }
            }
        }

        private static string ParseReference(LibGit2Sharp.Repository repo, string treeish, ILogger log)
        {
            LibGit2Sharp.Reference reference = null;
            LibGit2Sharp.GitObject dummy;
            try
            {
                repo.RevParse(treeish, out reference, out dummy);
            }
            catch
            {
                // nothing we can do
            }
            log.LogDebug($"Parsed {treeish} to mean {reference?.TargetIdentifier ?? "<invalid>"}");
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
                log.LogDebug($"Parsed origin/{treeish} to mean {reference?.TargetIdentifier ?? "<invalid>"}");
            }
            return reference?.TargetIdentifier;
        }
    }
}

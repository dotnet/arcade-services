using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Microsoft.DotNet.DarcLib.Helpers
{
    internal static class LibGit2SharpHelpers
    {
        /// <summary>
        /// This function works around a couple common issues when checking out files in LibGit2Sharp.
        /// 1. First attempt a normal whole-repo checkout at the specified treeish.
        /// 2. This could fail for two reasons: long path names on Windows, or the treeish not being resolved on any platform.
        /// 3. If the exception is one of the ones we know LibGit2Sharp to throw for max path issues, we try to checkout files individually.
        ///     1. Get the root tree.
        ///     2. For each item in the tree, try to check it out.
        ///     3. If that fails and the item is also a tree, recurse on #2.
        /// 4. Otherwise, assume that the treeish specified needs to be resolved.  Resolve it, then...
        /// 5. Attempt a normal whole-repo checkout at the resolved treeish.
        /// 6. If this fails with one of the exception types linked to MAX_PATH issues, do #3 with the resolved treeish.
        /// This will still fail if the specified treeish doesn't resolve, or if checkout fails for any other reason than MAX_PATH.
        /// </summary>
        /// <param name="repo">Repo to check out the files from</param>
        /// <param name="commit">Commit, tag, or branch to checkout the files at</param>
        /// <param name="options">Checkout options - mostly whether to force</param>
        /// <param name="log">Logger</param>
        public static void SafeCheckout(Repository repo, string commit, CheckoutOptions options, ILogger log)
        {
            try
            {
                log.LogDebug($"Trying safe checkout of {repo.Info.WorkingDirectory} at {commit}");
                Commands.Checkout(repo, commit, options);
            }
            catch (Exception e) when (e is InvalidSpecificationException
                                   || e is NameConflictException
                                   || e is LibGit2SharpException)
            {
                log.LogInformation($"Checkout of {repo.Info.WorkingDirectory} at {commit} failed, fetching before attempting individual files.");
                FetchRepo(repo, log);
                try
                {
                    log.LogDebug($"Post-fetch, trying to checkout {repo.Info.WorkingDirectory} at {commit} again");
                    Commands.Checkout(repo, commit, options);
                }
                catch
                {
                    log.LogWarning($"Couldn't check out one or more files, possibly due to path length limitations ({e.ToString()})." +
                                    "  Attempting to checkout by individual files.");
                    SafeCheckoutByIndividualFiles(repo, commit, options, log);
                }
            }
            catch
            {
                log.LogInformation($"Couldn't checkout {commit}, attempting fetch.");
                FetchRepo(repo, log);
                try
                {
                    log.LogDebug($"Post-fetch, trying to checkout {repo.Info.WorkingDirectory} at {commit} again");
                    Commands.Checkout(repo, commit, options);
                }
                catch
                {
                    log.LogDebug($"Couldn't checkout {commit} as a commit.  Attempting to resolve as a treeish.");
                    string resolvedReference = ParseReference(repo, commit, log);
                    if (resolvedReference != null)
                    {
                        log.LogDebug($"Resolved {commit} to {resolvedReference}, attempting to check out");
                        try
                        {
                            log.LogDebug($"Trying checkout of {repo.Info.WorkingDirectory} at {resolvedReference}");
                            Commands.Checkout(repo, resolvedReference, options);
                        }
                        catch (Exception e) when (e is InvalidSpecificationException
                                               || e is NameConflictException
                                               || e is LibGit2SharpException)
                        {
                            log.LogWarning($"Couldn't check out one or more files, possibly due to path length limitations ({e.ToString()})." +
                                            "  Attempting to checkout by individual files.");
                            SafeCheckoutByIndividualFiles(repo, resolvedReference, options, log);
                        }
                    }
                    else
                    {
                        log.LogError($"Couldn't resolve {commit} as a commit or treeish.  Checkout of {repo.Info.WorkingDirectory} failed.");
                        throw new ArgumentException($"Couldn't resolve {commit} as a commit or treeish.  Checkout of {repo.Info.WorkingDirectory} failed.");
                    }
                }
            }
        }

        /// <summary>
        /// Adds a file to the repo's index respecting the original file's mode.
        /// </summary>
        /// <param name="repo">Repo to add the files to</param>
        /// <param name="file">Original GitFile to add</param>
        /// <param name="fullPath">Final path for the file to be added</param>
        /// <param name="log">Logger</param>
        internal static void AddFileToIndex(Repository repo, GitFile file, string fullPath, ILogger log)
        {
            var fileMode = (Mode)Convert.ToInt32(file.Mode, 8);
            if (!Enum.IsDefined(typeof(Mode), fileMode) || fileMode == Mode.Nonexistent)
            {
                log.LogInformation($"Could not detect file mode {file.Mode} for file {file.FilePath}. Assigning non-executable mode.");
                fileMode = Mode.NonExecutableFile;
            }
            Blob fileBlob = repo.ObjectDatabase.CreateBlob(fullPath);
            repo.Index.Add(fileBlob, file.FilePath, fileMode);
        }

        private static void FetchRepo(Repository repo, ILogger log)
        {
            foreach (LibGit2Sharp.Remote remote in repo.Network.Remotes)
            {
                try
                {
                    IEnumerable<string> refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
                    log.LogDebug($"Fetching {string.Join(";", refSpecs)} from {remote.Url} in {repo.Info.Path}");
                    LibGit2Sharp.Commands.Fetch(repo, remote.Name, refSpecs, new FetchOptions(), $"Fetching {repo.Info.Path} from {remote.Url}");
                }
                catch (Exception e)
                {
                    log.LogWarning($"Fetch of {remote.Url} for {repo.Info.Path} failed: {e.ToString()}.  Are you offline?");
                }
            }
        }

        /// <summary>
        /// This is just a base function to recurse from.
        /// </summary>
        private static void SafeCheckoutByIndividualFiles(Repository repo, string commit, CheckoutOptions options, ILogger log)
        {
            log.LogDebug($"Beginning individual file checkout for {repo.Info.WorkingDirectory} at {commit}");
            SafeCheckoutTreeByIndividualFiles(repo, repo.Lookup(commit).Peel<Tree>(), "", commit, options, log);

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
        private static void SafeCheckoutTreeByIndividualFiles(Repository repo,
                                                              Tree tree,
                                                              string treePath,
                                                              string commit,
                                                              CheckoutOptions options,
                                                              ILogger log)
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
                    log.LogWarning($"Failed to checkout {Path.Combine(treePath, f.Path)} in {repo.Info.WorkingDirectory} at {commit}, skipping.  Exception: {e.ToString()}");
                    if (f.TargetType == TreeEntryTargetType.Tree)
                    {
                        SafeCheckoutTreeByIndividualFiles(repo, f.Target.Peel<Tree>(), Path.Combine(treePath, f.Path), commit, options, log);
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
        private static string ParseReference(Repository repo, string treeish, ILogger log)
        {
            Reference reference = null;
            GitObject dummy;
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

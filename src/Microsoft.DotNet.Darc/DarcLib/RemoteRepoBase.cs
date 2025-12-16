// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib;

public class RemoteRepoBase : GitRepoCloner
{
    private readonly ILogger _logger;
    private readonly IProcessManager _processManager;

    protected RemoteRepoBase(
        IRemoteTokenProvider remoteConfiguration,
        IProcessManager processManager,
        string temporaryRepositoryPath,
        IMemoryCache cache,
        ILogger logger)
        : base(remoteConfiguration, new LocalLibGit2Client(remoteConfiguration, new NoTelemetryRecorder(), processManager, new FileSystem(), logger), logger)
    {
        TemporaryRepositoryPath = temporaryRepositoryPath ?? Path.GetTempPath();
        Cache = cache;
        _logger = logger;
        _processManager = processManager;
    }

    /// <summary>
    ///     Location where repositories should be cloned.
    /// </summary>
    protected string TemporaryRepositoryPath { get; set; }

    /// <summary>
    /// Generic memory cache that may be supplied by the creator of the
    /// Remote for the purposes of caching remote responses.
    /// </summary>
    protected IMemoryCache Cache { get; set; }

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
    protected async Task CommitFilesAsync(
        List<GitFile> filesToCommit,
        string repoUri,
        string branch,
        string commitMessage,
        ILogger logger,
        string pat,
        string dotnetMaestroName,
        string dotnetMaestroEmail)
    {
        logger.LogInformation("Preparing changes to {branch}", branch);
        string tempRepoFolder = Path.Combine(TemporaryRepositoryPath, Path.GetRandomFileName());
        const string remote = "origin";
        try
        {
            string clonedRepo = null;

            logger.LogInformation("Sparse and shallow checkout of branch {branch} in {repoUri}...", branch, repoUri);
            clonedRepo = await SparseAndShallowCheckoutAsync(repoUri, branch, tempRepoFolder, remote, dotnetMaestroName, dotnetMaestroEmail, pat);

            foreach (GitFile file in filesToCommit)
            {
                Debug.Assert(file != null, "Passed in a null GitFile in filesToCommit");
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
                        await stream.WriteAsync(contentBytes);
                    }
                }
                else if (file.Operation == GitFileOperation.Delete)
                {
                    File.Delete(filePath);
                }

                await _processManager.ExecuteGit(clonedRepo, ["add", filePath, "-f"]);
            }

            var commitResult = await _processManager.ExecuteGit(clonedRepo, ["commit", "--allow-empty", "-m", commitMessage]);
            commitResult.ThrowIfFailed($"Failed to commit changes on branch '{branch}'");

            logger.LogInformation("Pushing branch {branch} to {remoteUri}", branch, remote);
            var pushResult = await _processManager.ExecuteGit(clonedRepo, ["-c", "core.askpass=", "-c", "credential.helper=", "push", remote, branch]);
            pushResult.ThrowIfFailed($"Failed to push changes to {remote}@{branch}");
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
                // .git/objects hierarchy are marked as read-only so we need to unset the read-only attribute otherwise an UnauthorizedAccessException is thrown.
                DependencyFileManager.NormalizeAttributes(tempRepoFolder);
                Directory.Delete(tempRepoFolder, true);
            }
            catch (DirectoryNotFoundException)
            {
                // If the directory wasn't found, that means that the clone operation above failed
                // but this error isn't interesting at all.
            }
            catch (Exception exc)
            {
                throw new Exception($"Something went wrong while trying to delete the folder {tempRepoFolder}", exc);
            }
        }
    }

    /// <summary>
    /// Since LibGit2Sharp doesn't support neither sparse checkout not shallow clone
    /// we implement the flow ourselves.
    /// </summary>
    /// <param name="repoUri">The repo to clone Uri</param>
    /// <param name="branch">The branch to checkout</param>
    /// <param name="workingDirectory">The working directory</param>
    /// <param name="remote">The name of the remote</param>
    /// <param name="user">User name</param>
    /// <param name="email">User's email</param>
    /// <param name="pat">User's personal access token</param>
    /// <param name="repoFolderName">The name of the folder where the repo is located</param>
    /// <returns>The full path of the cloned repo</returns>
    private async Task<string> SparseAndShallowCheckoutAsync(
        string repoUri,
        string branch,
        string workingDirectory,
        string remote,
        string user,
        string email,
        string pat,
        string repoFolderName = "clonedRepo")
    {
        Directory.CreateDirectory(workingDirectory);

        await ExecuteGitCommand(["init", repoFolderName], workingDirectory);

        workingDirectory = Path.Combine(workingDirectory, repoFolderName);
        repoUri = repoUri.Replace("https://", $"https://{user}:{pat}@");

        await ExecuteGitCommand(["remote", "add", remote, repoUri], workingDirectory, secretToMask: pat);
        await ExecuteGitCommand(["config", "core.sparsecheckout", "true"], workingDirectory);
        await ExecuteGitCommand(["config", "core.longpaths", "true"], workingDirectory);
        await ExecuteGitCommand(["config", "user.name", user], workingDirectory);
        await ExecuteGitCommand(["config", "user.email", email], workingDirectory);

        File.WriteAllLines(Path.Combine(workingDirectory, ".git/info/sparse-checkout"), ["eng/", ".config/", $"{VersionFiles.NugetConfigNames.First()}", $"{VersionFiles.GlobalJson}"]);

        await ExecuteGitCommand([$"-c", "core.askpass=", "-c", "credential.helper=", "pull", "--depth=1", remote, branch], workingDirectory, secretToMask: pat);
        await ExecuteGitCommand([$"checkout", branch], workingDirectory);

        return workingDirectory;
    }

    /// <summary>
    ///     Execute a git command
    /// </summary>
    /// <param name="arguments">Arguments to git</param>
    /// <param name="workingDirectory">Working directory</param>
    /// <param name="secretToMask">Mask this secret when calling the logger.</param>
    private async Task ExecuteGitCommand(string[] arguments, string workingDirectory, string secretToMask = null)
    {
        IEnumerable<string> maskedArguments = secretToMask == null ? arguments : arguments.Select(a => a.Replace(secretToMask, "***"));
        _logger.LogInformation("Executing command git {maskedArguments} in {workingDirectory}...", string.Join(' ', maskedArguments), workingDirectory);
        var result = await _processManager.ExecuteGit(workingDirectory, arguments);
        result.ThrowIfFailed("Failed to execute git command");
    }

    private static byte[] GetUtf8ContentBytes(string content, ContentEncoding encoding) => encoding switch
    {
        ContentEncoding.Base64 => Convert.FromBase64String(content),
        ContentEncoding.Utf8 => Encoding.UTF8.GetBytes(content),
        _ => throw new NotImplementedException("Unexpected content encoding."),
    };
}

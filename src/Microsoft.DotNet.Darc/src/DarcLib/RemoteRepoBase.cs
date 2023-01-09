// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib;

public class RemoteRepoBase : GitRepoCloner
{
    protected RemoteRepoBase(string gitExecutable, string temporaryRepositoryPath, IMemoryCache cache, ILogger logger, string accessToken)
        : base(accessToken, logger)
    {
        TemporaryRepositoryPath = temporaryRepositoryPath;
        GitExecutable = gitExecutable;
        Cache = cache;
    }

    /// <summary>
    ///     Location of the git executable. Can be "git" or full path to temporary download location
    ///     used in the Maestro context.
    /// </summary>
    protected string GitExecutable { get; set; }

    /// <summary>
    ///     Location where repositories should be cloned.
    /// </summary>
    protected string TemporaryRepositoryPath { get; set; }
        
    /// <summary>
    /// Generic memory cache that may be supplied by the creator of the
    /// Remote for the purposes of caching remote responses.
    /// </summary>
    protected IMemoryCache Cache { get; set;}

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
        ILogger logger,
        string pat,
        string dotnetMaestroName,
        string dotnetMaestroEmail)
    {
        logger.LogInformation("Pushing files to {branch}", branch);
        string tempRepoFolder = Path.Combine(TemporaryRepositoryPath, Path.GetRandomFileName());
        string remote = "origin";
        try
        {
            string clonedRepo = null;

            logger.LogInformation("Sparse and shallow checkout of branch {branch} in {repoUri}...", branch, repoUri);
            clonedRepo = LocalHelpers.SparseAndShallowCheckout(GitExecutable, repoUri, branch, tempRepoFolder, logger, remote, dotnetMaestroName, dotnetMaestroEmail, pat);

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
                        await stream.WriteAsync(contentBytes, 0, contentBytes.Length);
                    }
                }
                else if (file.Operation == GitFileOperation.Delete)
                {
                    File.Delete(filePath);
                }

                LocalHelpers.ExecuteCommand(GitExecutable, $"add {filePath}", logger, clonedRepo);
            }

            LocalHelpers.ExecuteCommand(GitExecutable, $"commit -m \"{commitMessage}\"", logger, clonedRepo);
            LocalHelpers.ExecuteCommand(GitExecutable, $"-c core.askpass= -c credential.helper= push {remote} {branch}", logger, clonedRepo);
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
                GitFileManager.NormalizeAttributes(tempRepoFolder);
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

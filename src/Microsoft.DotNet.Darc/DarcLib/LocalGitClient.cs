// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// This class can manage a local git repository.
/// It is deliberately not using LibGit2Sharp (for memory reasons) but instead calls git out of process.
/// TODO https://github.com/dotnet/arcade-services/issues/2982: CommitFilesAsync still uses LibGit2Sharp (though it's not used in the VMR flows)
/// </summary>
public class LocalGitClient : ILocalGitClient
{
    private readonly RemoteConfiguration _remoteConfiguration;
    private readonly IProcessManager _processManager;
    private readonly ILogger _logger;

    /// <summary>
    ///     Construct a new local git client
    /// </summary>
    /// <param name="path">Current path</param>
    public LocalGitClient(RemoteConfiguration remoteConfiguration, IProcessManager processManager, ILogger logger)
    {
        _remoteConfiguration = remoteConfiguration;
        _processManager = processManager;
        _logger = logger;
    }

    public async Task<string> GetFileContentsAsync(string relativeFilePath, string repoPath, string branch)
    {
        string fullPath = Path.Combine(repoPath, relativeFilePath);
        if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
        {
            string? parentTwoDirectoriesUp = Path.GetDirectoryName(Path.GetDirectoryName(fullPath));
            if (parentTwoDirectoriesUp != null && Directory.Exists(parentTwoDirectoriesUp))
            {
                throw new DependencyFileNotFoundException($"Found parent-directory path ('{parentTwoDirectoriesUp}') but unable to find specified file ('{relativeFilePath}')");
            }
            else
            {
                throw new DependencyFileNotFoundException($"Neither parent-directory path ('{parentTwoDirectoriesUp}') nor specified file ('{relativeFilePath}') found.");
            }
        }

        if (!File.Exists(fullPath))
        {
            throw new DependencyFileNotFoundException($"Could not find {fullPath}");
        }

        using (var streamReader = new StreamReader(fullPath))
        {
            return await streamReader.ReadToEndAsync();
        }
    }

    public async Task CommitFilesAsync(List<GitFile> filesToCommit, string repoPath, string branch, string commitMessage)
    {
        repoPath = await GetRootDirAsync(repoPath);

        try
        {
            using (var localRepo = new Repository(repoPath))
                foreach (GitFile file in filesToCommit)
                {
                    Debug.Assert(file != null, $"Passed in a null {nameof(GitFile)} in {nameof(filesToCommit)}");
                    switch (file.Operation)
                    {
                        case GitFileOperation.Add:
                            var parentDirectoryInfo = Directory.GetParent(file.FilePath)
                                ?? throw new Exception($"Cannot find parent directory of {file.FilePath}.");

                            string parentDirectory = parentDirectoryInfo.FullName;

                            if (!Directory.Exists(parentDirectory))
                            {
                                Directory.CreateDirectory(parentDirectory);
                            }

                            string fullPath = Path.Combine(repoPath, file.FilePath);
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
                                finalContent = await NormalizeLineEndingsAsync(repoPath, fullPath, finalContent);
                                await streamWriter.WriteAsync(finalContent);

                                AddFileToIndex(localRepo, file, fullPath);
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
        catch (Exception exc)
        {
            throw new DarcException($"Something went wrong when checking out {repoPath} in {repoPath}", exc);
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
    private async Task<string> NormalizeLineEndingsAsync(string repoPath, string filePath, string content)
    {
        const string crlf = "\r\n";
        const string lf = "\n";

        // Check gitAttributes to determine whether the file has eof handling set.
        var result = await _processManager.ExecuteGit(repoPath, new[] { "check-attr", "eol", "--", filePath });
        result.ThrowIfFailed($"Failed to determine eol for {filePath}");

        string eofAttr = result.StandardOutput.Trim();

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

    public async Task CheckoutAsync(string repoPath, string refToCheckout)
    {
        var result = await _processManager.ExecuteGit(repoPath, new[] { "checkout", refToCheckout });
        result.ThrowIfFailed($"Failed to check out {refToCheckout} in {repoPath}");
    }

    public async Task CreateBranchAsync(string repoPath, string branchName, bool overwriteExistingBranch = false)
    {
        var args = new[] { "checkout", overwriteExistingBranch ? "-B" : "-b", branchName };
        var result = await _processManager.ExecuteGit(repoPath, args);
        result.ThrowIfFailed($"Failed to create {branchName} in {repoPath}");
    }

    public async Task CommitAsync(
        string repoPath,
        string message,
        bool allowEmpty,
        (string Name, string Email)? author = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<string> args = new[] { "commit", "-m", message };

        if (allowEmpty)
        {
            args = args.Append("--allow-empty");
        }

        author ??= (Constants.DarcBotName, Constants.DarcBotEmail);

        args = args
            .Append("--author")
            .Append($"{author.Value.Name} <{author.Value.Email}>");

        var result = await _processManager.ExecuteGit(repoPath, args, cancellationToken: cancellationToken);
        result.ThrowIfFailed($"Failed to commit {repoPath}");
    }

    public async Task StageAsync(string repoPath, IEnumerable<string> pathsToStage, CancellationToken cancellationToken = default)
    {
        var result = await _processManager.ExecuteGit(repoPath, pathsToStage.Prepend("add"), cancellationToken: cancellationToken);
        result.ThrowIfFailed($"Failed to stage {string.Join(", ", pathsToStage)} in {repoPath}");
    }

    public async Task<string> GetRootDirAsync(string? repoPath = null, CancellationToken cancellationToken = default)
    {
        var result = await _processManager.ExecuteGit(repoPath ?? Environment.CurrentDirectory, new[] { "rev-parse", "--show-toplevel" }, cancellationToken: cancellationToken);
        result.ThrowIfFailed("Root directory of the repo was not found. Check that git is installed and that you are in a folder which is a git repo (.git folder should be present).");
        return result.StandardOutput.Trim();
    }

    /// <summary>
    ///     Get the current git commit sha.
    /// </summary>
    public async Task<string> GetGitCommitAsync(string? repoPath = null, CancellationToken cancellationToken = default)
    {
        repoPath ??= Environment.CurrentDirectory;

        var result = await _processManager.ExecuteGit(repoPath, new[] { "rev-parse", "HEAD" }, cancellationToken: cancellationToken);
        result.ThrowIfFailed("Commit was not resolved. Check if git is installed and that a .git directory exists in the root of your repository.");
        return result.StandardOutput.Trim();
    }

    public async Task<string> GetShaForRefAsync(string repoPath, string? gitRef)
    {
        if (gitRef != null && Constants.EmptyGitObject.StartsWith(gitRef))
        {
            return gitRef;
        }

        var args = new[]
        {
            "rev-parse",
            gitRef ?? Constants.HEAD,
        };

        var result = await _processManager.ExecuteGit(repoPath, args);
        result.ThrowIfFailed($"Failed to find commit {gitRef} in {repoPath}");

        return result.StandardOutput.Trim();
    }

    /// <summary>
    ///     Add a remote to a local repo if does not already exist.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="repoUrl">URL of the remote to add</param>
    /// <returns>Name of the remote</returns>
    public async Task<string> AddRemoteIfMissingAsync(string repoPath, string repoUrl, CancellationToken cancellationToken = default)
    {
        var result = await _processManager.ExecuteGit(repoPath, new[] { "remote", "-v" }, cancellationToken: cancellationToken);
        result.ThrowIfFailed($"Failed to get remotes for {repoPath}");

        string? remoteName = null;

        foreach (var line in result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var name = parts[0];
            var url = parts[1];

            if (url == repoUrl)
            {
                remoteName = name;
                break;
            }
        }

        if (remoteName == null)
        {
            _logger.LogDebug($"Adding {repoUrl} remote to {repoPath}");

            // Remote names don't matter much but should be stable
            remoteName = StringUtils.GetXxHash64(repoUrl);

            result = await _processManager.ExecuteGit(repoPath, new[] { "remote", "add", remoteName, repoUrl }, cancellationToken: cancellationToken);
            result.ThrowIfFailed($"Failed to add remote {remoteName} ({repoUrl}) to {repoPath}");
        }

        return remoteName;
    }

    public async Task<List<GitSubmoduleInfo>> GetGitSubmodulesAsync(string repoPath, string commit)
    {
        var submodules = new List<GitSubmoduleInfo>();

        if (commit == Constants.EmptyGitObject)
        {
            return submodules;
        }

        var submoduleFile = await GetFileFromGitAsync(repoPath, ".gitmodules", commit);
        if (submoduleFile == null)
        {
            return submodules;
        }

        GitSubmoduleInfo? currentSubmodule = null;

        var lines = submoduleFile
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim());

        var submoduleRegex = new Regex("^\\[submodule \"(?<name>.+)\"\\]$");
        var submoduleUrlRegex = new Regex("^\\s*url\\s*=\\s*(?<url>.+)$");
        var submodulePathRegex = new Regex("^\\s*path\\s*=\\s*(?<path>.+)$");

        async Task FinalizeSubmodule(GitSubmoduleInfo submodule)
        {
            if (submodule.Url == null)
            {
                throw new Exception($"Submodule {submodule.Name} has no URL");
            }

            if (submodule.Path == null)
            {
                throw new Exception($"Submodule {submodule.Name} has no path");
            }

            // Read SHA that the submodule points to
            var result = await _processManager.ExecuteGit(repoPath, "rev-parse", $"{commit}:{submodule.Path}");
            result.ThrowIfFailed($"Failed to find SHA of commit where submodule {submodule.Path} points to");

            submodule = submodule with
            {
                Commit = result.StandardOutput.Trim(),
            };

            submodules.Add(submodule);
        }

        foreach (var line in lines)
        {
            var match = submoduleRegex.Match(line);
            if (match.Success)
            {
                if (currentSubmodule != null)
                {
                    await FinalizeSubmodule(currentSubmodule);
                }

                currentSubmodule = new GitSubmoduleInfo(match.Groups["name"].Value, null!, null!, null!);
                continue;
            }

            match = submoduleUrlRegex.Match(line);
            if (match.Success)
            {
                currentSubmodule = currentSubmodule! with { Url = match.Groups["url"].Value };
                continue;
            }

            match = submodulePathRegex.Match(line);
            if (match.Success)
            {
                currentSubmodule = currentSubmodule! with { Path = match.Groups["path"].Value };
                continue;
            }
        }

        if (currentSubmodule != null)
        {
            await FinalizeSubmodule(currentSubmodule);
        }

        return submodules;
    }

    public async Task<string[]> GetStagedFiles(string repoPath)
    {
        var result = await _processManager.ExecuteGit(repoPath, "git", "diff", "--name-only", "--cached");
        result.ThrowIfFailed($"Failed to get staged files in {repoPath}");

        return result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task<string?> GetFileFromGitAsync(string repoPath, string relativeFilePath, string revision = "HEAD", string? outputPath = null)
    {
        var args = new List<string>
        {
            "show",
            $"{revision}:{relativeFilePath}"
        };

        if (outputPath != null)
        {
            args.Add("--output");
            args.Add(outputPath);
        }

        var result = await _processManager.ExecuteGit(repoPath, args);

        if (!result.Succeeded)
        {
            return null;
        }

        return result.StandardOutput;
    }

    public async Task<string> FetchAsync(string repoPath, string remoteUri, CancellationToken cancellationToken = default)
    {
        var args = new List<string>();
        var envVars = new Dictionary<string, string>
        {
            { "GIT_TERMINAL_PROMPT", "0" }
        };

        string? token = _remoteConfiguration.GetTokenForUri(remoteUri);

        if (!string.IsNullOrEmpty(token))
        {
            const string ENV_VAR_NAME = "GIT_REMOTE_PAT";
            args.Add($"--config-env=http.extraheader={ENV_VAR_NAME}");
            envVars[ENV_VAR_NAME] = GitNativeRepoCloner.GetAuthorizationHeaderArgument(token);
        }

        args.Add("fetch");
        args.Add(remoteUri);

        var result = await _processManager.ExecuteGit(repoPath, args, envVars, cancellationToken);
        result.ThrowIfFailed($"Failed to fetch from {remoteUri} in {repoPath}");
        return result.StandardOutput.Trim();
    }

    public async Task<string> BlameLineAsync(string repoPath, string relativeFilePath, int line)
    {
        var args = new[]
        {
            "blame",
            "--first-parent",
            "-slL",
            $"{line},{line}",
            relativeFilePath,
        };

        var result = await _processManager.ExecuteGit(repoPath, args);
        result.ThrowIfFailed($"Failed to blame line {line} of {repoPath}{Path.DirectorySeparatorChar}{relativeFilePath}");
        return result.StandardOutput.Trim().Split(' ').First();
    }
}

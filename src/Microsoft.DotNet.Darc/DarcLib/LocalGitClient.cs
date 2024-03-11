// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// This class can manage a local git repository.
/// It is deliberately not using LibGit2Sharp (for memory reasons) but instead calls git out of process.
/// </summary>
public class LocalGitClient : ILocalGitClient
{
    private readonly RemoteConfiguration _remoteConfiguration;
    private readonly IProcessManager _processManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger _logger;

    public LocalGitClient(
        RemoteConfiguration remoteConfiguration,
        IProcessManager processManager,
        IFileSystem fileSystem,
        ILogger logger)
    {
        _remoteConfiguration = remoteConfiguration;
        _processManager = processManager;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<string> GetFileContentsAsync(string relativeFilePath, string repoPath, string branch)
    {
        // Load non-working-tree version
        if (!string.IsNullOrEmpty(branch))
        {
            return await GetFileFromGitAsync(repoPath, relativeFilePath, branch)
                ?? throw new DependencyFileNotFoundException($"Could not find {relativeFilePath} in {repoPath}");
        }

        string fullPath = new NativePath(repoPath) / relativeFilePath;
        if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
        {
            var parentTwoDirectoriesUp = Path.GetDirectoryName(Path.GetDirectoryName(fullPath));
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

    public async Task CheckoutAsync(string repoPath, string refToCheckout)
    {
        var result = await _processManager.ExecuteGit(repoPath, ["checkout", refToCheckout]);
        result.ThrowIfFailed($"Failed to check out {refToCheckout} in {repoPath}");
    }

    public async Task ResetWorkingTree(NativePath repoPath, UnixPath? relativePath = null)
    {
        relativePath ??= UnixPath.CurrentDir;

        // After we apply the diff to the index, working tree won't have the files so they will be missing
        // We have to reset working tree to the index then
        // This will end up having the working tree match what is staged
        _logger.LogInformation("Cleaning the working tree directory {path}...", repoPath / relativePath);
        var args = new string[] { "checkout", relativePath };
        var result = await _processManager.ExecuteGit(repoPath, args, cancellationToken: CancellationToken.None);

        if (result.Succeeded)
        {
            _logger.LogDebug("{output}", result.ToString());
        }
        else if (result.StandardError.Contains($"pathspec '{relativePath}' did not match any file(s) known to git"))
        {
            // No files in the directory
            if (relativePath == UnixPath.CurrentDir)
            {
                _logger.LogDebug("Failed to reset working tree of {repo} as it was empty", repoPath);
            }
            else
            {
                // In case a submodule was removed, it won't be in the index anymore and the checkout will fail
                // We can just remove the working tree folder then
                _logger.LogInformation("A removed submodule detected. Removing files at {path}...", relativePath);
                _fileSystem.DeleteDirectory(repoPath / relativePath, true);
            }
        }

        // Also remove untracked files (in case files were removed in index)
        result = await _processManager.ExecuteGit(repoPath, ["clean", "-df", relativePath], cancellationToken: CancellationToken.None);
        result.ThrowIfFailed("Failed to clean the working tree!");
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
        var result = await _processManager.ExecuteGit(repoPath ?? Environment.CurrentDirectory, ["rev-parse", "--show-toplevel"], cancellationToken: cancellationToken);
        result.ThrowIfFailed("Root directory of the repo was not found. Check that git is installed and that you are in a folder which is a git repo (.git folder should be present).");
        return result.StandardOutput.Trim();
    }

    /// <summary>
    ///     Get the current git commit sha.
    /// </summary>
    public async Task<string> GetGitCommitAsync(string? repoPath = null, CancellationToken cancellationToken = default)
    {
        repoPath ??= Environment.CurrentDirectory;

        var result = await _processManager.ExecuteGit(repoPath, ["rev-parse", "HEAD"], cancellationToken: cancellationToken);
        result.ThrowIfFailed("Commit was not resolved. Check if git is installed and that a .git directory exists in the root of your repository.");
        return result.StandardOutput.Trim();
    }

    public async Task<string> GetShaForRefAsync(string repoPath, string? gitRef = null)
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

    public async Task<GitObjectType> GetObjectTypeAsync(string repoPath, string objectSha)
    {
        var args = new[]
        {
            "cat-file",
            "-t",
            objectSha,
        };

        var result = await _processManager.ExecuteGit(repoPath, args);
        result.ThrowIfFailed($"Failed to find object {objectSha} in {repoPath}");

        return result.StandardOutput.Trim() switch
        {
            "commit" => GitObjectType.Commit,
            "blob" => GitObjectType.Blob,
            "tree" => GitObjectType.Tree,
            "tag" => GitObjectType.Tag,
            _ => GitObjectType.Unknown,
        };
    }

    /// <summary>
    ///     Add a remote to a local repo if does not already exist.
    /// </summary>
    /// <param name="repoPath">Path to a git repository</param>
    /// <param name="repoUrl">URL of the remote to add</param>
    /// <returns>Name of the remote</returns>
    public async Task<string> AddRemoteIfMissingAsync(string repoPath, string repoUrl, CancellationToken cancellationToken = default)
    {
        var result = await _processManager.ExecuteGit(repoPath, ["remote", "-v"], cancellationToken: cancellationToken);
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

            result = await _processManager.ExecuteGit(repoPath, ["remote", "add", remoteName, repoUrl], cancellationToken: cancellationToken);
            result.ThrowIfFailed($"Failed to add remote {remoteName} ({repoUrl}) to {repoPath}");
        }

        return remoteName;
    }

    public async Task UpdateRemoteAsync(string repoPath, string remoteName, CancellationToken cancellationToken = default)
    {
        var result = await _processManager.ExecuteGit(repoPath, ["ls-remote", "--get-url", remoteName], cancellationToken: cancellationToken);
        result.ThrowIfFailed($"No remote named {remoteName} in {repoPath}");
        var remoteUri = result.StandardOutput.Trim();

        List<string> args = [ "remote", "update", remoteName ];
        var envVars = new Dictionary<string, string>();
        AddGitAuthHeader(args, envVars, remoteUri);

        result = await _processManager.ExecuteGit(repoPath, args, envVars, cancellationToken: cancellationToken);
        result.ThrowIfFailed($"Failed to update {repoPath} from remote {remoteName}");

        args = [ "fetch", "--tags", "--force", remoteName ];
        envVars = [];
        AddGitAuthHeader(args, envVars, remoteUri);

        result = await _processManager.ExecuteGit(repoPath, args, envVars, cancellationToken: cancellationToken);
        result.ThrowIfFailed($"Failed to update {repoPath} from remote {remoteName}");
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
        var result = await _processManager.ExecuteGit(repoPath, "diff", "--name-only", "--cached");
        result.ThrowIfFailed($"Failed to get staged files in {repoPath}");

        return result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task<string?> GetFileFromGitAsync(string repoPath, string relativeFilePath, string revision = "HEAD", string? outputPath = null)
    {
        var args = new List<string>
        {
            "show",
            $"{revision}:{relativeFilePath.TrimStart('/')}"
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

    public async Task<string> BlameLineAsync(string repoPath, string relativeFilePath, int line, string? blameFromCommit = null)
    {
        var args = new List<string>
        {
            "blame",
            "--first-parent",
            blameFromCommit != null ? blameFromCommit + '^' : Constants.HEAD,
            "-slL",
            $"{line},{line}",
            relativeFilePath,
        };

        var result = await _processManager.ExecuteGit(repoPath, args);
        result.ThrowIfFailed($"Failed to blame line {line} of {repoPath}{Path.DirectorySeparatorChar}{relativeFilePath}");
        return result.StandardOutput.Trim().Split(' ').First();
    }

    public void AddGitAuthHeader(IList<string> args, IDictionary<string, string> envVars, string repoUri)
    {
        var token = _remoteConfiguration.GetTokenForUri(repoUri);

        if (token == null)
        {
            return;
        }

        var repoType = GitRepoUrlParser.ParseTypeFromUri(repoUri);
        if (repoType == GitRepoType.None)
        {
            return;
        }

        const string ENV_VAR_NAME = "GIT_REMOTE_PAT";
        // Must be before the executed option
        args.Insert(0, $"--config-env=http.extraheader={ENV_VAR_NAME}");
        envVars[ENV_VAR_NAME] = repoType switch
        {
            GitRepoType.GitHub => $"Authorization: Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Constants.GitHubBotUserName}:{token}"))}",
            GitRepoType.AzureDevOps => $"Authorization: Bearer {token}",
            GitRepoType.Local => token,
            GitRepoType t => throw new Exception($"Cannot set authorization header for repo of type {t}"),
        };
        envVars["GIT_TERMINAL_PROMPT"] = "0";
    }

    public async Task<ProcessExecutionResult> RunGitCommandAsync(
        string repoPath,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        return await _processManager.ExecuteGit(repoPath, args, cancellationToken: cancellationToken);
    }
}

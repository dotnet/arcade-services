// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.SourceControl.WebApi;

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

    public async Task UpdateRemoteAsync(string repoPath, string remoteName, CancellationToken cancellationToken = default)
    {
        var result = await _processManager.ExecuteGit(repoPath, new[] { "remote", "update", remoteName }, cancellationToken: cancellationToken);
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

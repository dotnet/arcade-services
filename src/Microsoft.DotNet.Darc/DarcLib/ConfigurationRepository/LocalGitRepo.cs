// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib.ConfigurationRepository;

public class LocalGitRepo : MaestroConfiguration.Client.IGitRepo
{
    private readonly IGitRepo _gitRepo;
    private readonly ILocalGitRepo _localGitRepo;
    private readonly ILogger<LocalGitRepo> _logger;

    public LocalGitRepo(IGitRepo gitRepo, ILocalGitRepo localGitRepo, ILogger<LocalGitRepo> logger)
    {
        _gitRepo = gitRepo;
        _localGitRepo = localGitRepo;
        _logger = logger;
    }

    public async Task CommitFilesAsync(string repositoryUri, string branchName, IReadOnlyList<MaestroConfiguration.Client.GitFile> files, string commitMessage)
    {
        var darclibGitFiles = files.Select(f => new Helpers.GitFile(new UnixPath(repositoryUri) / f.Path, f.Content)).ToList();
        await _gitRepo.CommitFilesAsync(darclibGitFiles, repositoryUri, branchName, commitMessage);
        await _localGitRepo.StageAsync(["."]);
        await _localGitRepo.CommitAsync(commitMessage, allowEmpty: false);
    }

    public async Task CreateBranchAsync(string repositoryUri, string branch, string baseBranch)
        => await _gitRepo.CreateBranchAsync(repositoryUri, branch, baseBranch);

    public Task<string> CreatePullRequestAsync(string repositoryUri, string headBranch, string baseBranch, string prTitle, string prDescription = null)
    {
        _logger.LogWarning("Cannot create pull request when using local git repository.");
        return Task.FromResult(string.Empty);
    }

    public async Task DeleteFileAsync(string repositoryUri, string branch, string filePath, string commitMessage)
    {
        await _gitRepo.CommitFilesAsync(
            [new Helpers.GitFile(new UnixPath(repositoryUri) / filePath, string.Empty, ContentEncoding.Utf8, operation: GitFileOperation.Delete)],
            repositoryUri,
            branch,
            commitMessage);
        await _localGitRepo.StageAsync(["."]);
        await _localGitRepo.CommitAsync(commitMessage, allowEmpty: false);
    }

    public async Task<bool> DoesBranchExistAsync(string repositoryUri, string branchName)
        => await _gitRepo.DoesBranchExistAsync(repositoryUri, branchName);


    public async Task<string> GetFileContentsAsync(string repositoryUri, string branch, string filePath)
    {
        try
        {
            return await _gitRepo.GetFileContentsAsync(filePath, repositoryUri, branch);
        }
        catch (DependencyFileNotFoundException)
        {
            throw new FileNotFoundInRepoException(repositoryUri, branch, filePath);
        }
    }

    public async Task<List<MaestroConfiguration.Client.GitFile>> GetFilesContentAsync(string repositoryUri, string branch, string path)
    {
        var filePaths = await LsTreeBlobsAsync(repositoryUri, branch, path, recursive: true);

        var gitFiles = new List<MaestroConfiguration.Client.GitFile>();
        foreach (var fileName in filePaths)
        {
            var content = await GetFileContentsAsync(repositoryUri, branch, fileName);
            gitFiles.Add(new MaestroConfiguration.Client.GitFile(fileName, content));
        }

        return gitFiles;
    }

    public async Task<List<string>> ListBlobsAsync(string repositoryUri, string branch, string path)
        => await LsTreeBlobsAsync(repositoryUri, branch, path, recursive: false);

    private async Task<List<string>> LsTreeBlobsAsync(string repositoryUri, string branch, string path, bool recursive)
    {
        var args = new List<string> { "ls-tree", "--format=%(objecttype) %(path)" };
        if (recursive)
        {
            args.Add("-r");
        }
        args.Add(branch);
        args.Add(path + Path.DirectorySeparatorChar);
        return (await _localGitRepo.ExecuteGitCommand(args.ToArray()))
            .GetOutputLines()
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts[0] == "blob")
            .Select(parts => parts[1])
            .ToList();
    }

    public async Task<bool> RepoExistsAsync(string repositoryUri)
        => await _gitRepo.RepoExistsAsync(repositoryUri);
}

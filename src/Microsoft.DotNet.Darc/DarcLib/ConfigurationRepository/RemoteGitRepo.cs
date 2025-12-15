// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.MaestroConfiguration.Client;

#nullable enable
namespace Microsoft.DotNet.DarcLib.ConfigurationRepository;

public class RemoteGitRepo : MaestroConfiguration.Client.IGitRepo
{
    private readonly IGitRepo _gitRepo;
    private readonly IRemote _remote;

    public RemoteGitRepo(IGitRepo gitRepo, IRemote remote)
    {
        _remote = remote;
        _gitRepo = gitRepo;
    }

    public async Task CommitFilesAsync(string repositoryUri, string branchName, IReadOnlyList<MaestroConfiguration.Client.GitFile> files, string commitMessage)
        => await _remote.CommitUpdatesWithNoCloningAsync(
                files.Select(f => new Helpers.GitFile(f.Path, f.Content)).ToList(),
                repositoryUri,
                branchName,
                commitMessage);

    public async Task CreateBranchAsync(string repositoryUri, string branch, string baseBranch)
        => await _gitRepo.CreateBranchAsync(repositoryUri, branch, baseBranch);

    public async Task<string> CreatePullRequestAsync(string repositoryUri, string headBranch, string baseBranch, string prTitle, string? prDescription = null)
    {
        var pr = await _remote.CreatePullRequestAsync(
            repositoryUri,
            new PullRequest
            {
                BaseBranch = baseBranch,
                HeadBranch = headBranch,
                Title = prTitle,
                Description = prDescription
            });
        return pr.Url;
    }

    public async Task DeleteFileAsync(string repositoryUri, string branch, string filePath, string commitMessage)
        => await _remote.CommitUpdatesWithNoCloningAsync(
            [new Helpers.GitFile(filePath, string.Empty, ContentEncoding.Utf8, operation: GitFileOperation.Delete)],
            repositoryUri,
            branch,
            commitMessage);

    public async Task<bool> DoesBranchExistAsync(string repositoryUri, string branchName)
        => await _gitRepo.DoesBranchExistAsync(repositoryUri, branchName);

    public async Task<string> GetFileContentsAsync(string repositoryUri, string configurationBranch, string filePath)
    {
        try
        {
            return await _gitRepo.GetFileContentsAsync(filePath, repositoryUri, configurationBranch);
        }
        catch (DependencyFileNotFoundException)
        {
            throw new FileNotFoundInRepoException(repositoryUri, configurationBranch, filePath);
        }
    }

    public async Task<List<MaestroConfiguration.Client.GitFile>> GetFilesContentAsync(string repositoryUri, string branch, string path)
        => (await _remote.GetFilesAtCommitAsync(repositoryUri, await _remote.GetLatestCommitAsync(repositoryUri, branch), path))
                .Select(f => new MaestroConfiguration.Client.GitFile(f.FilePath, f.Content))
                .ToList();

    public async Task<List<string>> ListBlobsAsync(string repositoryUri, string branch, string path)
        => await _remote.ListFilesAtCommitAsync(repositoryUri, await _remote.GetLatestCommitAsync(repositoryUri, branch), path);

    public async Task<bool> RepoExistsAsync(string repositoryUri)
        => await _gitRepo.RepoExistsAsync(repositoryUri);
}

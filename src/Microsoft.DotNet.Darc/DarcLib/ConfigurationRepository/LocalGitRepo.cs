// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.MaestroConfiguration.Client;

namespace Microsoft.DotNet.DarcLib.ConfigurationRepository;

public class LocalGitRepo : MaestroConfiguration.Client.IGitRepo
{
    private readonly IGitRepo _gitRepo;
    private readonly ILocalGitRepo _localGitRepo;

    public LocalGitRepo(IGitRepo gitRepo, ILocalGitRepo localGitRepo)
    {
        _gitRepo = gitRepo;
        _localGitRepo = localGitRepo;
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
        => throw new InvalidOperationException("Cannot create pull request when using local git repository.");

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

    public async Task<bool> RepoExistsAsync(string repositoryUri)
        => await _gitRepo.RepoExistsAsync(repositoryUri);
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface IWorkBranchFactory
{
    Task<IWorkBranch> CreateWorkBranchAsync(string repoDir, string branchName);
}

public class WorkBranchFactory : IWorkBranchFactory
{
    private readonly ILocalGitClient _localGitClient;
    private readonly IProcessManager _processManager;
    private readonly ILogger<WorkBranch> _logger;

    public WorkBranchFactory(ILocalGitClient localGitClient, IProcessManager processManager, ILogger<WorkBranch> logger)
    {
        _localGitClient = localGitClient;
        _processManager = processManager;
        _logger = logger;
    }

    public async Task<IWorkBranch> CreateWorkBranchAsync(string repoDir, string branchName)
    {
        var result = await _processManager.ExecuteGit(repoDir, "rev-parse", "--abbrev-ref", "HEAD");
        result.ThrowIfFailed("Failed to determine the current branch");

        var originalBranch = result.StandardOutput.Trim();

        if (originalBranch == branchName)
        {
            var message = $"You are already on branch {branchName}. " +
                            "Previous sync probably failed and left the branch unmerged. " +
                            "To complete the sync checkout the original branch and try again.";

            throw new Exception(message);
        }

        _logger.LogInformation("Creating a temporary work branch {branchName}", branchName);

        await _localGitClient.CreateBranchAsync(repoDir, branchName, overwriteExistingBranch: true);

        return new WorkBranch(_localGitClient, _processManager, _logger, repoDir, originalBranch, branchName);
    }
}

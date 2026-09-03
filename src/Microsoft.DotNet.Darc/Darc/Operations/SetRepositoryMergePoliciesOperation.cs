// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Maestro.Common;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Models.PopUps;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class SetRepositoryMergePoliciesOperation : ConfigurationManagementOperationBase
{
    private readonly SetRepositoryMergePoliciesCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly IRemoteFactory _remoteFactory;
    private readonly IConfigurationRepositoryManager _configurationRepositoryManager;
    private readonly ILogger<SetRepositoryMergePoliciesOperation> _logger;

    public SetRepositoryMergePoliciesOperation(
        SetRepositoryMergePoliciesCommandLineOptions options,
        IBarApiClient barClient,
        IRemoteFactory remoteFactory,
        IConfigurationRepositoryManager configurationRepositoryManager,
        ILogger<SetRepositoryMergePoliciesOperation> logger)
        : base(options, logger)
    {
        _options = options;
        _barClient = barClient;
        _remoteFactory = remoteFactory;
        _configurationRepositoryManager = configurationRepositoryManager;
        _logger = logger;
    }

    protected override async Task<int> ExecuteInternalAsync()
    {
        var repoType = GitRepoUrlUtils.ParseTypeFromUri(_options.Repository);
        if (repoType == GitRepoType.Local || repoType == GitRepoType.None)
        {
            Console.WriteLine("Please specify full repository URL (GitHub or AzDO)");
            return Constants.ErrorCode;
        }

        string repository = _options.Repository;
        string branch = _options.Branch;
        bool mergePrs = _options.MergePrs ?? false;
        List<string> ignoredChecks = _options.IgnoreChecks?.ToList() ?? [];

        // If in quiet (non-interactive mode), ensure that all options were passed, then
        // just call the remote API
        if (_options.Quiet)
        {
            if (string.IsNullOrEmpty(repository) ||
                string.IsNullOrEmpty(branch) ||
                !_options.MergePrs.HasValue)
            {
                _logger.LogError("Missing input parameters for repository merge settings. Please see command help or remove --quiet/-q for interactive mode");
                return Constants.ErrorCode;
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(repository) && !string.IsNullOrEmpty(branch))
            {
                RepositoryBranch existingRepositoryBranch = await TryGetRepositoryBranchAsync(repository, branch);
                if (existingRepositoryBranch != null)
                {
                    mergePrs = _options.MergePrs ?? existingRepositoryBranch.MergePrs;
                    if (_options.IgnoreChecks == null || _options.IgnoreChecks.Count == 0)
                    {
                        ignoredChecks = [.. existingRepositoryBranch.IgnoredChecks];
                    }
                }
            }

            var initEditorPopUp = new SetRepositoryMergePoliciesPopUp("set-policies/set-policies-todo",
                _logger,
                repository,
                branch,
                mergePrs,
                ignoredChecks);

            var uxManager = new UxManager(_options.GitLocation, _logger);
            int exitCode = uxManager.PopUp(initEditorPopUp);
            if (exitCode != Constants.SuccessCode)
            {
                return exitCode;
            }
            repository = initEditorPopUp.Repository;
            branch = initEditorPopUp.Branch;
            mergePrs = initEditorPopUp.MergePrs;
            ignoredChecks = [.. initEditorPopUp.IgnoredChecks];
        }

        IRemote verifyRemote = await _remoteFactory.CreateRemoteAsync(repository);

        bool branchExistsOnRepo;
        try
        {
            branchExistsOnRepo = await UxHelpers.VerifyAndConfirmBranchExistsAsync(
                verifyRemote,
                repository,
                branch,
                !_options.Quiet);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogError("Your GitHub or Azure DevOps authentication seems to be invalid." +
                "Please see https://github.com/dotnet/arcade/blob/main/Documentation/Darc.md#step-3-set-additional-pats-for-azure-devops-and-github-operations" +
                "Make sure your authentication or access token is enabled for the organization associated with the repository `{repo}`.", repository);
            return Constants.ErrorCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying branch existence for {repo}@{branch}", repository, branch);
            return Constants.ErrorCode;
        }

        if (!branchExistsOnRepo)
        {
            Console.WriteLine("Aborting repository merge configuration.");
            return Constants.ErrorCode;
        }

        try
        {
            BranchMergePoliciesYaml branchMergePoliciesYaml = new()
            {
                Repository = repository,
                Branch = branch,
                MergePolicies = [],
                MergePrs = mergePrs,
                IgnoredChecks = ignoredChecks
            };

            bool configurationExists = await TryGetRepositoryBranchAsync(repository, branch) != null;
            if (configurationExists)
            {
                await _configurationRepositoryManager.UpdateRepositoryMergePoliciesAsync(
                    _options.ToConfigurationRepositoryOperationParameters(),
                    branchMergePoliciesYaml);
            }
            else
            {
                await _configurationRepositoryManager.AddRepositoryMergePoliciesAsync(
                    _options.ToConfigurationRepositoryOperationParameters(),
                    branchMergePoliciesYaml);
            }

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (ConfigurationObjectNotFoundException ex)
        {
            _logger.LogError("No existing repository branch configuration found for {repo}@{branch} in file {filePath} of repo {configRepo} on branch {configBranch}",
                repository,
                branch,
                ex.FilePath,
                ex.RepositoryUri,
                ex.BranchName);
            return Constants.ErrorCode;
        }
        catch (DuplicateConfigurationObjectException ex)
        {
            _logger.LogError("Repository branch merge settings for {repo}@{branch} already exist in '{filePath}' in repo {configRepo} on branch {configBranch}.",
                repository,
                branch,
                ex.FilePath,
                ex.Repository,
                ex.Branch);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to set repository merge settings.");
            return Constants.ErrorCode;
        }
    }

    private async Task<RepositoryBranch> TryGetRepositoryBranchAsync(string repository, string branch)
    {
        try
        {
            return await _barClient.GetRepositoryBranch(repository, branch);
        }
        catch (RestApiException ex) when (ex.Response.Status == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}

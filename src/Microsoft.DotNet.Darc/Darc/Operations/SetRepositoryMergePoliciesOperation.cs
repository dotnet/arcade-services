// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.Common;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Models.PopUps;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.Darc.Operations;

internal class SetRepositoryMergePoliciesOperation : Operation
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
    {
        _options = options;
        _barClient = barClient;
        _remoteFactory = remoteFactory;
        _configurationRepositoryManager = configurationRepositoryManager;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        if (_options.IgnoreChecks.Any() && !_options.AllChecksSuccessfulMergePolicy)
        {
            Console.WriteLine($"--ignore-checks must be combined with --all-checks-passed");
            return Constants.ErrorCode;
        }

        var repoType = GitRepoUrlUtils.ParseTypeFromUri(_options.Repository);
        if (repoType == GitRepoType.Local || repoType == GitRepoType.None)
        {
            Console.WriteLine("Please specify full repository URL (GitHub or AzDO)");
            return Constants.ErrorCode;
        }

        // Parse the merge policies
        List<MergePolicy> mergePolicies = [];

        if (_options.AllChecksSuccessfulMergePolicy)
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.AllCheckSuccessfulMergePolicyName,
                    Properties = new() { [MergePolicyConstants.IgnoreChecksMergePolicyPropertyName] = JToken.FromObject(_options.IgnoreChecks) }
                });
        }

        if (_options.NoRequestedChangesMergePolicy)
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.NoRequestedChangesMergePolicyName,
                    Properties = []
                });
        }

        if (_options.DontAutomergeDowngradesMergePolicy)
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.DontAutomergeDowngradesPolicyName,
                    Properties = []
                });
        }

        if (_options.StandardAutoMergePolicies)
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.StandardMergePolicyName,
                    Properties = []
                });
        }

        if (_options.CodeFlowCheckMergePolicy)
        {
            mergePolicies.Add(
                new MergePolicy
                {
                    Name = MergePolicyConstants.CodeflowMergePolicyName,
                    Properties = []
                });
        }

        string repository = _options.Repository;
        string branch = _options.Branch;

        // If in quiet (non-interactive mode), ensure that all options were passed, then
        // just call the remote API
        if (_options.Quiet)
        {
            if (string.IsNullOrEmpty(repository) ||
                string.IsNullOrEmpty(branch))
            {
                _logger.LogError($"Missing input parameters for merge policies. Please see command help or remove --quiet/-q for interactive mode");
                return Constants.ErrorCode;
            }
        }
        else
        {
            // Look up existing merge policies if the repository and branch were specified, and the user didn't
            // specify policies on the command line. In this case, they typically want to update
            if (mergePolicies.Count == 0 && !string.IsNullOrEmpty(repository) && !string.IsNullOrEmpty(branch))
            {
                mergePolicies = (await _barClient.GetRepositoryMergePoliciesAsync(repository, branch)).ToList();
            }

            // Help the user along with a form.  We'll use the API to gather suggested values
            // from existing subscriptions based on the input parameters.
            var initEditorPopUp = new SetRepositoryMergePoliciesPopUp("set-policies/set-policies-todo",
                _logger,
                repository,
                branch,
                mergePolicies,
                Constants.AvailableMergePolicyYamlHelp);

            var uxManager = new UxManager(_options.GitLocation, _logger);
            int exitCode = uxManager.PopUp(initEditorPopUp);
            if (exitCode != Constants.SuccessCode)
            {
                return exitCode;
            }
            repository = initEditorPopUp.Repository;
            branch = initEditorPopUp.Branch;
            mergePolicies = initEditorPopUp.MergePolicies;
        }

        IRemote verifyRemote = await _remoteFactory.CreateRemoteAsync(repository);

        if (!await UxHelpers.VerifyAndConfirmBranchExistsAsync(verifyRemote, repository, branch, !_options.Quiet))
        {
            Console.WriteLine("Aborting merge policy creation.");
            return Constants.ErrorCode;
        }

        try
        {
            if (_options.ShouldUseConfigurationRepository)
            {
                BranchMergePoliciesYaml branchMergePoliciesYaml = new()
                {
                    Repository = repository,
                    Branch = branch,
                    MergePolicies = MergePolicyYaml.FromClientModels(mergePolicies)
                };

                var policies = await _barClient.GetRepositoryMergePoliciesAsync(repository, branch);
                if (policies != null && policies.Any())
                {
                    if (branchMergePoliciesYaml.MergePolicies.Any())
                    {
                        try
                        {
                            await _configurationRepositoryManager.UpdateRepositoryMergePoliciesAsync(
                                _options.ToConfigurationRepositoryOperationParameters(),
                                branchMergePoliciesYaml);
                        }
                        // TODO drop to the "global try-catch" when configuration repo is the only behavior
                        catch (ConfigurationObjectNotFoundException ex)
                        {
                            _logger.LogError("No existing repository branch configuration found for {repo}@{branch} in file {filePath} of repo {configRepo} on branch {configBranch}",
                                branchMergePoliciesYaml.Repository,
                                branchMergePoliciesYaml.Branch,
                                ex.FilePath,
                                ex.RepositoryUri,
                                ex.BranchName);
                            return Constants.ErrorCode;
                        } 
                    }
                    else
                    {
                        try
                        {
                            await _configurationRepositoryManager.DeleteRepositoryMergePoliciesAsync(
                                _options.ToConfigurationRepositoryOperationParameters(),
                                branchMergePoliciesYaml);
                        }
                        // TODO drop to the "global try-catch" when configuration repo is the only behavior
                        catch (ConfigurationObjectNotFoundException ex)
                        {
                            _logger.LogError("No existing repository branch configuration found for {repo}@{branch} in file {filePath} of repo {configRepo} on branch {configBranch}",
                                branchMergePoliciesYaml.Repository,
                                branchMergePoliciesYaml.Branch,
                                ex.FilePath,
                                ex.RepositoryUri,
                                ex.BranchName);
                            return Constants.ErrorCode;
                        }
                    }
                }
                else
                {
                    if (!branchMergePoliciesYaml.MergePolicies.Any())
                    {
                        _logger.LogWarning("No merge policies specified for {repo}@{branch}, nothing to add.", 
                            branchMergePoliciesYaml.Repository, 
                            branchMergePoliciesYaml.Branch);
                        return Constants.SuccessCode;
                    }
                    try
                    {
                        await _configurationRepositoryManager.AddRepositoryMergePoliciesAsync(
                                        _options.ToConfigurationRepositoryOperationParameters(),
                                        branchMergePoliciesYaml);
                    }
                    // TODO drop to the "global try-catch" when configuration repo is the only behavior
                    catch (DuplicateConfigurationObjectException ex)
                    {
                        _logger.LogError("Repository branch merge policies for {repo}@{branch} already exist in '{filePath}'.",
                            branchMergePoliciesYaml.Repository,
                            branchMergePoliciesYaml.Branch,
                            ex.FilePath);
                        return Constants.ErrorCode;
                    }
                }
            }
            else
            {
                await _barClient.SetRepositoryMergePoliciesAsync(repository, branch, mergePolicies);
                Console.WriteLine($"Successfully updated merge policies for {repository}@{branch}.");
            }
            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (RestApiException e) when (e.Response.Status == (int) System.Net.HttpStatusCode.BadRequest)
        {
            _logger.LogError($"Failed to set repository auto merge policies: {e.Response.Content}");
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Failed to set merge policies.");
            return Constants.ErrorCode;
        }
    }
}

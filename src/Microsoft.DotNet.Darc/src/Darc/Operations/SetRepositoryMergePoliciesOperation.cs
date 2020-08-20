// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Contracts;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Models.PopUps;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Common;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class SetRepositoryMergePoliciesOperation : Operation
    {
        SetRepositoryMergePoliciesCommandLineOptions _options;
        public SetRepositoryMergePoliciesOperation(SetRepositoryMergePoliciesCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        public override async Task<int> ExecuteAsync()
        {
            IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

            if (_options.IgnoreChecks.Count() > 0 && !_options.AllChecksSuccessfulMergePolicy)
            {
                Console.WriteLine($"--ignore-checks must be combined with --all-checks-passed");
                return Constants.ErrorCode;
            }

            // Parse the merge policies
            List<MergePolicy> mergePolicies = new List<MergePolicy>();
            if (_options.NoExtraCommitsMergePolicy)
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = MergePolicyConstants.NoExtraCommitsMergePolicyName
                    });
            }

            if (_options.AllChecksSuccessfulMergePolicy)
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = MergePolicyConstants.AllCheckSuccessfulMergePolicyName,
                        Properties = ImmutableDictionary.Create<string, JToken>()
                            .Add(MergePolicyConstants.IgnoreChecksMergePolicyPropertyName, JToken.FromObject(_options.IgnoreChecks))
                    });
            }

            if (_options.NoRequestedChangesMergePolicy)
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = MergePolicyConstants.NoRequestedChangesMergePolicyName,
                        Properties = ImmutableDictionary.Create<string, JToken>()
                    });
            }

            if (_options.StandardAutoMergePolicies)
            {
                mergePolicies.Add(
                    new MergePolicy
                    {
                        Name = MergePolicyConstants.StandardMergePolicyName,
                        Properties = ImmutableDictionary.Create<string, JToken>()
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
                    Logger.LogError($"Missing input parameters for merge policies. Please see command help or remove --quiet/-q for interactive mode");
                    return Constants.ErrorCode;
                }
            }
            else
            {
                // Look up existing merge policies if the repository and branch were specified, and the user didn't
                // specify policies on the command line. In this case, they typically want to update
                if (!mergePolicies.Any() && !string.IsNullOrEmpty(repository) && !string.IsNullOrEmpty(branch))
                {
                    mergePolicies = (await remote.GetRepositoryMergePoliciesAsync(repository, branch)).ToList();
                }

                // Help the user along with a form.  We'll use the API to gather suggested values
                // from existing subscriptions based on the input parameters.
                SetRepositoryMergePoliciesPopUp initEditorPopUp =
                    new SetRepositoryMergePoliciesPopUp("set-policies/set-policies-todo",
                                             Logger,
                                             repository,
                                             branch,
                                             mergePolicies,
                                             Constants.AvailableMergePolicyYamlHelp);

                UxManager uxManager = new UxManager(_options.GitLocation, Logger);
                int exitCode = uxManager.PopUp(initEditorPopUp);
                if (exitCode != Constants.SuccessCode)
                {
                    return exitCode;
                }
                repository = initEditorPopUp.Repository;
                branch = initEditorPopUp.Branch;
                mergePolicies = initEditorPopUp.MergePolicies;
            }

            IRemote verifyRemote = RemoteFactory.GetRemote(_options, repository, Logger);
            IEnumerable<RepositoryBranch> targetRepository = await verifyRemote.GetRepositoriesAsync(repository);

            if (targetRepository == null || !targetRepository.Any())
            {
                Console.WriteLine($"The target repository '{repository}' doesn't have a Maestro installation. Aborting merge policy creation.");
                return Constants.ErrorCode;
            }

            if (!(await UxHelpers.VerifyAndConfirmBranchExistsAsync(verifyRemote, repository, branch, !_options.Quiet)))
            {
                Console.WriteLine("Aborting merge policy creation.");
                return Constants.ErrorCode;
            }

            try
            {
                await remote.SetRepositoryMergePoliciesAsync(
                    repository, branch, mergePolicies);
                Console.WriteLine($"Successfully updated merge policies for {repository}@{branch}.");
                return Constants.SuccessCode;
            }
            catch (AuthenticationException e)
            {
                Console.WriteLine(e.Message);
                return Constants.ErrorCode;
            }
            catch (RestApiException e) when (e.Response.Status == (int) System.Net.HttpStatusCode.BadRequest)
            {
                Logger.LogError($"Failed to set repository auto merge policies: {e.Response.Content}");
                return Constants.ErrorCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Failed to set merge policies.");
                return Constants.ErrorCode;
            }
        }
    }
}

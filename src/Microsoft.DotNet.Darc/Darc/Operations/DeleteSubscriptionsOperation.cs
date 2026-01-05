// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class DeleteSubscriptionsOperation : Operation
{
    private readonly IBarApiClient _barClient;
    private readonly DeleteSubscriptionsCommandLineOptions _options;
    private readonly IConfigurationRepositoryManager _configRepositoryManager;
    private readonly ILogger<DeleteSubscriptionsOperation> _logger;
    public DeleteSubscriptionsOperation(
        DeleteSubscriptionsCommandLineOptions options,
        IBarApiClient barClient,
        IConfigurationRepositoryManager configRepositoryManager,
        ILogger<DeleteSubscriptionsOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _configRepositoryManager = configRepositoryManager;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            bool noConfirm = _options.NoConfirmation;
            List<Subscription> subscriptionsToDelete = [];

            if (!string.IsNullOrEmpty(_options.Id))
            {
                // Look up subscription so we can print it later.
                try
                {
                    Subscription subscription = await _barClient.GetSubscriptionAsync(_options.Id);
                    subscriptionsToDelete.Add(subscription);
                }
                catch (RestApiException e) when (e.Response.Status == (int) HttpStatusCode.NotFound)
                {
                    Console.WriteLine($"Subscription with id '{_options.Id}' was not found.");
                    return Constants.ErrorCode;
                }
            }
            else
            {
                if (!_options.HasAnyFilters())
                {
                    Console.WriteLine($"Please specify one or more filters to select which subscriptions should be deleted (see help).");
                    return Constants.ErrorCode;
                }

                IEnumerable<Subscription> subscriptions = await _options.FilterSubscriptions(_barClient);

                if (!subscriptions.Any())
                {
                    Console.WriteLine("No subscriptions found matching the specified criteria.");
                    return Constants.ErrorCode;
                }

                subscriptionsToDelete.AddRange(subscriptions);
            }

            if (_options.ShouldUseConfigurationRepository)
            {
                foreach (Subscription subscription in subscriptionsToDelete)
                {
                    try
                    {
                        await _configRepositoryManager.DeleteSubscriptionAsync(
                                        _options.ToConfigurationRepositoryOperationParameters(),
                                        SubscriptionYaml.FromClientModel(subscription));
                    }
                    // TODO drop to the "global try-catch" when configuration repo is the only behavior
                    catch (ConfigurationObjectNotFoundException ex)
                    {
                        _logger.LogError("No existing subscription with id {id} found in file {filePath} of repo {repo} on branch {branch}",
                            subscription.Id,
                            ex.FilePath,
                            ex.RepositoryUri,
                            ex.BranchName);
                        return Constants.ErrorCode;
                    }
                }
            }
            else
            {
                if (!noConfirm)
                {
                    // Print out the list of subscriptions about to be triggered.
                    Console.WriteLine($"Will delete the following {subscriptionsToDelete.Count} subscriptions...");
                    foreach (var subscription in subscriptionsToDelete)
                    {
                        Console.WriteLine($"  {UxHelpers.GetSubscriptionDescription(subscription)}");
                    }

                    if (!UxHelpers.PromptForYesNo("Continue?"))
                    {
                        Console.WriteLine($"No subscriptions deleted, exiting.");
                        return Constants.ErrorCode;
                    }
                }

                Console.Write($"Deleting {subscriptionsToDelete.Count} subscriptions...{(noConfirm ? Environment.NewLine : "")}");
                foreach (var subscription in subscriptionsToDelete)
                {
                    // If noConfirm was passed, print out the subscriptions as we go
                    if (noConfirm)
                    {
                        Console.WriteLine($"  {UxHelpers.GetSubscriptionDescription(subscription)}");
                    }
                    await _barClient.DeleteSubscriptionAsync(subscription.Id);
                } 
            }
            Console.WriteLine("done");

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unexpected error while deleting subscriptions.");
            return Constants.ErrorCode;
        }
    }
}

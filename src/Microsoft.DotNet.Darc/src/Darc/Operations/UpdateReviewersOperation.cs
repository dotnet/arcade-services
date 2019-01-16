// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class UpdateReviewersOperation : Operation
    {
        UpdateReviewersCommandLineOptions _options;
        public UpdateReviewersOperation(UpdateReviewersCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        public override async Task<int> ExecuteAsync()
        {
            var subscriptionId = _options.SubscriptionId.Trim();
            var reviewers = _options.Reviewers.Trim();

            if (string.IsNullOrWhiteSpace(subscriptionId))
            {
                Logger.LogError("Please specify a subscription id");
                return Constants.ErrorCode;
            }

            // stuff the reviewers into a List
            var reviewersList = _options.Reviewers.Split(',').ToList();

            DarcSettings darcSettings = LocalSettings.GetDarcSettings(_options, Logger);
            // No need to set up a git type or PAT here.
            Remote remote = new Remote(darcSettings, Logger);

            try
            {
                Subscription updatedSubscription = await remote.UpdateReviewersAsync(subscriptionId, reviewersList);
                Console.WriteLine($"Successfully updated subscription with id '{subscriptionId}'");
                return Constants.SuccessCode;
            }
            catch (ApiErrorException e) when (e.Response.StatusCode == HttpStatusCode.NotFound)
            {
                // Not found is fine to ignore.  If we get this, it will be an aggregate exception with an inner API exception
                // that has a response message code of NotFound.  Return success.
                Console.WriteLine($"Subscription with id '{subscriptionId}' does not exist.");
                return Constants.SuccessCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Failed to update subscription with id '{subscriptionId}'");
                return Constants.ErrorCode;
            }
        }
    }
}

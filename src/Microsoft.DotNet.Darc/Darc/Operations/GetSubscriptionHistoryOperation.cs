// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

/// <summary>
/// Retrieves the history of subscription trigger outcomes based on input filters.
/// </summary>
internal class GetSubscriptionHistoryOperation : Operation
{
    private readonly GetSubscriptionHistoryCommandLineOptions _options;
    private readonly IBarApiClient _barClient;
    private readonly ILogger<GetSubscriptionHistoryOperation> _logger;

    public GetSubscriptionHistoryOperation(
        GetSubscriptionHistoryCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<GetSubscriptionHistoryOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        Guid? subscriptionId = null;
        if (!string.IsNullOrWhiteSpace(_options.SubscriptionId))
        {
            if (!Guid.TryParse(_options.SubscriptionId.Trim(), out var parsedId))
            {
                Console.WriteLine("The value provided for --id must be a valid subscription GUID.");
                return Constants.ErrorCode;
            }

            subscriptionId = parsedId;
        }

        string? outcomeType = null;
        if (!string.IsNullOrWhiteSpace(_options.OutcomeType))
        {
            if (!Enum.TryParse<OutcomeType>(_options.OutcomeType.Trim(), ignoreCase: true, out var parsedType))
            {
                Console.WriteLine($"The value provided for --type must be one of: {string.Join(", ", Enum.GetNames<OutcomeType>())}.");
                return Constants.ErrorCode;
            }

            outcomeType = parsedType.ToString();
        }

        if (!TryParseDate(_options.After, "--after", out DateTimeOffset? after))
        {
            return Constants.ErrorCode;
        }

        if (!TryParseDate(_options.Before, "--before", out DateTimeOffset? before))
        {
            return Constants.ErrorCode;
        }

        if (_options.Limit < 1 || _options.Limit > 1000)
        {
            Console.WriteLine("The value provided for --limit must be between 1 and 1000.");
            return Constants.ErrorCode;
        }

        try
        {
            IReadOnlyList<SubscriptionTriggerOutcome> outcomes = await _barClient.GetSubscriptionTriggerOutcomesAsync(
                subscriptionId: subscriptionId,
                buildId: _options.BuildId,
                after: after,
                before: before,
                subscriptionOutcomeType: outcomeType,
                search: _options.Search,
                limit: _options.Limit);

            if (outcomes.Count == 0)
            {
                Console.WriteLine("No subscription trigger outcomes found matching the specified criteria.");
                return Constants.ErrorCode;
            }

            switch (_options.OutputFormat)
            {
                case DarcOutputType.json:
                    Console.WriteLine(JsonConvert.SerializeObject(outcomes, Formatting.Indented, new StringEnumConverter()));
                    break;
                case DarcOutputType.text:
                    foreach (SubscriptionTriggerOutcome outcome in outcomes)
                    {
                        OutputTextOutcome(outcome);
                    }
                    break;
                default:
                    throw new NotImplementedException($"Output format type {_options.OutputFormat} not yet supported for get-subscription-history.");
            }

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error: Failed to retrieve subscription trigger history.");
            return Constants.ErrorCode;
        }
    }

    private bool TryParseDate(string? value, string optionName, out DateTimeOffset? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            _logger.LogError("The value '{value}' provided for {optionName} is not a valid date (e.g. \"2025-01-15T12:00:00Z\").", value, optionName);
            return false;
        }

        parsed = date;
        return true;
    }

    private static void OutputTextOutcome(SubscriptionTriggerOutcome outcome)
    {
        Console.WriteLine($"{outcome.Date.UtcDateTime:u}  [{outcome.Type}]  build {outcome.BuildId}");
        Console.WriteLine($"  Subscription: {outcome.SourceRepository} ==> '{outcome.TargetRepository}' ('{outcome.TargetBranch}')");
        Console.WriteLine($"  Subscription Id: {outcome.SubscriptionId}");
        if (!string.IsNullOrEmpty(outcome.PrUrl))
        {
            Console.WriteLine($"  PR: {outcome.PrUrl}");
        }
        if (!string.IsNullOrWhiteSpace(outcome.Message))
        {
            Console.WriteLine($"  {outcome.Message}");
        }
        Console.WriteLine();
    }
}

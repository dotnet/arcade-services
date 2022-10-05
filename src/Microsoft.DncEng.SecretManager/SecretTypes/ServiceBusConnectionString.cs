using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Azure.Management.ServiceBus;
using Microsoft.Azure.Management.ServiceBus.Models;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.CommandLineLib.Authentication;
using Microsoft.Rest;

namespace Microsoft.DncEng.SecretManager.SecretTypes;

[Name("service-bus-connection-string")]
public class ServiceBusConnectionString : SecretType<ServiceBusConnectionString.Parameters>
{
    public class Parameters
    {
        public Guid Subscription { get; set; }
        public string ResourceGroup { get; set; }
        public string Namespace { get; set; }
        public string Permissions { get; set; }
    }

    private readonly TokenCredentialProvider _tokenCredentialProvider;
    private readonly ISystemClock _clock;

    public ServiceBusConnectionString(TokenCredentialProvider tokenCredentialProvider, ISystemClock clock)
    {
        _tokenCredentialProvider = tokenCredentialProvider;
        _clock = clock;
    }

    private async Task<ServiceBusManagementClient> CreateManagementClient(Parameters parameters, CancellationToken cancellationToken)
    {
        var creds = await _tokenCredentialProvider.GetCredentialAsync();
        var token = await creds.GetTokenAsync(new TokenRequestContext(new[]
        {
            "https://management.azure.com/.default",
        }), cancellationToken);
        var serviceClientCredentials = new TokenCredentials(token.Token);
        return new ServiceBusManagementClient(serviceClientCredentials)
        {
            SubscriptionId = parameters.Subscription.ToString(),
        };
    }

    protected override async Task<SecretData> RotateValue(Parameters parameters, RotationContext context, CancellationToken cancellationToken)
    {
        var client = await CreateManagementClient(parameters, cancellationToken);
        var accessPolicyName = context.SecretName + "-access-policy";
        var rule = new SBAuthorizationRule(new List<AccessRights?>(), name: accessPolicyName);
        bool updateRule = false;
        foreach (var c in parameters.Permissions)
        {
            switch (c)
            {
                case 's':
                    rule.Rights.Add(AccessRights.Send);
                    break;
                case 'l':
                    rule.Rights.Add(AccessRights.Listen);
                    break;
                case 'm':
                    rule.Rights.Add(AccessRights.Manage);
                    break;
                default:
                    throw new ArgumentException($"Invalid permission specification '{c}'");
            }
        }
        try
        {
            var existingRule = await client.Namespaces.GetAuthorizationRuleAsync(parameters.ResourceGroup, parameters.Namespace, accessPolicyName, cancellationToken);
            if (existingRule.Rights.Count != rule.Rights.Count ||
                existingRule.Rights.Zip(rule.Rights).Any((p) => p.First != p.Second))
            {
                updateRule = true;
            }
        }
        catch (ErrorResponseException e) when (e.Response.StatusCode == HttpStatusCode.NotFound)
        {
            updateRule = true;
        }

        if (updateRule)
        {
            await client.Namespaces.CreateOrUpdateAuthorizationRuleAsync(parameters.ResourceGroup, parameters.Namespace,
                accessPolicyName, rule, cancellationToken);
        }

        var currentKey = context.GetValue("currentKey", "primary");
        AccessKeys keys;
        string result;
        switch (currentKey)
        {
            case "primary":
                keys = await client.Namespaces.RegenerateKeysAsync(parameters.ResourceGroup, parameters.Namespace, accessPolicyName,
                    new RegenerateAccessKeyParameters(KeyType.SecondaryKey), cancellationToken);
                result = keys.SecondaryConnectionString;
                context.SetValue("currentKey", "secondary");
                break;
            case "secondary":
                keys = await client.Namespaces.RegenerateKeysAsync(parameters.ResourceGroup, parameters.Namespace, accessPolicyName,
                    new RegenerateAccessKeyParameters(KeyType.PrimaryKey), cancellationToken);
                result = keys.PrimaryConnectionString;
                context.SetValue("currentKey", "primary");
                break;
            default:
                throw new InvalidOperationException($"Unexpected 'currentKey' value '{currentKey}'.");
        }


        return new SecretData(result, DateTimeOffset.MaxValue, _clock.UtcNow.AddMonths(6));
    }
}

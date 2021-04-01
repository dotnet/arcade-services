using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Azure.Management.ServiceBus;
using Microsoft.Azure.Management.ServiceBus.Models;
using Microsoft.DncEng.CommandLineLib.Authentication;
using Microsoft.Rest;

namespace Microsoft.DncEng.SecretManager.SecretTypes
{
    [Name("service-bus-connection-string")]
    public class ServiceBusConnectionString : SecretType
    {
        private readonly TokenCredentialProvider _tokenCredentialProvider;
        private readonly string _subscription;
        private readonly string _resourceGroup;
        private readonly string _namespace;
        private readonly string _permissions;

        public ServiceBusConnectionString(IReadOnlyDictionary<string, string> parameters, TokenCredentialProvider tokenCredentialProvider) : base(parameters)
        {
            _tokenCredentialProvider = tokenCredentialProvider;
            ReadRequiredParameter("subscription", ref _subscription);
            ReadRequiredParameter("resourceGroup", ref _resourceGroup);
            ReadRequiredParameter("namespace", ref _namespace);
            ReadRequiredParameter("permissions", ref _permissions);
        }

        private async Task<ServiceBusManagementClient> CreateManagementClient(CancellationToken cancellationToken)
        {
            var creds = await _tokenCredentialProvider.GetCredentialAsync();
            var token = await creds.GetTokenAsync(new TokenRequestContext(new[]
            {
                "https://management.azure.com/.default",
            }), cancellationToken);
            var serviceClientCredentials = new TokenCredentials(token.Token);
            return new ServiceBusManagementClient(serviceClientCredentials)
            {
                SubscriptionId = _subscription,
            };
        }

        protected override async Task<SecretData> RotateValue(RotationContext context, CancellationToken cancellationToken)
        {
            var client = await CreateManagementClient(cancellationToken);
            var accessPolicyName = context.SecretName + "-access-policy";
            var rule = new SBAuthorizationRule(new List<AccessRights?>(), name: accessPolicyName);
            bool updateRule = false;
            foreach (var c in _permissions)
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
                var existingRule = await client.Namespaces.GetAuthorizationRuleAsync(_resourceGroup, _namespace, accessPolicyName, cancellationToken);
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
                await client.Namespaces.CreateOrUpdateAuthorizationRuleAsync(_resourceGroup, _namespace,
                    accessPolicyName, rule, cancellationToken);
            }

            var currentKey = context.GetValue("currentKey", "primary");
            AccessKeys keys;
            string result;
            switch (currentKey)
            {
                case "primary":
                    keys = await client.Namespaces.RegenerateKeysAsync(_resourceGroup, _namespace, accessPolicyName,
                        new RegenerateAccessKeyParameters(KeyType.SecondaryKey), cancellationToken);
                    result = keys.SecondaryConnectionString;
                    context.SetValue("currentKey", "secondary");
                    break;
                case "secondary":
                    keys = await client.Namespaces.RegenerateKeysAsync(_resourceGroup, _namespace, accessPolicyName,
                        new RegenerateAccessKeyParameters(KeyType.PrimaryKey), cancellationToken);
                    result = keys.PrimaryConnectionString;
                    context.SetValue("currentKey", "primary");
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected 'currentKey' value '{currentKey}'.");
            }


            return new SecretData(result, DateTimeOffset.MaxValue, DateTimeOffset.UtcNow.AddMonths(6));
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Azure.Core;
using Maestro.Common.AppCredentials;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Operations;

internal class LoginOperation : Operation
{
    private readonly LoginCommandLineOptions _options;
    private readonly ILogger<LoginOperation> _logger;

    public LoginOperation(
        LoginCommandLineOptions options,
        ILogger<LoginOperation> logger)
    {
        _options = options;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            // Determine which Maestro URI to use
            string barUri = _options.BarUri ?? ProductConstructionServiceApiOptions.ProductionMaestroUri;
            _logger.LogInformation("Authenticating with Maestro at {barUri} (a browser window might open)", barUri);

            // Get the appropriate app ID for the Maestro URI
            string appId = ProductConstructionServiceApiOptions.GetAppIdForUri(barUri);
            
            // Create a user credential which will trigger interactive browser login
            // The authentication record will be stored automatically in ~/.darc/.auth-record-{appId}
            var credential = AppCredential.CreateUserCredential(appId, "Maestro.User");
            
            // Force authentication by requesting a token
            var tokenContext = new TokenRequestContext([$"api://{appId}/Maestro.User"]);
            var token = await credential.GetTokenAsync(tokenContext, default);
            
            if (token.Token != null)
            {
                _logger.LogInformation("Successfully authenticated with Maestro!");
                return Constants.SuccessCode;
            }
            else
            {
                _logger.LogError("Failed to obtain authentication token");
                _logger.LogInformation("If authentication continues to fail, you can use 'darc authenticate' to manually configure tokens.");
                return Constants.ErrorCode;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed: {message}", ex.Message);
            _logger.LogInformation("If authentication continues to fail, you can use 'darc authenticate' to manually configure tokens.");
            return Constants.ErrorCode;
        }
    }
}

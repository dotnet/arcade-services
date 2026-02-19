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

    /// <summary>
    /// Implements the 'login' verb
    /// </summary>
    public override async Task<int> ExecuteAsync()
    {
        try
        {
            // Determine which Maestro URI to use
            string barUri = _options.BarUri ?? ProductConstructionServiceApiOptions.ProductionMaestroUri;
            _logger.LogInformation($"Authenticating with Maestro at {barUri}");

            // Get the appropriate app ID for the Maestro URI
            string appId = GetAppIdForUri(barUri);
            
            _logger.LogInformation("Opening browser for authentication...");
            
            // Create a user credential which will trigger interactive browser login
            // The authentication record will be stored automatically in ~/.darc/.auth-record-{appId}
            var credential = AppCredential.CreateUserCredential(appId, "Maestro.User");
            
            // Force authentication by requesting a token
            var tokenContext = new TokenRequestContext([$"api://{appId}/Maestro.User"]);
            var token = await credential.GetTokenAsync(tokenContext, default);
            
            if (token.Token != null)
            {
                _logger.LogInformation("Successfully authenticated with Maestro!");
                _logger.LogInformation($"Authentication credentials have been stored in {AppCredential.AUTH_CACHE}");
                _logger.LogInformation("These credentials will be used by automation tools and the darc CLI.");
                return Constants.SuccessCode;
            }
            else
            {
                _logger.LogError("Failed to obtain authentication token.");
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

    /// <summary>
    /// Gets the Entra app ID for a given Maestro URI
    /// </summary>
    private string GetAppIdForUri(string uri)
    {
        string normalizedUri = uri.TrimEnd('/');
        
        // Production Maestro
        if (normalizedUri == ProductConstructionServiceApiOptions.ProductionMaestroUri.TrimEnd('/') ||
            normalizedUri == ProductConstructionServiceApiOptions.OldProductionMaestroUri.TrimEnd('/'))
        {
            return "54c17f3d-7325-4eca-9db7-f090bfc765a8"; // MaestroProductionAppId
        }
        
        // Staging Maestro or localhost
        if (normalizedUri == ProductConstructionServiceApiOptions.StagingMaestroUri.TrimEnd('/') ||
            normalizedUri == ProductConstructionServiceApiOptions.OldStagingMaestroUri.TrimEnd('/') ||
            normalizedUri == ProductConstructionServiceApiOptions.PcsLocalUri.TrimEnd('/'))
        {
            return "baf98f1b-374e-487d-af42-aa33807f11e4"; // MaestroStagingAppId
        }
        
        throw new ArgumentException($"Unknown Maestro URI: {uri}. Please use one of the known Maestro endpoints or configure authentication manually using 'darc authenticate'.");
    }
}

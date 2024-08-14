// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;
using Azure.Identity;

namespace Maestro.Common.AppCredentials;

/// <summary>
/// A credential for authenticating against Azure applications.
/// </summary>
public class AppCredential : TokenCredential
{
    public static string AUTH_CACHE = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".darc");

    private static readonly string AUTH_RECORD_PREFIX = ".auth-record";

    private const string TENANT_ID = "72f988bf-86f1-41af-91ab-2d7cd011db47";

    private readonly TokenRequestContext _requestContext;
    private readonly TokenCredential _tokenCredential;

    public AppCredential(TokenCredential credential, TokenRequestContext requestContext)
    {
        _requestContext = requestContext;
        _tokenCredential = credential;
    }

    public override AccessToken GetToken(TokenRequestContext _, CancellationToken cancellationToken)
    {
        // We hardcode the request context as we know which scopes we need to invoke in each scenario (user vs daemon)
        return _tokenCredential.GetToken(_requestContext, cancellationToken);
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext _, CancellationToken cancellationToken)
    {
        // We hardcode the request context as we know which scopes we need to invoke in each scenario (user vs daemon)
        return _tokenCredential.GetTokenAsync(_requestContext, cancellationToken);
    }

    /// <summary>
    /// Use this for user-based flows.
    /// </summary>
    public static AppCredential CreateUserCredential(string appId, string userScope = ".default")
        => CreateUserCredential(appId, new TokenRequestContext([$"api://{appId}/{userScope}"]));

    /// <summary>
    /// Use this for user-based flows.
    /// </summary>
    public static AppCredential CreateUserCredential(string appId, TokenRequestContext requestContext)
    {
        var authRecordPath = Path.Combine(AUTH_CACHE, $"{AUTH_RECORD_PREFIX}-{appId}");
        var credential = GetInteractiveCredential(appId, requestContext, authRecordPath);

        return new AppCredential(credential, requestContext);
    }

    /// <summary>
    /// Creates an interactive credential. Checks local cache first for an authentication record.
    /// Authentication record is a set of app and user-specific metadata used by the library to authenticate
    /// </summary>
    private static InteractiveBrowserCredential GetInteractiveCredential(
        string appId,
        TokenRequestContext requestContext,
        string authRecordPath)
    {
        // This is a usual configuration for a credential obtained against an entra app through a browser sign-in
        var credentialOptions = new InteractiveBrowserCredentialOptions
        {
            TenantId = TENANT_ID,
            ClientId = appId,
            RedirectUri = new Uri("http://localhost"),
            // These options describe credential caching only during runtime
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions()
            {
                Name = "maestro"
            },
        };


        var authRecordDir = Path.GetDirectoryName(authRecordPath) ??
            throw new ArgumentException($"Cannot resolve cache dir from auth record: {authRecordPath}");

        if (!Directory.Exists(authRecordDir))
        {
            Directory.CreateDirectory(authRecordDir);
        }

        if (File.Exists(authRecordPath))
        {
            try
            {
                // Fetch existing authentication record to not prompt the user for consent
                using var authRecordReadStream = new FileStream(authRecordPath, FileMode.Open, FileAccess.Read);
                credentialOptions.AuthenticationRecord = AuthenticationRecord.Deserialize(authRecordReadStream);
            }
            catch
            {
                // We failed to read the authentication record, we should delete the invalid file and re-create it
                File.Delete(authRecordPath);

                return GetInteractiveCredential(appId, requestContext, authRecordPath);
            }

            return new InteractiveBrowserCredential(credentialOptions);
        }

        var credential = new InteractiveBrowserCredential(credentialOptions);

        // Prompt the user for consent and save the resulting authentication record on disk
        var authRecord = credential.Authenticate(requestContext);

        using var authRecordStream = new FileStream(authRecordPath, FileMode.Create, FileAccess.Write);
        authRecord.Serialize(authRecordStream);

        return credential;
    }

    /// <summary>
    /// Use this for invocations from services using an MI.
    /// ID can be "system" for system-assigned identity or GUID for a user assigned one.
    /// </summary>
    public static AppCredential CreateManagedIdentityCredential(string appId, string managedIdentityId)
    {
        var miCredential = managedIdentityId == "system"
            ? new ManagedIdentityCredential()
            : new ManagedIdentityCredential(managedIdentityId);

        var appCredential = new ClientAssertionCredential(
            TENANT_ID,
            appId,
            async (ct) => (await miCredential.GetTokenAsync(new TokenRequestContext(["api://AzureADTokenExchange"]), ct)).Token);

        var requestContext = new TokenRequestContext([$"api://{appId}/.default"]);
        return new AppCredential(appCredential, requestContext);
    }

    /// <summary>
    /// Use this for invocations from pipelines without a token.
    /// </summary>
    public static AppCredential CreateNonUserCredential(string appId)
    {
        var requestContext = new TokenRequestContext([$"{appId}/.default"]);
        var credential = new AzureCliCredential();
        return new AppCredential(credential, requestContext);
    }
}

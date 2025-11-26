// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;
using Azure.Identity;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Maestro.Common.AppCredentials;
public class CachedInteractiveBrowserCredential: TokenCredential
{
    private InteractiveBrowserCredential _credential;
    private readonly InteractiveBrowserCredentialOptions _options;
    private readonly string _authRecordPath;

    private bool _isCached = false;

    public CachedInteractiveBrowserCredential(
        InteractiveBrowserCredentialOptions options,
        string authRecordPath)
    {
        _options = options;
        _authRecordPath = authRecordPath;

        if (File.Exists(_authRecordPath))
        {
            try
            {
                // Fetch existing authentication record to not prompt the user for consent
                options.AuthenticationRecord = GetAuthenticationRecord();
                _isCached = true;
            }
            catch
            {
                // We failed to read the authentication record, we should delete the invalid file
                File.Delete(_authRecordPath);
            }
        }

        _credential = new InteractiveBrowserCredential(options);
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        CacheAuthenticationRecord(requestContext);

        return _credential.GetToken(requestContext, cancellationToken);
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        CacheAuthenticationRecord(requestContext);

        return _credential.GetTokenAsync(requestContext, cancellationToken);
    }

    private AuthenticationRecord GetAuthenticationRecord()
    {
        using var authRecordReadStream = new FileStream(_authRecordPath, FileMode.Open, FileAccess.Read);
        return AuthenticationRecord.Deserialize(authRecordReadStream);
    }

    private void CacheAuthenticationRecord(TokenRequestContext requestContext)
    {
        if (_isCached)
        {
            return;
        }

        var authRecordDir = Path.GetDirectoryName(_authRecordPath) ??
            throw new ArgumentException($"Cannot resolve cache dir from auth record: {_authRecordPath}");

        if (!Directory.Exists(authRecordDir))
        {
            Directory.CreateDirectory(authRecordDir);
        }

        static bool IsMsalCachePersistenceException(Exception e) =>
            e is MsalCachePersistenceException || (e.InnerException is not null && IsMsalCachePersistenceException(e.InnerException));

        AuthenticationRecord authRecord;
        try
        {
            // Prompt the user for consent and save the resulting authentication record on disk
            authRecord = _credential.Authenticate(requestContext);
        }
        catch (Exception e) when (IsMsalCachePersistenceException(e))
        {
            // If we cannot persist the token cache, fall back to interactive authentication without persistence
            _options.TokenCachePersistenceOptions = null;
            _credential = new InteractiveBrowserCredential(_options);
            _credential.Authenticate(requestContext);
            return;
        }

        using var authRecordStream = new FileStream(_authRecordPath, FileMode.Create, FileAccess.Write);
        authRecord.Serialize(authRecordStream);

        _isCached = true;
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;
using Azure.Identity;
using Microsoft.Identity.Client.Extensions.Msal;

namespace Maestro.Common.AppCredentials;
public class CachedInteractiveBrowserCredential: TokenCredential
{
    private InteractiveBrowserCredential _browserCredential;
    private DeviceCodeCredential _deviceCodeCredential;

    private readonly InteractiveBrowserCredentialOptions _options;
    private readonly string _authRecordPath;

    private int _isCached = 0;
    private int _isDeviceCodeFallback = 0;

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
                Interlocked.Exchange(ref _isCached, 1);
            }
            catch
            {
                // We failed to read the authentication record, we should delete the invalid file
                File.Delete(_authRecordPath);
            }
        }

        _browserCredential = new InteractiveBrowserCredential(options);
        _deviceCodeCredential = new DeviceCodeCredential(new()
        {
            TenantId = _options.TenantId,
            ClientId = _options.ClientId,
            TokenCachePersistenceOptions = _options.TokenCachePersistenceOptions,
        });
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        CacheAuthenticationRecord(requestContext, cancellationToken);

        if (Interlocked.CompareExchange(ref _isDeviceCodeFallback, 0, 0) == 1)
        {
            return _deviceCodeCredential.GetToken(requestContext, cancellationToken);
        }

        try
        {
            return _browserCredential.GetToken(requestContext, cancellationToken);
        }
        catch (AuthenticationFailedException)
        {
            Interlocked.Exchange(ref _isDeviceCodeFallback, 1);
            return _deviceCodeCredential.GetToken(requestContext, cancellationToken);
        }
    }

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        CacheAuthenticationRecord(requestContext, cancellationToken);

        if (Interlocked.CompareExchange(ref _isDeviceCodeFallback, 0, 0) == 1)
        {
            return await _deviceCodeCredential.GetTokenAsync(requestContext, cancellationToken);
        }

        try
        {
            return await _browserCredential.GetTokenAsync(requestContext, cancellationToken);
        }
        catch (AuthenticationFailedException)
        {
            Interlocked.Exchange(ref _isDeviceCodeFallback, 1);
            return await _deviceCodeCredential.GetTokenAsync(requestContext, cancellationToken);
        }
    }

    private AuthenticationRecord GetAuthenticationRecord()
    {
        using var authRecordReadStream = new FileStream(_authRecordPath, FileMode.Open, FileAccess.Read);
        return AuthenticationRecord.Deserialize(authRecordReadStream);
    }

    private void CacheAuthenticationRecord(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _isCached, 0, 0) == 1)
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
            authRecord = Authenticate(requestContext, cancellationToken);
        }
        catch (Exception e) when (IsMsalCachePersistenceException(e))
        {
            // If we cannot persist the token cache, fall back to interactive authentication without persistence
            _browserCredential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions()
            {
                TenantId = _options.TenantId,
                ClientId = _options.ClientId,
            });
            _deviceCodeCredential = new DeviceCodeCredential(new()
            {
                TenantId = _options.TenantId,
                ClientId = _options.ClientId,
            });
            authRecord = Authenticate(requestContext, cancellationToken);
        }

        using var authRecordStream = new FileStream(_authRecordPath, FileMode.Create, FileAccess.Write);
        authRecord.Serialize(authRecordStream, cancellationToken);

        Interlocked.Exchange(ref _isCached, 1);
    }

    private AuthenticationRecord Authenticate(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        try
        {
            return _browserCredential.Authenticate(requestContext, cancellationToken)
                ?? _deviceCodeCredential.Authenticate(requestContext, cancellationToken);
        }
        catch (AuthenticationFailedException)
        {
            Interlocked.Exchange(ref _isDeviceCodeFallback, 1);
            return _deviceCodeCredential.Authenticate(requestContext, cancellationToken);
        }
    }
}

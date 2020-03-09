using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Identity.Client;

namespace Microsoft.DncEng.Configuration.Extensions
{
    public class LocalDevTokenCredential : TokenCredential
    {
        private readonly IPublicClientApplication _app;

        public LocalDevTokenCredential()
        {
            // this is the azure cli's guid
            _app = PublicClientApplicationBuilder.Create("04b07795-8ddb-461a-bbee-02f9e1bf7b46")
                .WithDefaultRedirectUri()
                .Build();

            _app.UserTokenCache.SetBeforeAccessAsync(OnBeforeAccessAsync);
            _app.UserTokenCache.SetAfterAccessAsync(OnAfterAccessAsync);

            _account = Task.Run(async () => await _app.GetAccountsAsync()).Result.FirstOrDefault();
        }

        private static string CacheDirectory { get; } =
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".auth-cache");

        private static string CacheLockPath => Path.Join(CacheDirectory, ".cache.lock");

        private static string CacheFilePath => Path.Join(CacheDirectory, "token.cache");

        private static string DeviceCodeLockPath => Path.Join(CacheDirectory, ".auth.lock");

        private static DateTimeOffset _lastReadTime;

        private static async Task OnBeforeAccessAsync(TokenCacheNotificationArgs arg)
        {
            var ts = File.GetLastWriteTimeUtc(CacheFilePath);
            if (File.Exists(CacheFilePath) && _lastReadTime < ts)
            {
                using (await SentinelFileLock.AcquireAsync(CacheLockPath, 100, TimeSpan.FromMilliseconds(600)))
                {
                    var protectedBytes = await File.ReadAllBytesAsync(CacheFilePath);
                    var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                    _lastReadTime = ts;
                    arg.TokenCache.DeserializeMsalV3(bytes, false);
                }
            }
        }

        private static async Task OnAfterAccessAsync(TokenCacheNotificationArgs arg)
        {
            using (await SentinelFileLock.AcquireAsync(CacheLockPath, 100, TimeSpan.FromMilliseconds(600)))
            {
                _lastReadTime = default;
                var bytes = arg.TokenCache.SerializeMsalV3();
                var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                await File.WriteAllBytesAsync(CacheFilePath, protectedBytes);
            }
        }

        private IAccount _account;

        public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            bool uiRequired = false;
            if (_account != null)
            {
                try
                {
                    var result = await _app.AcquireTokenSilent(requestContext.Scopes, _account)
                        .ExecuteAsync(cancellationToken);
                    return new AccessToken(result.AccessToken, result.ExpiresOn);
                }
                catch (MsalUiRequiredException)
                {
                    // will be handled lower
                    uiRequired = true;
                }
            }

            // we give the lock a total wait time of 1 hour because the breakpoint to do device code authentication is inside the lock
            using (await SentinelFileLock.AcquireAsync(DeviceCodeLockPath, 3600, TimeSpan.FromMilliseconds(1000)))
            {
                _account = (await _app.GetAccountsAsync()).FirstOrDefault();
                if (_account == null || uiRequired)
                {
                    var deviceCodeResult = await _app.AcquireTokenWithDeviceCode(requestContext.Scopes, DeviceCodeResultCallback)
                        .ExecuteAsync(cancellationToken);
                    _account = deviceCodeResult.Account;
                    return new AccessToken(deviceCodeResult.AccessToken, deviceCodeResult.ExpiresOn);
                }
            }
            
            // a recursive call here is fine because there wasn't any better way to do this, and this is local dev only
            // This return statement will only get executed when `_account` was null at the top of the method,
            // and it becomes non-null at line 91
            // When that happens the function will either return the access token from AcquireTokenSilentAsync,
            // or will hit the `uiRequired` branch and call AcquireTokenWithDeviceCode and return
            return await GetTokenAsync(requestContext, cancellationToken);
        }

        [MethodImpl(MethodImplOptions.NoOptimization)]
        private static Task DeviceCodeResultCallback(DeviceCodeResult arg)
        {
            if (!Debugger.IsAttached)
                throw new InvalidOperationException("Debugger required for local service fabric authentication.");
            var userCode = arg.UserCode;
            var verificationUrl = arg.VerificationUrl;
            // If your debugger breaks here you need to authenticate to azure with a device code
            // navigate a browser to the url contained in the "verificationUrl" variable and enter the code in the "userCode" variable
            Debugger.Break();
            return Task.CompletedTask;
        }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return Task.Run(async () => await GetTokenAsync(requestContext, cancellationToken), cancellationToken).GetAwaiter().GetResult();
        }
    }
}

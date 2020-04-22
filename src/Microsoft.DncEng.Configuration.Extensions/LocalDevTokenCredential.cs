using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Identity.Client;

namespace Microsoft.DncEng.Configuration.Extensions
{
    public class LocalDevTokenCredential : TokenCredential
    {
        public static bool IsBoostrapping { get; set; } = false;

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
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DncEngConfiguration", ".auth-cache");

        private static string CacheLockPath => Path.Join(CacheDirectory, ".cache.lock");

        private static string CacheFilePath => Path.Join(CacheDirectory, "token.cache");

        private static string DeviceCodeLockPath => Path.Join(CacheDirectory, ".auth.lock");

        private static DateTimeOffset _lastReadTime;

        private static async Task OnBeforeAccessAsync(TokenCacheNotificationArgs arg)
        {
            var ts = File.GetLastWriteTimeUtc(CacheFilePath);
            if (File.Exists(CacheFilePath) && _lastReadTime < ts)
            {
                try
                {

                    using (await SentinelFileLock.AcquireAsync(CacheLockPath, 100, TimeSpan.FromMilliseconds(600)))
                    {
                        var protectedBytes = await File.ReadAllBytesAsync(CacheFilePath);
                        var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine);
                        _lastReadTime = ts;
                        arg.TokenCache.DeserializeMsalV3(bytes, false);
                    }
                }
                catch (CryptographicException)
                {
                    // Unable to deserialize, just treat it as if the file is gone
                }
            }
        }

        private static async Task OnAfterAccessAsync(TokenCacheNotificationArgs arg)
        {
            using (await SentinelFileLock.AcquireAsync(CacheLockPath, 100, TimeSpan.FromMilliseconds(600)))
            {
                _lastReadTime = default;
                var bytes = arg.TokenCache.SerializeMsalV3();
                var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine);
                var info = new FileInfo(CacheFilePath);
                if (!info.Exists)
                {
                    if (!IsBoostrapping)
                    {
                        throw new InvalidOperationException("Token Cache File Not Found, Please re-run boostrap.");
                    }

                    try
                    {
                        using (info.Create()) { }
                    }
                    catch { }
                    var sec = new FileSecurity();
                    sec.SetAccessRuleProtection(true, false);
                    sec.AddAccessRule(new FileSystemAccessRule(ConfigurationConstants.ConfigurationAccessGroupName, FileSystemRights.FullControl, AccessControlType.Allow));
                    sec.AddAccessRule(new FileSystemAccessRule("Administrators", FileSystemRights.FullControl, AccessControlType.Allow));
                    info.SetAccessControl(sec);

                    using (FileStream stream = info.OpenWrite())
                    {
                        stream.Write(protectedBytes);
                    }
                }
                else
                {
                    using (var stream = info.OpenWrite())
                    {
                        stream.Write(protectedBytes);
                    }
                }
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
            if (!IsBoostrapping)
            {
                throw new InvalidOperationException("Authentication is required, please re-run bootstrap.");
            }
            var userCode = arg.UserCode;
            var verificationUrl = arg.VerificationUrl;

            Console.WriteLine($"To sign in, use a web browser to open the page {verificationUrl} and enter the code {userCode} to authenticate.");
            Console.WriteLine("Press any key when finished...");
            Console.ReadKey(true);

            return Task.CompletedTask;
        }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return Task.Run(async () => await GetTokenAsync(requestContext, cancellationToken), cancellationToken).GetAwaiter().GetResult();
        }
    }
}

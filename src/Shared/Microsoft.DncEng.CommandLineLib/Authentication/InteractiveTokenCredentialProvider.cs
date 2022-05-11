using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;

namespace Microsoft.DncEng.CommandLineLib.Authentication
{
    [ExcludeFromCodeCoverage]
    public class InteractiveTokenCredentialProvider : ITelemetryInitializer
    {
        private readonly Lazy<Task<TokenCredential>> _getCred;
        private string _userId;

        public InteractiveTokenCredentialProvider()
        {
            _getCred = new Lazy<Task<TokenCredential>>(GetCredentialAsyncImpl);
        }

        public Task<TokenCredential> GetCredentialAsync()
        {
            return _getCred.Value;
        }

        private async Task<TokenCredential> GetCredentialAsyncImpl()
        {
            string authRecordPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "dotnet-eng",
                "auth-data.json"
            );


            // Fetch the cached auth record, so that we don't have to browser auth every time the user uses the tool
            AuthenticationRecord record = null;
            if (File.Exists(authRecordPath))
            {
                try
                {
                    await using FileStream stream = File.Open(authRecordPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    record = await AuthenticationRecord.DeserializeAsync(stream);
                }
                catch
                {
                    // Failed to cache, next attempt will just re-prompt
                }
            }

            var cred = new InteractiveBrowserCredential(
                new InteractiveBrowserCredentialOptions
                {
                    TokenCachePersistenceOptions = new TokenCachePersistenceOptions{ Name = GetType().Assembly.GetName().Name },
                    AuthenticationRecord = record,
                }
            );

            if (record == null)
            {
                // If we didn't already have a record, call authenticate async to trigger the browser login
                // so we can get the authentication record and store it
                record = await cred.AuthenticateAsync();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(authRecordPath));
                    await using FileStream stream = File.Create(authRecordPath);
                    await record.SerializeAsync(stream);
                }
                catch
                {
                    // Failed to cache, next attempt will just re-prompt
                }
            }

            _userId = record.Username;
            return cred;
        }

        public void Initialize(ITelemetry telemetry)
        {
            string userId = _userId;
            if (!string.IsNullOrEmpty(userId))
            {
                telemetry.Context.User.AuthenticatedUserId = userId;
            }
        }
    }
}

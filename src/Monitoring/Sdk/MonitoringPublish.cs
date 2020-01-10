// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using BuildTask = Microsoft.Build.Utilities.Task;

namespace Microsoft.DotNet.Monitoring.Sdk
{
    public class MonitoringPublish : BuildTask
    {
        [Required]
        public string Host { get; set; }

        [Required]
        public string AccessToken { get; set; }

        [Required]
        public string DashboardDirectory { get; set; }

        [Required]
        public string DataSourceDirectory{ get; set; }

        [Required]
        public string NotificationsDirectory { get; set; }
        
        [Required]
        public string KeyVaultName { get; set; }

        [Required]
        public string KeyVaultConnectionString { get; set; }

        [Required]
        public string Tag { get; set; }

        [Required]
        public string Environment { get; set; }

        public sealed override bool Execute()
        {
            return ExecuteAsync().GetAwaiter().GetResult();
        }

        private async Task<bool> ExecuteAsync()
        {
            using (var client = new GrafanaClient(Host, AccessToken))
            using (var deploy = new DeployPublisher(client, KeyVaultName, KeyVaultConnectionString, Tag, DashboardDirectory, DataSourceDirectory, NotificationsDirectory, Environment))
            {
                try
                {
                    await deploy.PostToGrafanaAsync();
                }
                catch (HttpRequestException e)
                {
                    Log.LogErrorFromException(e, showStackTrace: false, showDetail: false, file: "MonitoringPublish");
                    return false;
                }
            }

            return true;
        }
    }
}

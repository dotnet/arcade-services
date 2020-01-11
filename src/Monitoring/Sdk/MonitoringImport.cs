// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using BuildTask = Microsoft.Build.Utilities.Task;

namespace Microsoft.DotNet.Monitoring.Sdk
{
    public class MonitoringImport : BuildTask
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
        public string NotificationDirectory { get; set; }

        [Required]
        public string DashboardId { get; set; }

        [Required]
        public string Tag { get; set; }

        [Required]
        public ITaskItem[] Environments { get; set; }

        public sealed override bool Execute()
        {
            return ExecuteAsync().GetAwaiter().GetResult();
        }

        private async Task<bool> ExecuteAsync()
        {
            using (var client = new GrafanaClient(Host, AccessToken))
            {
                var deploy = new DeployImporter(
                    client,
                    Tag,
                    DashboardDirectory,
                    DataSourceDirectory,
                    NotificationDirectory,
                    Environments.Select(e => e.ItemSpec).ToArray(),
                    Log);

                try
                {
                    await deploy.ImportFromGrafana(DashboardId);
                }
                catch (HttpRequestException e)
                {
                    Log.LogErrorFromException(e,
                        showStackTrace: false,
                        showDetail: false,
                        file: "MonitoringImport");
                    return false;
                }
            }

            return true;
        }
    }
}

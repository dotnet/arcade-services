// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
        public ITaskItem[] Dashboard { get; set; }

        [Required]
        public ITaskItem[] DataSource { get; set; }

        public sealed override bool Execute()
        {
            return ExecuteAsync().GetAwaiter().GetResult();
        }

        private async Task<bool> ExecuteAsync()
        {
            Log.LogMessage(MessageImportance.Low, "Uploading grafana dashboards to '{0}'", Host);
            
            foreach (ITaskItem dataSource in DataSource)
            {
                string dataSourcePath = dataSource.ItemSpec;

                Log.LogMessage(MessageImportance.Low, "Uploading data source '{0}'", dataSourcePath);
            }
            foreach (ITaskItem dashboard in Dashboard)
            {
                string dashboardPath = dashboard.ItemSpec;

                Log.LogMessage(MessageImportance.Low, "Uploading dashboard '{0}'", dashboardPath);
            }

            return true;
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Task = Microsoft.Build.Utilities.Task;

namespace Microsoft.DotNet.Monitoring.Sdk
{
    public class MonitoringImport: Task
    {
        [Required]
        public string Host { get; set; }

        [Required]
        public string AccessToken { get; set; }

        [Required]
        public string DashboardId { get; set; }
        
        [Required]
        public string DashboardDirectory { get; set; }

        [Required]
        public string DataSourceDirectory{ get; set; }

        public sealed override bool Execute()
        {
            return ExecuteAsync().GetAwaiter().GetResult();
        }

        private async Task<bool> ExecuteAsync()
        {
            Log.LogMessage(MessageImportance.Normal, "Importing grafana dashboard '{0}' form '{1}' to '{2}'", DashboardId, Host, DashboardDirectory);
            Log.LogMessage(MessageImportance.Low, "Importing grafana data sources to '{0}'", DataSourceDirectory);
            return true;
        }
    }
}

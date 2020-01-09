// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DotNet.Grafana;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.Monitoring.Sdk
{
    public sealed class DeployImporter : DeployToolBase
    {
        public DeployImporter(
            GrafanaClient grafanaClient,
            string sourceTagValue,
            string dashboardDirectory,
            string datasourceDirectory) : base(
            grafanaClient, sourceTagValue, dashboardDirectory, datasourceDirectory)
        {
        }

        public Task ImportFromGrafana(params string[] dashboardUids)
        {
            return ImportFromGrafana((IEnumerable<string>) dashboardUids);
        }

        public async Task ImportFromGrafana(IEnumerable<string> dashboardUids)
        {
            foreach (string dashboardUid in dashboardUids)
            {
                // Get a dashboard
                JObject dashboard = await GrafanaClient.GetDashboardAsync(dashboardUid).ConfigureAwait(false);

                // Cache dashboard data needed for post
                JObject slimmedDashboard = GrafanaSerialization.SanitizeDashboard(dashboard);

                string targetUid = slimmedDashboard.Value<string>("uid");

                JArray tags = slimmedDashboard.Value<JArray>("tags");
                JToken uidTag = tags.FirstOrDefault(t => t.Value<string>().StartsWith(BaseUidTagPrefix));
                if (uidTag != null)
                {
                    targetUid = uidTag.Value<string>().Substring(BaseUidTagPrefix.Length);
                }

                // Get the dashboard folder data and cache if not already present
                int folderId = GrafanaSerialization.ExtractFolderId(dashboard);
                JObject folder = await GrafanaClient.GetFolderAsync(folderId).ConfigureAwait(false);
                FolderData folderData = GrafanaSerialization.SanitizeFolder(folder);

                // Folder uid is needed for the dashboard export object                    
                JObject dashboardObject = GrafanaSerialization.ConstructDashboardExportObject(slimmedDashboard, folderData.Uid);

                // Get datasources used in the dashboard
                IEnumerable<string> dataSourceNames = GrafanaSerialization.ExtractDataSourceNames(dashboard);

                foreach (string datasourceName in dataSourceNames)
                {
                    string datasourcePath = GetDatasourceFilePath(datasourceName);
                    if (File.Exists(datasourcePath))
                    {
                        // If we already have that datasource, don't overwrite it's useful values with empty ones
                        continue;
                    }

                    JObject datasource = await GrafanaClient.GetDataSource(datasourceName).ConfigureAwait(false);
                    datasource = GrafanaSerialization.SanitizeDataSource(datasource);
                    using (var datasourceStreamWriter = new StreamWriter(datasourcePath))
                    using (var datasourceJsonWriter = new JsonTextWriter(datasourceStreamWriter))
                    {
                        await datasource.WriteToAsync(datasourceJsonWriter);
                    }
                }


                string dashboardPath = GetDashboardFilePath(folderData.Title, targetUid);
                using (var dashboardStreamWriter = new StreamWriter(dashboardPath))
                using (var dashboardJsonWriter = new JsonTextWriter(dashboardStreamWriter))
                {
                    await dashboardObject.WriteToAsync(dashboardJsonWriter);
                }
            }
        }
    }
}

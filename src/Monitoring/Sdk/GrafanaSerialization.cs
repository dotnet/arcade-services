// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DotNet.Grafana
{
    /// <summary>
    /// Utility class to hold methods manipulating JSON in ways specific to the Grafana API
    /// </summary>
    public static class GrafanaSerialization
    {
        /// <summary>
        /// Extract the Folder ID of a Dashboard from a JSON object returned by the api/dashboards/uid endpoint
        /// </summary>
        /// <param name="dashboard"></param>
        /// <returns></returns>
        public static int ExtractFolderId(JObject dashboard)
        {
            return dashboard["meta"]["folderId"].Value<int>();
        }

        /// <summary>
        /// Modify a Dashboard JSON object as retrieved from the Grafana API into
        /// something suitable to post back to the API
        /// </summary>
        public static JObject SanitizeDashboard(JObject dashboard)
        {
            var slimmedDashboard = new JObject((JObject)dashboard["dashboard"]);
            slimmedDashboard.Remove("id");
            slimmedDashboard.Remove("version");
            var allTargets = slimmedDashboard.SelectTokens("panels.[*].targets.[*]");
            foreach (JToken jToken in allTargets)
            {
                var target = (JObject) jToken;
                target.Remove("subscription");
            }
            return slimmedDashboard;
        }

        /// <summary>
        /// Extract the names of data sources used by a given dashboard. 
        /// </summary>
        /// <param name="dashboard">A JSON definition of a dashboard as delivered by the Grafana API</param>
        /// <returns></returns>
        public static IEnumerable<string> ExtractDataSourceNames(JObject dashboard)
        {
            // Data sources live in panel[*].datasource, unless the "Mixed Data source" feature
            // is used. Then, get names from panel[*].target.datasource. 

            return dashboard
                .SelectTokens("$.dashboard.panels[*]..datasource")
                .Values<string>()
                .Where(x => !String.IsNullOrEmpty(x))
                .Where(x => x != "-- Mixed --")
                .Distinct();
        }
        
        /// <summary>
        /// Modify a Data Source JSON object as retrieved from the Grafana API into
        /// something suitable to post back to the API
        /// </summary>
        public static JObject SanitizeDataSource(JObject datasource)
        {
            string datasourceName = datasource.Value<string>("name");

            var slimmedDatasource = new JObject(datasource);
            slimmedDatasource.Remove("id");
            slimmedDatasource.Remove("orgId");
            slimmedDatasource.Remove("url");

            // Add an entry in secureJsonData for each secureJsonField and decorate as a KeyVault insert.
            var secureFields = datasource.Value<JObject>("secureJsonFields");
            if (secureFields == null)
            {
                return slimmedDatasource;
            }

            var secureJsonData = new JObject();
            foreach (var (name, _) in secureFields)
            {
                secureJsonData[name] = $"[vault(PLACEHOLDER:{datasourceName}:{name})]";
            }

            slimmedDatasource["secureJsonFields"] = secureJsonData;

            return slimmedDatasource;
        }

        public static FolderData SanitizeFolder(JObject folder)
        {
            return new FolderData(folder.Value<string>("uid"), folder.Value<string>("title"));
        }

        /// <summary>
        /// Construct a $.dashboards.item element
        /// </summary>
        public static JObject ConstructDashboardExportObject(JObject dashboard, string folderUid)
        {
            JObject dashboardObject = new JObject(
                new JProperty("dashboard", dashboard),
                new JProperty("meta", new JObject(
                    new JProperty("folderUid", folderUid))));

            return dashboardObject;
        }

        public static JObject SanitizeNotificationChannel(JObject notificationChannel)
        {
            return notificationChannel;
        }
    }

    public class FolderData
    {
        public FolderData(string uid, string title)
        {
            Uid = uid;
            Title = title;
        }

        public string Uid { get; }
        public string Title { get; }

        public int? Id { get; set; }
    }
}

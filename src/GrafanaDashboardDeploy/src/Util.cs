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
    public class Util
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
            var slimmedDashboard = new JObject(dashboard["dashboard"].ToObject<JObject>());
            slimmedDashboard.Remove("id");
            slimmedDashboard.Remove("version");
            return slimmedDashboard;
        }

        /// <summary>
        /// Extract the names of data sources used by a given dashboard. 
        /// </summary>
        /// <param name="dashboard">A JSON definition of a dashboard as delivered by the Grafana API</param>
        /// <returns></returns>
        public static IEnumerable<string> ExtractDataSourceNames(JObject dashboard)
        {
            // Datasources live in panel[*].datasource, unless the "Mixed Datasource" feature
            // is used. Then, get names from panel[*].target.datasource. 
            var datasourceNames = dashboard
                .SelectTokens("$.dashboard.panels[*]..datasource")
                .Values<string>()
                .Where(x => !String.IsNullOrEmpty(x))
                .Where(x => x != "-- Mixed --")
                .Distinct();

            return datasourceNames;
        }
        
        /// <summary>
        /// Modify a Data Source JSON object as retrieved from the Grafana API into
        /// something suitable to post back to the API
        /// </summary>
        public static JObject SanitizeDataSource(JObject dataSource)
        {
            JObject slimmedDatasource = new JObject(dataSource);
            slimmedDatasource.Remove("id");
            slimmedDatasource.Remove("orgId");
            slimmedDatasource.Remove("url");

            // Add an entry in secureJsonData for each secureJsonField and decorate as a KeyVault insert.
            if (dataSource.ContainsKey("secureJsonFields"))
            {
                var secureJsonData = new JObject();
                foreach (var secretField in dataSource["secureJsonFields"].Children<JProperty>())
                {
                    secureJsonData.Add(new JProperty(secretField.Name, $"[vault({secretField.Name})]"));
                }

                slimmedDatasource.Add(new JProperty("secureJsonData", secureJsonData));
            }

            return slimmedDatasource;
        }

        public static JObject SanitizeFolder(JObject folder)
        {
            var folderUid = folder["uid"].Value<string>();
            var folderName = folder["title"].Value<string>();

            return new JObject(
                new JProperty("uid", folderUid),
                new JProperty("title", folderName));
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
    }
}

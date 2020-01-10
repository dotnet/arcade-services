// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
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
        private readonly string[] _environments;
        private readonly JsonSerializer _serializer = new JsonSerializer {Formatting = Formatting.Indented};

        public DeployImporter(
            GrafanaClient grafanaClient,
            string sourceTagValue,
            string dashboardDirectory,
            string datasourceDirectory,
            string notificationsDirectory,
            string[] environments) : base(
            grafanaClient, sourceTagValue, dashboardDirectory, datasourceDirectory, notificationsDirectory)
        {
            _environments = environments;
        }

        public Task ImportFromGrafana(params string[] dashboardUids)
        {
            return ImportFromGrafana((IEnumerable<string>) dashboardUids);
        }

        public async Task ImportFromGrafana(IEnumerable<string> dashboardUids)
        {
            HashSet<string> usedNotificationIds = new HashSet<string>();

            foreach (var dashboardPath in GetAllDashboardPaths())
            {
                if (dashboardUids.Any(d => d.Contains(GetUidFromDashboardFile(dashboardPath))))
                {
                    // This is a new dashboard, don't assume it alert tags
                    JObject data;
                    using (var sr = new StreamReader(dashboardPath))
                    using (var jr = new JsonTextReader(sr))
                    {
                        data = await JObject.LoadAsync(jr).ConfigureAwait(false);
                    }

                    IEnumerable<string> notificationIds = data.SelectTokens("..alertRuleTags.NotificationId")
                        .Select(d => d.Value<string>());
                    foreach (var alertId in notificationIds)
                    {
                        usedNotificationIds.Add(alertId);
                    }
                }
            }

            foreach (string dashboardUid in dashboardUids)
            {
                // Get a dashboard
                JObject dashboard = await GrafanaClient.GetDashboardAsync(dashboardUid).ConfigureAwait(false);

                // Cache dashboard data needed for post
                JObject slimmedDashboard = GrafanaSerialization.SanitizeDashboard(dashboard);

                UpdateNotificationIds(slimmedDashboard, usedNotificationIds);

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
                    JObject datasource = await GrafanaClient.GetDataSourceAsync(datasourceName).ConfigureAwait(false);
                    datasource = GrafanaSerialization.SanitizeDataSource(datasource);

                    // Create the data source for each environment
                    foreach (string env in _environments)
                    {
                        string datasourcePath = GetDatasourcePath(env, datasourceName);
                        if (File.Exists(datasourcePath))
                        {
                            // If we already have that datasource, don't overwrite it's useful values with empty ones
                            continue;
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(datasourcePath));
                        using (var datasourceStreamWriter = new StreamWriter(datasourcePath))
                        using (var datasourceJsonWriter = new JsonTextWriter(datasourceStreamWriter))
                        {
                            _serializer.Serialize(datasourceJsonWriter, datasource);
                        }
                    }
                }

                HashSet<string> usedNotifications = slimmedDashboard
                    .SelectTokens("panels.[*].alert.notifications.[*].uid")
                    .Select(t => t.Value<string>())
                    .Where(uid => !string.IsNullOrEmpty(uid))
                    .ToHashSet();
                foreach (string notificationUid in usedNotifications)
                {
                    JObject notificationChannel = await GrafanaClient.GetNotificationChannelAsync(notificationUid);
                    notificationChannel = GrafanaSerialization.SanitizeNotificationChannel(notificationChannel);

                    // Create the data source for each environment
                    foreach (string env in _environments)
                    {
                        string notificationPath = GetNotificationPath(env, notificationUid);
                        if (File.Exists(notificationPath))
                        {
                            // If we already have that notification, don't overwrite it's useful values with empty ones
                            continue;
                        }
                        
                        Directory.CreateDirectory(Path.GetDirectoryName(notificationPath));
                        using (var datasourceStreamWriter = new StreamWriter(notificationPath))
                        using (var datasourceJsonWriter = new JsonTextWriter(datasourceStreamWriter))
                        {
                            _serializer.Serialize(datasourceJsonWriter, notificationChannel);
                        }
                    }
                }


                string dashboardPath = GetDashboardFilePath(folderData.Title, targetUid);
                Directory.CreateDirectory(Path.GetDirectoryName(dashboardPath));

                using (var dashboardStreamWriter = new StreamWriter(dashboardPath))
                using (var dashboardJsonWriter = new JsonTextWriter(dashboardStreamWriter))
                {
                    _serializer.Serialize(dashboardJsonWriter, dashboardObject);
                }
            }
        }

        private static void UpdateNotificationIds(JObject slimmedDashboard, HashSet<string> usedNotificationIds)
        {
            var alertTags = slimmedDashboard.SelectTokens("..alertRuleTags");
            foreach (var alertTag in alertTags)
            {
                string notificationId = alertTag.Value<string>("NotificationId");
                if (notificationId == null || !usedNotificationIds.Add(notificationId))
                {
                    // We don't have one, or we already used it
                    notificationId = Guid.NewGuid().ToString("N");
                    alertTag["NotificationId"] = notificationId;
                    usedNotificationIds.Add(notificationId);
                }
            }
        }
    }
}

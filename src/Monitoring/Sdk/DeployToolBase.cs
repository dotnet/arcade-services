// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;

namespace Microsoft.DotNet.Monitoring.Sdk
{
    public abstract class DeployToolBase
    {
        protected const string DashboardExtension = ".dashboard.json";
        protected const string DatasourceExtension = ".datasource.json";
        protected const string NotificationExtension = ".notification.json";
        protected const string BaseUidTagPrefix = "baseuid:";
        protected const string SourceTagPrefix = "source:";

        private readonly string _sourceTagValue;

        protected GrafanaClient GrafanaClient { get; }
        protected string DashboardDirectory { get; }
        protected string DatasourceDirectory { get; }
        protected string NotificationDirectory { get; }

        protected DeployToolBase(
            GrafanaClient grafanaClient,
            string sourceTagValue,
            string dashboardDirectory,
            string datasourceDirectory,
            string notificationDirectory)
        {
            GrafanaClient = grafanaClient;
            _sourceTagValue = sourceTagValue;
            DashboardDirectory = dashboardDirectory;
            DatasourceDirectory = datasourceDirectory;
            NotificationDirectory = notificationDirectory;
        }

        protected string SourceTag => SourceTagPrefix + _sourceTagValue;
        
        protected string GetDashboardFilePath(string folder, string uid)
        {
            return Path.Combine(DashboardDirectory, folder, uid + DashboardExtension);
        }
        
        protected string GetDatasourcePath(string environment, string uid)
        {
            return Path.Combine(DatasourceDirectory, environment, uid + DashboardExtension);
        }

        protected string GetNotificationPath(string environment, string uid)
        {
            return Path.Combine(NotificationDirectory, environment, uid + NotificationExtension);
        }

        protected static string GetUidTag(string uid)
        {
            return BaseUidTagPrefix + uid;
        }

        protected string[] GetAllDashboardPaths()
        {
            return Directory.GetFiles(DashboardDirectory,
                "*" + DashboardExtension,
                SearchOption.AllDirectories);
        }

        protected static string GetUidFromDashboardFile(string dashboardFileName)
        {
            return dashboardFileName.Substring(0, dashboardFileName.Length - DashboardExtension.Length);
        }
    }
}

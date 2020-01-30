// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using Microsoft.Build.Utilities;

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
        protected TaskLoggingHelper Log { get; }
        protected string DashboardDirectory { get; }
        protected string DatasourceDirectory { get; }
        protected string NotificationDirectory { get; }

        protected DeployToolBase(
            GrafanaClient grafanaClient,
            string sourceTagValue,
            string dashboardDirectory,
            string datasourceDirectory,
            string notificationDirectory,
            TaskLoggingHelper log)
        {
            GrafanaClient = grafanaClient;
            _sourceTagValue = sourceTagValue;
            DashboardDirectory = dashboardDirectory;
            DatasourceDirectory = datasourceDirectory;
            NotificationDirectory = notificationDirectory;
            Log = log;
        }

        protected string SourceTag => SourceTagPrefix + _sourceTagValue;
        
        protected string GetDashboardFilePath(string folder, string uid)
        {
            return Path.Combine(DashboardDirectory, folder, uid + DashboardExtension);
        }
        
        protected string GetDatasourcePath(string environment, string uid)
        {
            return Path.Combine(DatasourceDirectory, environment, uid + DatasourceExtension);
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
        
        protected static string GetUidFromDashboardFile(string fileName)
        {
            return fileName.Substring(0, fileName.Length - DashboardExtension.Length);
        }

        protected static string GetUidFromNotificationFile(string fileName)
        {
            return fileName.Substring(0, fileName.Length - NotificationExtension.Length);
        }

        protected static string GetNameFromDatasourceFile(string fileName)
        {
            return fileName.Substring(0, fileName.Length - DatasourceExtension.Length);
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;

namespace Microsoft.DotNet.Monitoring.Sdk
{
    public class DeployToolBase
    {
        protected GrafanaClient GrafanaClient { get; }
        protected string SourceTagValue { get; }
        protected string DashboardDirectory { get; }
        protected string DatasourceDirectory { get; }
        protected const string DashboardExtension = ".dashboard.json";
        protected const string DatasourceExtension = ".datasource.json";
        protected const string BaseUidTagPrefix = "baseuid:";
        protected const string SourceTagPrefix = "source:";

        public DeployToolBase(
            GrafanaClient grafanaClient,
            string sourceTagValue,
            string dashboardDirectory,
            string datasourceDirectory)
        {
            GrafanaClient = grafanaClient;
            SourceTagValue = sourceTagValue;
            DashboardDirectory = dashboardDirectory;
            DatasourceDirectory = datasourceDirectory;
        }

        protected string GetDashboardFilePath(string folder, string uid)
        {
            return Path.Combine(DashboardDirectory, folder, uid + DashboardExtension);
        }

        protected string GetDatasourceFilePath(string datasourceName)
        {
            return Path.Combine(DatasourceDirectory, datasourceName + DatasourceExtension);
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;

namespace DependencyUpdateErrorProcessor
{
    class DependencyUpdateErrorProcessorOptions
    {
        public IConfigurationRefresher ConfigurationRefresherEndPointUri { get; set; }
        public IConfiguration DynamicConfigs { get; set; }
        public string GithubUrl { get; set; }
        public string Owner { get; set; }
        public string Repository { get; set; }
        public string FyiHandle { get; set; }
    }
}

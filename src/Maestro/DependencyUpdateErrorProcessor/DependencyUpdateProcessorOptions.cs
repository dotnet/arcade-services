// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;

namespace DependencyUpdateErrorProcessor
{
    public class DependencyUpdateErrorProcessorOptions
    {
        public string GithubUrl { get; set; }
        public string FyiHandle { get; set; }
        public bool IsEnabled { get; set; }
    }
}

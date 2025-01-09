// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Options;

public interface ICommandLineOptions
{
    string AzureDevOpsPat { get; set; }
    string BuildAssetRegistryBaseUri { get; set; }
    string BuildAssetRegistryToken { get; set; }
    string GitHubPat { get; set; }
    string GitLocation { get; set; }
    DarcOutputType OutputFormat { get; set; }
    bool Debug { get; set; }
    bool Verbose { get; set; }
    bool IsCi { get; set; }

    Operation GetOperation(ServiceProvider sp);
    IRemoteTokenProvider GetRemoteTokenProvider();
    IAzureDevOpsTokenProvider GetAzdoTokenProvider();
    IRemoteTokenProvider GetGitHubTokenProvider();

    /// <summary>
    /// Reads missing options from the local settings.
    /// </summary>
    void InitializeFromSettings(ILogger logger);
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.Darc.Options;
public interface ICommandLineOptions
{
    string AzureDevOpsPat { get; set; }
    string BuildAssetRegistryBaseUri { get; set; }
    string FederatedToken { get; set; }
    string BuildAssetRegistryToken { get; set; }
    string GitHubPat { get; set; }
    string GitLocation { get; set; }
    DarcOutputType OutputFormat { get; set; }
    bool Debug { get; set; }
    bool Verbose { get; set; }
    bool IsCi { get; set; }

    Operation GetOperation();
    RemoteTokenProvider GetRemoteConfiguration();

    /// <summary>
    /// Reads missing options from the local settings.
    /// </summary>
    void InitializeFromSettings(ILogger logger);
}

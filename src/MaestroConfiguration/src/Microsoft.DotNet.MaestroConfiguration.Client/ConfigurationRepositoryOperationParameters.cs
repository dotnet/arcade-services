// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.MaestroConfiguration.Client;

public class ConfigurationRepositoryOperationParameters
{
    public required string RepositoryUri { get; init; }
    public required string ConfigurationBaseBranch { get; init; }
    public string? ConfigurationBranch { get; set; }
    public required bool DontOpenPr { get; init; }
    public string? ConfigurationFilePath { get; init; }
}

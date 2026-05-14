// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.Darc.Options;

namespace Microsoft.DotNet.Darc.Operations;

/// <summary>
/// Base class for operations that work with the configuration repository.
/// Provides common functionality such as printing the --configuration-branch hint.
/// </summary>
internal abstract class ConfigurationManagementOperationBase : Operation
{
    private readonly IConfigurationManagementCommandLineOptions _configOptions;

    protected ConfigurationManagementOperationBase(IConfigurationManagementCommandLineOptions configOptions)
    {
        _configOptions = configOptions;
    }

    /// <summary>
    /// Prints a hint suggesting the user pass --configuration-branch to group further changes into the same PR.
    /// Only printed when no configuration branch was explicitly supplied.
    /// </summary>
    protected void PrintConfigurationBranchHintIfNeeded()
    {
        if (string.IsNullOrEmpty(_configOptions.ConfigurationBranch))
        {
            Console.WriteLine();
            Console.WriteLine($"💡 Making more changes? Supply --configuration-branch {_configOptions.GetOrGenerateConfigurationBranch()} with the next darc command to clump the changes in one PR.");
        }
    }
}

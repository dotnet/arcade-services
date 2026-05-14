// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;

namespace Microsoft.DotNet.Darc.Operations;

/// <summary>
/// Base class for operations that work with the configuration repository.
/// Implements the Template Method pattern: <see cref="ExecuteAsync"/> calls the abstract
/// <see cref="ExecuteInternalAsync"/> and then prints the --configuration-branch hint on success.
/// </summary>
internal abstract class ConfigurationManagementOperationBase : Operation
{
    private readonly IConfigurationManagementCommandLineOptions _configOptions;

    protected ConfigurationManagementOperationBase(IConfigurationManagementCommandLineOptions configOptions)
    {
        _configOptions = configOptions;
    }

    /// <summary>
    /// Prints an introductory message, executes the operation, and on success prints a hint to reuse the configuration branch.
    /// </summary>
    public sealed override async Task<int> ExecuteAsync()
    {
        Console.WriteLine($"This command will make changes to the configuration repository '{_configOptions.ConfigurationRepository}' on branch '{_configOptions.GetOrGenerateConfigurationBranch()}'.");
        int exitCode = await ExecuteInternalAsync();
        if (exitCode == Constants.SuccessCode)
        {
            PrintConfigurationBranchHintIfNeeded();
        }
        return exitCode;
    }

    /// <summary>
    /// Performs the actual work of the operation. Return <see cref="Constants.SuccessCode"/> on success
    /// or <see cref="Constants.ErrorCode"/> on failure.
    /// </summary>
    protected abstract Task<int> ExecuteInternalAsync();

    private void PrintConfigurationBranchHintIfNeeded()
    {
        if (string.IsNullOrEmpty(_configOptions.ConfigurationBranch))
        {
            Console.WriteLine();
            Console.WriteLine($"💡 Making more changes? Supply `--configuration-branch {_configOptions.GetOrGenerateConfigurationBranch()}` with the next darc command to clump the changes in one PR.");
        }
    }
}

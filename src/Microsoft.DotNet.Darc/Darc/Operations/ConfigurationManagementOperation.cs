// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.Darc.Operations;

internal abstract class ConfigurationManagementOperation : Operation
{
    private const string UseConfigRepositoryEnvVar = "DARC_USE_CONFIGURATION_REPOSITORY";

    protected static bool ShouldUseConfigurationRepository()
    {
        var val = Environment.GetEnvironmentVariable(UseConfigRepositoryEnvVar);
        return !string.IsNullOrEmpty(val) && bool.Parse(val);
    }
}

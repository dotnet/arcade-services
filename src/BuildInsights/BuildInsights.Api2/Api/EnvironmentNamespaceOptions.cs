// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Api;

public class EnvironmentNamespaceOptions
{
    public const string ConfigurationKey = "EnvironmentNamespaceOptions";

    public required string DefaultNamespaceName { get; init; }
}

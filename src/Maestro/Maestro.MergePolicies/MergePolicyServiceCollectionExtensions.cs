// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace Maestro.MergePolicies;

public static class MergePolicyServiceCollectionExtensions
{
    public static IServiceCollection AddMergePolicies(this IServiceCollection services)
    {
        services.AddTransient<IMergePolicyBuilder, AllChecksSuccessfulMergePolicyBuilder>();
        services.AddTransient<IMergePolicyBuilder, NoRequestedChangesMergePolicyBuilder>();
        services.AddTransient<IMergePolicyBuilder, DontAutomergeDowngradesMergePolicyBuilder>();
        services.AddTransient<IMergePolicyBuilder, StandardMergePolicyBuilder>();
        services.AddTransient<IMergePolicyBuilder, ValidateCoherencyMergePolicyBuilder>();
        services.AddTransient<IMergePolicyBuilder, CodeFlowMergePolicyBuilder>();
        return services;
    }
}

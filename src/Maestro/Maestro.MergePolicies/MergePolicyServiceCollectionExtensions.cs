// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace Maestro.MergePolicies;

public static class MergePolicyServiceCollectionExtensions
{
    public static IServiceCollection AddMergePolicyBuilder(this IServiceCollection services)
    {
        services.AddTransient<MergePolicyBuilder>();
        return services;
    }
}

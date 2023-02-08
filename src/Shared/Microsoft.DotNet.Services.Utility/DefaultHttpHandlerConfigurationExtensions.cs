// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Http;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class DefaultHttpHandlerConfigurationExtensions
{
    /// <summary>
    /// Add a handler that will be applied to all HttpClients returned from IHttpClientFactory
    /// </summary>
    public static IServiceCollection AddDefaultHttpHandler<T>(this IServiceCollection services) where T : DelegatingHandler
    {
        services.AddSingleton<IHttpMessageHandlerBuilderFilter, DefaultHttpHandlerConfiguration<T>>();
        return services;
    }
}

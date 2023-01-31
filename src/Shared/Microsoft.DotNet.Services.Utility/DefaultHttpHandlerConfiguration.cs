// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Microsoft.DotNet.Services.Utility;

internal class DefaultHttpHandlerConfiguration<T> : IHttpMessageHandlerBuilderFilter where T : DelegatingHandler
{
    private readonly IServiceProvider _services;

    public DefaultHttpHandlerConfiguration(IServiceProvider services)
    {
        _services = services;
    }

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        return builder =>
        {
            builder.AdditionalHandlers.Add(ActivatorUtilities.CreateInstance<T>(_services));
            next(builder);
        };
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Reflection;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.DotNet.Services.Utility;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace BuildInsights.Utilities.AzureDevOps;

public sealed class VssConnectionProvider : IDisposable
{
    private readonly IAzureDevOpsTokenProvider _tokenProvider;
    private readonly IEnumerable<AzureDevOpsDelegatingHandler> _handlers;
    private readonly ConcurrentDictionary<string, VssConnection> _connections = [];
    private readonly Lazy<ProductInfoHeaderValue> _productInfoHeaderValue = new(GetClientHeader);

    public VssConnectionProvider(
        IAzureDevOpsTokenProvider tokenProvider,
        IEnumerable<AzureDevOpsDelegatingHandler> handlers)
    {
        _tokenProvider = tokenProvider;
        _handlers = handlers;
    }

    private VssConnection CreateConnection(string orgId)
    {
        var accessToken = _tokenProvider.GetTokenForAccount(orgId);
        VssCredentials credentials = new VssBasicCredential(string.Empty, accessToken);
        var settings = new VssClientHttpRequestSettings { UserAgent = [_productInfoHeaderValue.Value], SendTimeout = TimeSpan.FromMinutes(5), };
        return new VssConnection(
            new Uri($"https://dev.azure.com/{orgId}", UriKind.Absolute),
            new VssHttpMessageHandler(credentials, settings),
            _handlers);
    }

    public VssConnection GetConnection(string orgId)
    {
        if (!_connections.TryGetValue(orgId, out VssConnection? connection))
        {
            connection = CreateConnection(orgId);
            _connections[orgId] = connection;
        }

        return connection;
    }

    public void Dispose()
    {
        foreach (var connection in _connections.Values)
        {
            connection.Dispose();
        }
    }

    private static ProductInfoHeaderValue GetClientHeader()
    {
        string assemblyName = Assembly.GetEntryAssembly()?.GetName().Name ?? "BuildResultAnalysis";
        string assemblyVersion =
            Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? "42.42.42.42";
        return new(assemblyName, assemblyVersion);
    }
}

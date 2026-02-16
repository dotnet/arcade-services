// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using BuildInsights.Utilities.AzureDevOps.Models;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace BuildInsights.Utilities.AzureDevOps;

public sealed class VssConnectionProvider : IDisposable
{
    private readonly IOptionsMonitor<AzureDevOpsSettingsCollection> _settings;
    private readonly IEnumerable<AzureDevOpsDelegatingHandler> _handlers;
    private readonly ConcurrentDictionary<string, VssConnection> _connections = [];
    private readonly Lazy<ProductInfoHeaderValue> _productInfoHeaderValue = new(GetClientHeader);

    public VssConnectionProvider(IOptionsMonitor<AzureDevOpsSettingsCollection> settings, IEnumerable<AzureDevOpsDelegatingHandler> handlers)
    {
        _settings = settings;
        _handlers = handlers;
    }

    private VssConnection CreateConnection(string orgId)
    {
        AzureDevOpsSettings options = _settings.CurrentValue.Settings.FirstOrDefault(s => s.OrgId.Equals(orgId, StringComparison.InvariantCultureIgnoreCase))
            ?? _settings.CurrentValue.Settings.FirstOrDefault(s => s.OrgId.Equals("default", StringComparison.InvariantCultureIgnoreCase))
            ?? throw new InvalidOperationException($"Azure DevOps organization {orgId} has not been configured");

        VssCredentials credentials = new VssBasicCredential(string.Empty, options.AccessToken);
        var settings = new VssClientHttpRequestSettings { UserAgent = [_productInfoHeaderValue.Value], SendTimeout = TimeSpan.FromMinutes(5), };
        return new VssConnection(
            new Uri(options.CollectionUri, UriKind.Absolute),
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

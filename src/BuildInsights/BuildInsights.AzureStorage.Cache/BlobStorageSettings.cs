// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

#nullable disable
namespace BuildInsights.AzureStorage.Cache;

public class BlobStorageSettings
{
    public string Endpoint { get; set; }

    public string ContainerName { get; set; }

    /// <summary>
    /// How long to wait before requesting to renew the acquired lease
    /// </summary>
    public TimeSpan LeaseRenewalTimespan { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long to wait before retrying to acquire a lease that may already be leased by another process
    /// </summary>
    public TimeSpan LeaseAcquireRetryWaitTime { get; set; } = TimeSpan.FromSeconds(15);
}

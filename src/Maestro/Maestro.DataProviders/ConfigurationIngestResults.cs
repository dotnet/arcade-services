// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Maestro.DataProviders;

public class ConfigurationIngestResults
{
    public ConfigurationIngestResult Subscriptions { get; } = new();
    public ConfigurationIngestResult Channels { get; } = new();
    public ConfigurationIngestResult DefaultChannels { get; } = new();
}

public class ConfigurationIngestResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
}

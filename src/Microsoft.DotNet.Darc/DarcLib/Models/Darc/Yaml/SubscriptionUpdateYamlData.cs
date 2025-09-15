// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using YamlDotNet.Serialization;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models.Darc.Yaml;

/// <summary>
/// Helper class for YAML encoding/decoding purposes.
/// This is used so that we can have friendly alias names for elements.
/// </summary>
#nullable disable
public class SubscriptionUpdateYamlData : InputSubscriptionYamlData
{
    public const string EnabledElement = "Enabled";

    [YamlMember(ApplyNamingConventions = false)]
    public string Id { get; set; }

    [YamlMember(Alias = EnabledElement, ApplyNamingConventions = false)]
    public string Enabled { get; set; }
}

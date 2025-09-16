// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using YamlDotNet.Serialization;


namespace Microsoft.DotNet.DarcLib.Models.Darc.Yaml;

public class SubscriptionUpdateYamlData : SubscriptionYamlData
{
    public const string EnabledElement = "Enabled";

    [YamlMember(Alias = EnabledElement, ApplyNamingConventions = false)]
    public string Enabled { get; set; }
}

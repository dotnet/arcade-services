// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using YamlDotNet.Serialization;

namespace Microsoft.DotNet.DarcLib.Models.Darc.Yaml;

public class ChannelYamlData
{
    public string Name { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults)]
    public string Classification { get; set; }
}

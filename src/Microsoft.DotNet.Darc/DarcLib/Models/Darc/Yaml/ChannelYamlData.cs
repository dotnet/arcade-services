// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.DarcLib.Models.Darc.Yaml;

public class ChannelYamlData
{
    public string Name { get; set; }
    public string Classification { get; set; }
    public string Description { get; set; }
    public bool IsInternal { get; set; }
}

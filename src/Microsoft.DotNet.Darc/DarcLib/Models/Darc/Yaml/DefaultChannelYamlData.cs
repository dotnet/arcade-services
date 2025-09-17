// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.DarcLib.Models.Darc.Yaml;

public class DefaultChannelYamlData
{
    public string Repository { get; set; }
    public string Branch { get; set; }
    public string Channel { get; set; }
    public bool Enabled { get; set; } = true;
}

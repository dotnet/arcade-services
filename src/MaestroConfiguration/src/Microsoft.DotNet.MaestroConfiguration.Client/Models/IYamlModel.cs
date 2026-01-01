// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;

namespace Microsoft.DotNet.MaestroConfiguration.Client.Models;

/// <summary>
/// Marker interface for YAML configuration models.
/// </summary>
public interface IYamlModel : IComparer
{
    string GetUniqueId();

    string GetDefaultFilePath();
}

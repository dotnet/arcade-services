// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen;

internal class ConfigParameters
{
    public ConfigParameters(string name, string type, bool isNullable)
    {
        Name = name;
        Type = type;
        IsNullable = isNullable;
    }

    public string Name { get; }
    public string Type { get; }
    public bool IsNullable { get; }
}

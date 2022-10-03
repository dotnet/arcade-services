// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen;

internal class ConfigMethod
{
    public ConfigMethod(
        string name,
        List<ConfigParameters> parameters,
        string returnTypeSymbol,
        bool configureAllParameters,
        bool isConfigurationAsync,
        bool isFetchAsync,
        bool hasFetch)
    {
        Name = name;
        Parameters = parameters;
        if (returnTypeSymbol != null)
        {
            if (returnTypeSymbol.StartsWith("("))
            {
                ReturnTypeSymbol = returnTypeSymbol;
            }
            else
            {
                ReturnTypeSymbol = "global::" + returnTypeSymbol;
            }
        }
        ConfigureAllParameters = configureAllParameters;
        IsConfigurationAsync = isConfigurationAsync;
        IsFetchAsync = isFetchAsync;
        HasFetch = hasFetch;
    }

    public string Name { get; }
    public List<ConfigParameters> Parameters { get; }
    public string ReturnTypeSymbol { get; }
    public bool HasFetch { get; }
    public bool ConfigureAllParameters { get; }
    public bool IsConfigurationAsync { get; }
    public bool IsFetchAsync { get; }

    public string GetItemName(TestDataClassWriter.NameFormat format)
    {
        string name = Name;
        if (name.StartsWith("Get"))
        {
            name = name.Substring(3);
        }

        return TestDataClassWriter.FormatName(format, name);
    }
}

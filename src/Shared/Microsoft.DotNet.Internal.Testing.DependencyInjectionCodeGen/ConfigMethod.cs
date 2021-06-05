// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen
{
    internal class ConfigMethod
    {
        public ConfigMethod(
            string name,
            List<ConfigParameters> parameters,
            string returnTypeSymbol,
            bool configureAllParameters,
            bool isAsync)
        {
            Name = name;
            Parameters = parameters;
            ReturnTypeSymbol = returnTypeSymbol;
            ConfigureAllParameters = configureAllParameters;
            IsAsync = isAsync;
        }

        public string Name { get; }
        public List<ConfigParameters> Parameters { get; }
        public string ReturnTypeSymbol { get; }
        public bool ConfigureAllParameters { get; }
        public bool IsAsync { get; }

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
}

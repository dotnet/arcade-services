// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen
{
    internal class TestConfigDeclaration
    {
        public TestConfigDeclaration(
            string namespaceName,
            string parentName,
            ISymbol declaringClassSymbol,
            ClassDeclarationSyntax classDeclarationSyntax)
        {
            NamespaceName = namespaceName;
            ParentName = parentName;
            DeclaringClassSymbol = declaringClassSymbol;
            ClassDeclarationSyntax = classDeclarationSyntax;
            ConfigClassName = declaringClassSymbol.Name;
        }

        public string NamespaceName { get; }
        public string ParentName { get; }
        public ISymbol DeclaringClassSymbol { get; }
        public ClassDeclarationSyntax ClassDeclarationSyntax { get; }
        public string ConfigClassName { get; }
        public List<ConfigMethod> Methods { get; } = new List<ConfigMethod>();
    }
}

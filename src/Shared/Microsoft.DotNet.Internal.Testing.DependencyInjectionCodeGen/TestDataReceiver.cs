// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpExtensions = Microsoft.CodeAnalysis.CSharp.CSharpExtensions;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen
{
    public enum DiagnosticIds
    {
        NestedClassRequired,
        PartialClassRequired,
        NamespaceRequired,
        StaticClassRequired
    }

    internal class TestDataReceiver : ISyntaxContextReceiver
    {
        public List<TestConfigDeclaration> Declarations { get; } = new List<TestConfigDeclaration>();
        public List<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();

        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            if (context.Node is ClassDeclarationSyntax cls)
            {
                ISymbol currentType = ModelExtensions.GetDeclaredSymbol(context.SemanticModel, cls);
                if (HasNamedAttribute(
                    currentType,
                    "Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions.TestDependencyInjectionSetupAttribute"
                ))
                {
                    bool error = false;
                    var nested = cls.Parent as ClassDeclarationSyntax;
                    if (nested == null)
                    {
                        Diagnostics.Add(Error(DiagnosticIds.NestedClassRequired, $"Class {cls.Identifier.Text} must be nested inside class to use [TestDependencyInjectionSetupAttribute]", cls));
                        error = true;
                    }
                    
                    if (cls.Modifiers.All(m => m.Kind() != SyntaxKind.StaticKeyword))
                    {
                        Diagnostics.Add(Error(DiagnosticIds.StaticClassRequired, $"Class {cls.Identifier.Text} must be declared static to use [TestDependencyInjectionSetupAttribute]", nested));
                        error = true;
                    }

                    if (error)
                        return;

                    if (nested.Modifiers.All(m => m.Kind() != SyntaxKind.PartialKeyword))
                    {
                        Diagnostics.Add(Error(DiagnosticIds.PartialClassRequired, $"Class {nested.Identifier.Text} must be declared partial to use [TestDependencyInjectionSetupAttribute]", nested));
                        error = true;
                    }

                    if (!(nested.Parent is NamespaceDeclarationSyntax))
                    {
                        Diagnostics.Add(Error(DiagnosticIds.NamespaceRequired, $"Class {nested.Identifier.Text} must be inside a namespace to use [TestDependencyInjectionSetupAttribute]", nested));
                        error = true;
                    }
                    
                    if (error)
                        return;

                    ISymbol parentType = ModelExtensions.GetDeclaredSymbol(context.SemanticModel, nested);
                    Declarations.Add(
                        new TestConfigDeclaration(
                            SymbolNameHelper.FullName(parentType.ContainingNamespace),
                            nested.Identifier.Text,
                            currentType,
                            cls
                        )
                    );
                }
            }

            if (context.Node is MethodDeclarationSyntax method)
            {
                var sMethod = ModelExtensions.GetDeclaredSymbol(context.SemanticModel, method) as IMethodSymbol;
                TestConfigDeclaration decl = Declarations.FirstOrDefault(
                    d => d.DeclaringClassSymbol.Equals(sMethod.ContainingType, SymbolEqualityComparer.Default)
                );

                if (decl == null)
                {
                    return;
                }

                if (sMethod.Parameters.Length == 0)
                {
                    return;
                }

                if (SymbolNameHelper.FullName(sMethod.Parameters[0].Type) !=
                    "Microsoft.Extensions.DependencyInjection.IServiceCollection")
                {
                    return;
                }

                string configType;
                bool isAsync;
                if (sMethod.ReturnsVoid)
                {
                    configType = null;
                    isAsync = false;
                }
                else if (!(sMethod.ReturnType is INamedTypeSymbol returnType))
                {
                    return;
                }
                else if (returnType.IsGenericType)
                {
                    if (SymbolNameHelper.FullName(returnType.ConstructedFrom) == "System.Func<T,TResult>")
                    {
                        if (SymbolNameHelper.FullName(returnType.TypeArguments[0]) != "System.IServiceProvider")
                        {
                            return;
                        }

                        configType = SymbolNameHelper.FullName(returnType.TypeArguments[1]);
                        isAsync = false;
                    }
                    else if (SymbolNameHelper.FullName(returnType.ConstructedFrom) == "System.Threading.Tasks.Task<TResult>")
                    {
                        if (returnType.TypeArguments[0] is INamedTypeSymbol asyncReturn &&
                            asyncReturn.IsGenericType &&
                            SymbolNameHelper.FullName(asyncReturn.ConstructedFrom) == "System.Func<T,TResult>" &&
                            SymbolNameHelper.FullName(asyncReturn.TypeArguments[0]) != "System.IServiceProvider")
                        {
                            configType = SymbolNameHelper.FullName(asyncReturn.TypeArguments[1]);
                            isAsync = true;
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }
                else if (SymbolNameHelper.FullName(returnType) == "System.Threading.Tasks.Task")
                {
                    isAsync = true;
                    configType = null;
                }
                else
                {
                    return;
                }

                decl.Methods.Add(
                    new ConfigMethod(
                        sMethod.Name,
                        sMethod.Parameters.Skip(1)
                            .Select(
                                p => new ConfigParameters(
                                    p.Name,
                                    SymbolNameHelper.FullName(p.Type),
                                    IsNullable(p.Type)
                                )
                            )
                            .ToList(),
                        configType,
                        HasNamedAttribute(sMethod, "Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions.ConfigureAllParametersAttribute"),
                        isAsync
                    )
                );
            }
        }

        private Diagnostic Error(DiagnosticIds id, LocalizableString message, CSharpSyntaxNode locationNode)
        {
            return Diagnostic.Create(
                ((int) id).ToString("D4"),
                "TDG",
                message,
                DiagnosticSeverity.Error,
                DiagnosticSeverity.Error,
                true,
                0,
                location: locationNode.GetLocation()
            );
        }

        private static bool HasNamedAttribute(ISymbol currentType, string attributeName)
        {
            return currentType.GetAttributes().Any(a => SymbolNameHelper.FullName(a.AttributeClass) == attributeName);
        }

        private static bool IsNullable(ITypeSymbol type)
        {
            return type.IsReferenceType ||
                type is INamedTypeSymbol namedPType &&
                namedPType.IsGenericType &&
                !namedPType.IsUnboundGenericType &&
                SymbolNameHelper.FullName(namedPType.ConstructedFrom) == "System.Nullable";
        }
    }
}

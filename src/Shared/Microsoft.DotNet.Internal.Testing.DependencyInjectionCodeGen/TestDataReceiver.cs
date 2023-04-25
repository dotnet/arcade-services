// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen;

public enum DiagnosticIds
{
    NestedClassRequired,
    PartialClassRequired,
    NamespaceRequired,
    StaticClassRequired,
    DependencyInjectionRequired,
    UnresolvedClass,
    ParentCannotBeNested,
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
                    
                if (cls.Modifiers.All(m => !m.IsKind(SyntaxKind.StaticKeyword)))
                {
                    Diagnostics.Add(Error(DiagnosticIds.StaticClassRequired, $"Class {cls.Identifier.Text} must be declared static to use [TestDependencyInjectionSetupAttribute]", nested));
                    error = true;
                }

                if (!HasDependencyInjectionReference(context))
                {
                    Diagnostics.Add(
                        Error(
                            DiagnosticIds.DependencyInjectionRequired,
                            "Missing reference to Microsoft.Extensions.DependencyInjection to use [TestDependencyInjectionSetupAttribute]",
                            cls
                        )
                    );
                    error = true;
                }

                if (error)
                    return;

                if (nested.Modifiers.All(m => !m.IsKind(SyntaxKind.PartialKeyword)))
                {
                    Diagnostics.Add(Error(DiagnosticIds.PartialClassRequired, $"Class {nested.Identifier.Text} must be declared partial to use [TestDependencyInjectionSetupAttribute]", nested));
                    error = true;
                }
                
                if (error)
                    return;

                ISymbol parentSymbol = ModelExtensions.GetDeclaredSymbol(context.SemanticModel, nested);
                if (parentSymbol is not INamedTypeSymbol parentType)
                {
                    Diagnostics.Add(Error(DiagnosticIds.UnresolvedClass, $"Class declaration {nested.Identifier.Text} does not resolve to a named type symbol.", nested));
                    return;
                }
                if (parentType.ContainingNamespace == null)
                {
                    Diagnostics.Add(Error(DiagnosticIds.NamespaceRequired, $"Class {nested.Identifier.Text} must be inside a namespace to use [TestDependencyInjectionSetupAttribute]", nested));
                    return;
                }
                if (parentType.ContainingType != null)
                {
                    Diagnostics.Add(Error(DiagnosticIds.ParentCannotBeNested, $"Class {nested.Identifier.Text} cannot be nested inside a type to use [TestDependencyInjectionSetupAttribute]", nested));
                    return;
                }

                Declarations.Add(
                    new TestConfigDeclaration(
                        parentType.ContainingNamespace.FullName(),
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

            if (sMethod.Parameters[0].Type.FullName() !=
                "Microsoft.Extensions.DependencyInjection.IServiceCollection")
            {
                return;
            }

            string configType;
            bool isConfigurationAsync;
            bool hasFetch;
            bool isFetchAsync;
            if (sMethod.ReturnsVoid)
            {
                configType = null;
                isConfigurationAsync = false;
                isFetchAsync = false;
                hasFetch = false;
            }
            else if (!(sMethod.ReturnType is INamedTypeSymbol returnType))
            {
                return;
            }
            else if (returnType.FullName() == "System.Threading.Tasks.Task")
            {
                configType = null;
                isConfigurationAsync = true;
                isFetchAsync = false;
                hasFetch = false;
            }
            else if (returnType.IsGenericType)
            {
                if (returnType.ConstructedFrom.FullName() == "System.Action<T>" && returnType.TypeArguments[0].FullName() == "System.IServiceProvider")
                {
                    configType = null;
                    hasFetch = true;
                    isFetchAsync = false;
                    isConfigurationAsync = false;
                }
                else if (returnType.ConstructedFrom.FullName() == "System.Func<T,TResult>")
                {
                    isConfigurationAsync = false;
                    hasFetch = true;

                    if (returnType.TypeArguments[0].FullName() != "System.IServiceProvider")
                    {
                        return;
                    }


                    ITypeSymbol funcReturn = returnType.TypeArguments[1];
                    if (funcReturn.FullName() == "System.Threading.Tasks.Task")
                    {
                        configType = null;
                        isFetchAsync = true;

                    }
                    else if (funcReturn is INamedTypeSymbol {IsGenericType: true} namedFuncReturn &&
                             namedFuncReturn.ConstructedFrom.FullName() == "System.Threading.Tasks.Task<TResult>")
                    {
                        configType = namedFuncReturn.TypeArguments[0].FullName();
                        isFetchAsync = true;
                    }
                    else
                    {
                        configType = funcReturn.FullName();
                        isFetchAsync = false;
                    }
                }
                else if (returnType.ConstructedFrom.FullName() == "System.Threading.Tasks.Task<TResult>")
                {
                    isConfigurationAsync = true;

                    if (returnType.TypeArguments[0] is INamedTypeSymbol {IsGenericType: true} asyncReturn)
                    {
                        if (asyncReturn.ConstructedFrom.FullName() == "System.Action<T>" && asyncReturn.TypeArguments[0].FullName() == "System.IServiceProvider")
                        {
                            configType = null;
                            hasFetch = true;
                            isFetchAsync = false;
                        }
                        else if (asyncReturn.ConstructedFrom.FullName() == "System.Func<T,TResult>" && asyncReturn.TypeArguments[0].FullName() == "System.IServiceProvider")
                        {
                            hasFetch = true;
                            ITypeSymbol funcReturn = asyncReturn.TypeArguments[1];
                            if (funcReturn.FullName() == "System.Threading.Tasks.Task")
                            {
                                configType = null;
                                isFetchAsync = true;

                            }
                            else if (funcReturn is INamedTypeSymbol {IsGenericType: true} namedFuncReturn &&
                                     namedFuncReturn.ConstructedFrom.FullName() == "System.Threading.Tasks.Task<TResult>")
                            {
                                configType = namedFuncReturn.TypeArguments[0].FullName();
                                isFetchAsync = true;
                            }
                            else
                            {
                                configType = funcReturn.FullName();
                                isFetchAsync = false;
                            }
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
                else
                {
                    return;
                }
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
                                p.Type.FullName(),
                                IsNullable(p.Type)
                            )
                        )
                        .ToList(),
                    configType,
                    HasNamedAttribute(sMethod, "Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions.ConfigureAllParametersAttribute"),
                    isConfigurationAsync,
                    isFetchAsync,
                    hasFetch
                )
            );
        }
    }

    private static bool HasDependencyInjectionReference(GeneratorSyntaxContext context)
    {
        INamedTypeSymbol serviceProviderExtensions = context.SemanticModel.Compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions"
        );
        if (serviceProviderExtensions == null)
            return false;

        var buildMethod = serviceProviderExtensions.GetMembers("BuildServiceProvider");
        if (buildMethod.IsDefaultOrEmpty)
            return false;
            
        INamedTypeSymbol serviceCollection = context.SemanticModel.Compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection"
        );

        return buildMethod.Any(
            b => b.IsStatic &&
                 b.Kind == SymbolKind.Method &&
                 b is IMethodSymbol {Parameters: {Length: 1}} bMethod &&
                 bMethod.Parameters[0].Type
                     .Equals(
                         serviceCollection,
                         SymbolEqualityComparer.Default
                     )
        );
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
        return currentType.GetAttributes().Any(a => a.AttributeClass.FullName() == attributeName);
    }

    private static bool IsNullable(ITypeSymbol type)
    {
        return type.IsReferenceType ||
               type is INamedTypeSymbol namedPType &&
               namedPType.IsGenericType &&
               !namedPType.IsUnboundGenericType &&
               namedPType.ConstructedFrom.FullName() == "System.Nullable<T>";
    }
}

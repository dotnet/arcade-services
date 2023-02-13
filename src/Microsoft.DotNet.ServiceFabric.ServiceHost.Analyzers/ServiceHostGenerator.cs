using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Analyzers;

[Generator]
public class ServiceHostGenerator : IIncrementalGenerator
{
    private delegate void ServiceCodeGenerator(IncrementalGeneratorInitializationContext context, IncrementalValuesProvider<(InvocationExpressionSyntax node, SemanticModel model)> invocation);

    private static readonly Dictionary<string, ServiceCodeGenerator> Generators = new()
    {
        ["RegisterStatefulActorService"] = StatefulActorServiceGenerator.Invoke,
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        foreach (var methodName in Generators.Keys)
        {
            var invocations = context.SyntaxProvider.CreateSyntaxProvider((n, _) =>
                n is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax
                    {
                        RawKind: (int) SyntaxKind.SimpleMemberAccessExpression,
                        Name: GenericNameSyntax
                        {
                            Identifier.Text: string name,
                        },
                    }
                } && name == methodName, (syntax, _) => ((InvocationExpressionSyntax)syntax.Node, syntax.SemanticModel));
            Generators[methodName](context, invocations);
        }
    }
}

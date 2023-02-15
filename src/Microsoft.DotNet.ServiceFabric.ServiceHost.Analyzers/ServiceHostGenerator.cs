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

        bool IsRegisterMethod(SyntaxNode node, string name)
        {
            if (node is not InvocationExpressionSyntax invocationExpression)
            {
                return false;
            }

            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccessExpression)
            {
                return false;
            }

            if (memberAccessExpression.Name is not GenericNameSyntax nameSyntax)
            {
                return false;
            }

            if (nameSyntax.Identifier.Text != name)
            {
                return false;
            }

            return true;
        }

        foreach (var methodName in Generators.Keys)
        {
            var invocations = context.SyntaxProvider.CreateSyntaxProvider(
                (n, _) => IsRegisterMethod(n, methodName),
                (syntax, _) => ((InvocationExpressionSyntax) syntax.Node, syntax.SemanticModel));
            Generators[methodName](context, invocations);
        }
    }
}

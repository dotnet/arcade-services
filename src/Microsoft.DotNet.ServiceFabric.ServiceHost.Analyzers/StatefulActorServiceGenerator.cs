using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Analyzers;

public static class Diagnostics
{
#pragma warning disable RS2008
    public static readonly DiagnosticDescriptor InvalidActorRegisterMethodParameters = new DiagnosticDescriptor(
        "SH001",
        "Actor register method parameter must be a constant string",
        "Actor register method parameter just be a constant string", 
        "ServiceHost",
        DiagnosticSeverity.Error, 
        true);
    public static readonly DiagnosticDescriptor InvalidActorRegisterMethodTypeArgs = new DiagnosticDescriptor(
        "SH002",
        "Actor register method type arguments must be a single type",
        "Actor register method type arguments must be a single type",
        "ServiceHost",
        DiagnosticSeverity.Error, 
        true);
    public static readonly DiagnosticDescriptor ActorTypeMustBeDefinedInSource = new DiagnosticDescriptor(
        "SH003",
        "Actor implementation type must be defined in source",
        "Actor implementation type must be defined in source",
        "ServiceHost",
        DiagnosticSeverity.Error, 
        true);
    public static readonly DiagnosticDescriptor ActorTypeMustBePartial = new DiagnosticDescriptor(
        "SH004",
        "Actor implementation type must be partial",
        "Actor implementation type must be partial",
        "ServiceHost",
        DiagnosticSeverity.Error, 
        true);
}
public static class StatefulActorServiceGenerator
{
    public static void Invoke(IncrementalGeneratorInitializationContext context, IncrementalValuesProvider<(InvocationExpressionSyntax node, SemanticModel model)> invocation)
    {
        context.RegisterSourceOutput(invocation, (ctx, data) => Execute(ctx, data.node, data.model));
    }

    private static void Execute(SourceProductionContext context, InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        if (invocation.ArgumentList.Arguments.Count != 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidActorRegisterMethodParameters,
                invocation.ArgumentList.GetLocation()));
            return;
        }

        if (invocation.ArgumentList.Arguments[0] is not
            {
                Expression: LiteralExpressionSyntax
                {
                    Token:
                    {
                        RawKind: (int) SyntaxKind.StringLiteralToken,
                        Value: string actorName,
                    },
                },
            })
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidActorRegisterMethodParameters,
                invocation.ArgumentList.GetLocation()));
            return;
        }

        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax
                {
                    TypeArgumentList.Arguments: var typeArgs,
                },
            })
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidActorRegisterMethodTypeArgs,
                invocation.Expression.GetLocation()));
            return;
        }

        if (typeArgs.Count != 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidActorRegisterMethodTypeArgs,
                invocation.Expression.GetLocation()));
            return;
        }

        var symbol = semanticModel.GetSymbolInfo(typeArgs[0]).Symbol;

        if (symbol is not INamedTypeSymbol actorTypeSymbol)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidActorRegisterMethodTypeArgs,
                invocation.Expression.GetLocation()));
            return;
        }

        actorTypeSymbol = actorTypeSymbol.OriginalDefinition;
        if (actorTypeSymbol.Locations.Any(l => l.IsInMetadata))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.ActorTypeMustBeDefinedInSource, typeArgs[0].GetLocation()));
            return;
        }

        var actorTypeDefinition = actorTypeSymbol.DeclaringSyntaxReferences.First().GetSyntax();
        if (actorTypeDefinition is not ClassDeclarationSyntax
            {
                Modifiers: var modifiers,
            } || !modifiers.Any(SyntaxKind.PartialKeyword))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.ActorTypeMustBePartial, actorTypeDefinition.GetLocation()));
            return;
        }

        var source = new StringBuilder();
        source.AppendLine("using System;");
        source.AppendLine("using System.Fabric;");
        source.AppendLine("using System.Threading.Tasks;");
        source.AppendLine("using Microsoft.ApplicationInsights;");
        source.AppendLine("using Microsoft.ApplicationInsights.Extensibility;");
        source.AppendLine("using Microsoft.ApplicationInsights.DataContracts;");
        source.AppendLine("using Microsoft.DotNet.ServiceFabric.ServiceHost;");
        source.AppendLine("using Microsoft.DotNet.ServiceFabric.ServiceHost.Actors;");
        source.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        source.AppendLine("using Microsoft.ServiceFabric.Actors;");
        source.AppendLine("using Microsoft.ServiceFabric.Actors.Runtime;");
        var ns = actorTypeSymbol.ContainingNamespace;
        if (ns != null)
        {
            source.AppendLine($"namespace {ns.Name}");
            source.AppendLine("{");
        }
        GenerateIServiceHostActorImplementation(source, actorName, actorTypeSymbol);
        source.AppendLine("namespace GeneratedCode");
        source.AppendLine("{");
        GenerateDelegatedActorImplementation(source, actorName, actorTypeSymbol);
        source.AppendLine("}");
        if (ns != null)
        {
            source.AppendLine("}");
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(source.ToString());
        var output = syntaxTree.GetRoot().NormalizeWhitespace().ToFullString();

        context.AddSource($"{actorName}.g.cs", output);
    }

    private static void GenerateDelegatedActorImplementation(StringBuilder source, string actorName, INamedTypeSymbol actorTypeSymbol)
    {
        var interfacesToWrap = actorTypeSymbol.Interfaces
            .Where(iface => iface.Name == "IRemindable" || iface.AllInterfaces.Any(iface2 => iface2.Name == "IActor"))
            .ToList();
        var methodsToWrap = interfacesToWrap.SelectMany(iface => iface.GetMembers().OfType<IMethodSymbol>());

        source.AppendLine("[StatePersistence(StatePersistence.Persisted)]");
        source.Append($"public class {actorName} : DelegatedActor");
        foreach (var iface in interfacesToWrap)
        {
            source.Append(", ").AppendType(iface);
        }

        source.AppendLine().AppendLine("{");
        source.AppendLine("private IServiceScopeFactory _scopeFactory;");
        source.AppendLine()
            .AppendLine($"public {actorName}(ActorService actorService, ActorId actorId, IServiceScopeFactory scopeFactory)");
        source.AppendLine(": base(actorService, actorId)");
        source.AppendLine("{");
        source.AppendLine("_scopeFactory = scopeFactory;");
        source.AppendLine("}").AppendLine();

        foreach (var method in methodsToWrap)
        {
            source.Append("async ").AppendType(method.ReturnType)
                .Append(" ")
                .AppendType(method.ContainingType)
                .Append(".")
                .Append(method.Name)
                .Append("(");
            for (int i = 0; i < method.Parameters.Length; i++)
            {
                var p = method.Parameters[i];
                if (i != 0)
                {
                    source.Append(", ");
                }

                source.AppendType(p.Type).Append(" ").Append(p.Name);
            }

            source.AppendLine(")");
            source.AppendLine("{");
            source.AppendLine("using IServiceScope __scope = _scopeFactory.CreateScope();");
            source.AppendLine("var __client = __scope.ServiceProvider.GetRequiredService<TelemetryClient>();");
            source.AppendLine("var __context = __scope.ServiceProvider.GetRequiredService<ServiceContext>();");
            source.AppendLine($$"""
                string __url = $"{__context.ServiceName}/{Id}/{{method.ContainingType.Name}}/{{method.Name}}";
                """);
            source.AppendLine("using IOperationHolder<RequestTelemetry> __op = __client.StartOperation<RequestTelemetry>($\"RPC {__url}\");");
            source.AppendLine("try").AppendLine("{");
            source.AppendLine("__op.Telemetry.Url = new Uri(__url);")
                .AppendType(actorTypeSymbol).Append(" __actor = __scope.ServiceProvider.GetRequiredService<")
                .AppendType(actorTypeSymbol).AppendLine(">();")
                .AppendLine("__actor.Initialize(Id, StateManager, this);");
            if (method.ReturnType.ToDisplayString().Contains("Task<"))
            {
                source.Append("return ");
            }
            source.Append($"await __actor.{method.Name}(");
            for (int i = 0; i < method.Parameters.Length; i++)
            {
                var p = method.Parameters[i];
                if (i != 0)
                {
                    source.Append(", ");
                }

                source.Append(p.Name);
            }

            source.AppendLine(");");


            source.AppendLine("}");
            source.AppendLine("catch (Exception ex)").AppendLine("{")
                .AppendLine("__op.Telemetry.Success = false;")
                .AppendLine("__client.TrackException(ex);")
                .AppendLine("throw;");
            source.AppendLine("}");
            source.AppendLine("}");
        }

        source.AppendLine("}");
    }

    private static void GenerateIServiceHostActorImplementation(StringBuilder source, string actorName, INamedTypeSymbol actorTypeSymbol)
    {
        var generatedActorName = $"GeneratedCode.{actorName}";

        source.AppendLine($$"""
            partial class {{ actorTypeSymbol.Name}} : IActorImplementation
            {
                public static Task RegisterActorAsync(Action<IServiceCollection> configureServices)
                {
                    return ActorRuntime.RegisterActorAsync<{{ generatedActorName}}>((context, info) => new DelegatedActorService<{{ actorTypeSymbol.Name}}
                    >(context, info, configureServices, (actorService, actorId, scopeFactory) => new {{
                        generatedActorName}} (actorService, actorId, scopeFactory)));
                }
            }
            """ );
    }
}

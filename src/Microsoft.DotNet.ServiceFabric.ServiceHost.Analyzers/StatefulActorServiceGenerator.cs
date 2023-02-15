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

        if (invocation.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax literalExpression)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidActorRegisterMethodParameters,
                invocation.ArgumentList.Arguments[0].GetLocation()));
            return;
        }

        if (!literalExpression.Token.IsKind(SyntaxKind.StringLiteralToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidActorRegisterMethodParameters,
                literalExpression.GetLocation()));
            return;
        }

        string actorName = (string)literalExpression.Token.Value;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidActorRegisterMethodTypeArgs,
                invocation.Expression.GetLocation()));
            return;
        }

        if (memberAccess.Name is not GenericNameSyntax genericName)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidActorRegisterMethodTypeArgs,
                memberAccess.Name.GetLocation()));
            return;
        }

        SeparatedSyntaxList<TypeSyntax> typeArguments = genericName.TypeArgumentList.Arguments;

        if (typeArguments.Count != 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidActorRegisterMethodTypeArgs,
                invocation.Expression.GetLocation()));
            return;
        }

        var symbol = semanticModel.GetSymbolInfo(typeArguments[0]).Symbol;

        if (symbol is not INamedTypeSymbol actorTypeSymbol)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidActorRegisterMethodTypeArgs,
                invocation.Expression.GetLocation()));
            return;
        }

        actorTypeSymbol = actorTypeSymbol.OriginalDefinition;
        if (actorTypeSymbol.Locations.Any(l => l.IsInMetadata))
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.ActorTypeMustBeDefinedInSource, typeArguments[0].GetLocation()));
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

        source.AppendLine("[global::Microsoft.ServiceFabric.Actors.Runtime.StatePersistence(global::Microsoft.ServiceFabric.Actors.Runtime.StatePersistence.Persisted)]");
        source.Append($"public class {actorName} : global::Microsoft.DotNet.ServiceFabric.ServiceHost.Actors.DelegatedActor");
        foreach (var iface in interfacesToWrap)
        {
            source.Append(", ").AppendType(iface);
        }

        source.AppendLine().AppendLine("{");
        source.AppendLine("private global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopeFactory;");
        source.AppendLine()
            .AppendLine($"public {actorName}(global::Microsoft.ServiceFabric.Actors.Runtime.ActorService actorService, global::Microsoft.ServiceFabric.Actors.ActorId actorId, global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory)");
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
            source.AppendLine("using global::Microsoft.Extensions.DependencyInjection.IServiceScope __scope = _scopeFactory.CreateScope();");
            source.AppendLine("var __client = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Microsoft.ApplicationInsights.TelemetryClient>(__scope.ServiceProvider);");
            source.AppendLine("var __context = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::System.Fabric.ServiceContext>(__scope.ServiceProvider);");
            source.AppendLine($$"""
                string __url = $"{__context.ServiceName}/{Id}/{{method.ContainingType.Name}}/{{method.Name}}";
                """);
            source.AppendLine("using var __op = global::Microsoft.ApplicationInsights.TelemetryClientExtensions.StartOperation<global::Microsoft.ApplicationInsights.DataContracts.RequestTelemetry>(__client, $\"RPC {__url}\");");
            source.AppendLine("try").AppendLine("{");
            source.AppendLine("__op.Telemetry.Url = new global::System.Uri(__url);")
                .AppendType(actorTypeSymbol).Append(" __actor = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<")
                .AppendType(actorTypeSymbol).AppendLine(">(__scope.ServiceProvider);")
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
            source.AppendLine("catch (global::System.Exception ex)").AppendLine("{")
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
        var ns = actorTypeSymbol.ContainingNamespace;
        if (ns != null)
        {
            generatedActorName = ns + "." + generatedActorName;
        }

        source.AppendLine($$"""
            partial class {{actorTypeSymbol.Name}} : global::Microsoft.DotNet.ServiceFabric.ServiceHost.Actors.IActorImplementation
            {
                public static global::System.Threading.Tasks.Task RegisterActorAsync(global::System.Action<global::Microsoft.Extensions.DependencyInjection.IServiceCollection> configureServices)
                {
                    return global::Microsoft.ServiceFabric.Actors.Runtime.ActorRuntime.RegisterActorAsync<global::{{generatedActorName}}>((context, info) => new global::Microsoft.DotNet.ServiceFabric.ServiceHost.Actors.DelegatedActorService<{{SymbolDisplay.ToDisplayString(actorTypeSymbol, SymbolDisplayFormat.FullyQualifiedFormat)}}
                    >(context, info, configureServices, (actorService, actorId, scopeFactory) => new global::{{generatedActorName}} (actorService, actorId, scopeFactory)));
                }
            }
            """ );
    }
}

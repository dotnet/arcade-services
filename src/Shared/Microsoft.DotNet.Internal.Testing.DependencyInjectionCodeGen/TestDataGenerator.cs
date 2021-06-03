using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen
{
    [Generator]
    public class TestDataGenerator : ISourceGenerator
    {
        public enum NameFormat
        {
            Property,
            Field,
            Parameter,
            Local,
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            if (!Debugger.IsAttached)
            {
                // Debugger.Launch();
            }

            context.RegisterForSyntaxNotifications(() => new TestDataReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var rec = context.SyntaxContextReceiver as TestDataReceiver;

            foreach (TestConfigDeclaration decl in rec.Declarations)
            {
                if (decl.Methods.Count == 0)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            "0001",
                            "TDG",
                            $"Class {decl.NamespaceName}.{decl.ParentName}.{decl.ConfigClassName} is declared with [TestConfigurationAttribute], but has no viable methods",
                            DiagnosticSeverity.Warning,
                            DiagnosticSeverity.Warning,
                            true,
                            1,
                            location: decl.ClassDeclarationSyntax.GetLocation()
                        )
                    );
                    continue;
                }

                string testDataSource = BuildTestData(decl);
                context.AddSource(decl.ParentName + ".TestData.cs", testDataSource);
            }
        }

        private string BuildTestData(TestConfigDeclaration decl)
        {
            Dictionary<string, int> parameterNameCounts = null;

            bool IsParameterNameUnique(string name)
            {
                if (parameterNameCounts == null)
                {
                    parameterNameCounts =
                        new Dictionary<string, int>();
                    foreach (ConfigMethod method in decl.Methods)
                    {
                        foreach (ConfigParameters parameter in method.Parameters)
                        {
                            if (!parameterNameCounts.TryGetValue(parameter.Name, out int value))
                            {
                                value = 0;
                            }

                            parameterNameCounts[parameter.Name] = value + 1;
                        }
                    }
                }

                return parameterNameCounts[name] == 1;
            }

            string SafeParameterName(string name, ConfigMethod method)
            {
                if (IsParameterNameUnique(name))
                {
                    return name;
                }

                return method.GetItemName(NameFormat.Property) + FormatName(NameFormat.Property, name);
            }

            return $@"namespace {decl.NamespaceName}
{{
    public partial class {decl.ParentName}
    {{
        private class TestData : System.IDisposable, System.IAsyncDisposable
        {{
            private Microsoft.Extensions.DependencyInjection.ServiceProvider _serviceProvider;

            private TestData(
                Microsoft.Extensions.DependencyInjection.ServiceProvider __serviceProvider{TestDataParameters()})
            {{
                _serviceProvider = __serviceProvider;{TestDataAssignment()}
            }}

{TestDataProperties()}

            public void Dispose() => _serviceProvider.Dispose();
            public System.Threading.Tasks.ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

            public static Builder Default => new Builder();

            public class Builder
            {{{BuilderFields()}                

                public Builder () {{ }}
                private Builder(
                    {BuilderParameters()})
                {{{BuilderCtorAssignments()}
                }}

                private Builder With(
                    {WithParameters()})
                {{
                    return new Builder(
                        {CtorCall()}
                    );
                }}

{BuilderModificationMethods()}
                public TestData Build()
                {{
                    var collection = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
                    
{ConfigurationCallbacks()}
                    var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(collection);

                    return new TestData(
                        provider{TestDataArgumentsFromProvider()}
                    );
                }}
            }}
        }}
    }}
}}
";

            string TestDataProperties()
            {
                return BuildMethodList(
                    (b, m) => b.AppendFormat(
                        "\n            public {0} {1} {{ get; }}",
                        m.ReturnTypeSymbol,
                        m.GetItemName(NameFormat.Property)
                    )
                );
            }

            string TestDataParameters()
            {
                return BuildMethodList(
                    (b, m) => b.AppendFormat(
                        ",\n                {0} {1}",
                        m.ReturnTypeSymbol,
                        m.GetItemName(NameFormat.Parameter)
                    )
                );
            }

            string TestDataAssignment()
            {
                return BuildMethodList(
                    (b, m) => b.AppendFormat(
                        "\n                {0} = {1};",
                        m.GetItemName(NameFormat.Property),
                        m.GetItemName(NameFormat.Parameter)
                    )
                );
            }

            string BuilderFields()
            {
                return BuildParameterList(
                    "",
                    (b, t, n) => b.AppendFormat(
                        "\n                private readonly {0} {1};",
                        t,
                        FormatName(NameFormat.Field, n)
                    )
                );
            }

            string BuilderParameters()
            {
                return BuildParameterList(
                    ",\n                    ",
                    (b, t, n) => b.AppendFormat("{0} {1}", t, FormatName(NameFormat.Parameter, n))
                );
            }

            string BuilderCtorAssignments()
            {
                return BuildParameterList(
                    "",
                    (b, t, n) => b.AppendFormat(
                        "\n                    {0} = {1};",
                        FormatName(NameFormat.Field, n),
                        FormatName(NameFormat.Parameter, n)
                    )
                );
            }

            string WithParameters()
            {
                return BuildParameterListWithNullable(
                    ",\n                    ",
                    (builder, type, name, nullable) => builder.AppendFormat(
                        "{0}{1} {2} = default",
                        type,
                        nullable ? "" : "?",
                        FormatName(NameFormat.Parameter, name)
                    )
                );
            }

            string CtorCall()
            {
                return BuildParameterList(
                    ",\n                        ",
                    (builder, _, name) => builder.AppendFormat(
                        "{0}:{1} ?? {2}",
                        FormatName(NameFormat.Parameter, name),
                        FormatName(NameFormat.Parameter, name),
                        FormatName(NameFormat.Field, name)
                    )
                );
            }

            string BuilderModificationMethods()
            {
                void WithParameterList(StringBuilder b, ConfigMethod m)
                {
                    b.Append("                public Builder With");
                    b.Append(m.Name);
                    b.Append("(");
                    for (int i = 0; i < m.Parameters.Count; i++)
                    {
                        ConfigParameters p = m.Parameters[i];
                        if (i != 0)
                        {
                            b.Append(", ");
                        }

                        b.Append(p.Type);
                        b.Append(" ");
                        string formatName = FormatName(NameFormat.Parameter, p.Name);
                        b.Append(formatName);
                    }

                    b.Append(") => With(");
                    for (int i = 0; i < m.Parameters.Count; i++)
                    {
                        ConfigParameters p = m.Parameters[i];
                        if (i != 0)
                        {
                            b.Append(", ");
                        }

                        string pName = FormatName(NameFormat.Parameter, p.Name);
                        b.Append(pName);
                        b.Append(": ");
                        b.Append(pName);
                    }

                    b.AppendLine(");");
                    b.AppendLine();
                }

                void IndividualParameters(StringBuilder b, ConfigMethod m)
                {
                    foreach (ConfigParameters p in m.Parameters)
                    {
                        string safeName = SafeParameterName(p.Name, m);
                        b.AppendFormat(
                            "                public Builder With{0}({1} {2}) => With({3}: {2});\n",
                            FormatName(NameFormat.Property, safeName),
                            p.Type,
                            FormatName(NameFormat.Parameter, p.Name),
                            FormatName(NameFormat.Parameter, safeName)
                        );
                    }
                }

                var builder = new StringBuilder();
                foreach (ConfigMethod method in decl.Methods)
                {
                    if (method.Parameters.Count == 0)
                    {
                        // These are default configurations
                        continue;
                    }

                    if (method.ConfigureAllParameters)
                    {
                        WithParameterList(builder, method);
                    }
                    else
                    {
                        IndividualParameters(builder, method);
                    }
                }

                return builder.ToString();
            }

            string ConfigurationCallbacks()
            {
                var builder = new StringBuilder();

                foreach (ConfigMethod method in decl.Methods)
                {
                    builder.Append("                    ");
                    if (!string.IsNullOrEmpty(method.ReturnTypeSymbol))
                    {
                        builder.Append("var ");
                        builder.Append(method.GetItemName(NameFormat.Local));
                        builder.Append(" = ");
                    }

                    builder.Append(decl.ConfigClassName);
                    builder.Append('.');
                    builder.Append(method.Name);
                    builder.Append("(collection");
                    foreach (ConfigParameters parameter in method.Parameters)
                    {
                        builder.Append(", ");
                        builder.Append(FormatName(NameFormat.Field, parameter.Name));
                    }

                    builder.AppendLine(");");
                    builder.AppendLine();
                }

                return builder.ToString();
            }

            string TestDataArgumentsFromProvider()
            {
                return BuildMethodList(
                    (b, m) => b.AppendFormat(
                        ",\n                        {0}(provider)",
                        m.GetItemName(NameFormat.Local)
                    )
                );
            }

            string BuildMethodList(Action<StringBuilder, ConfigMethod> format)
            {
                var builder = new StringBuilder();
                foreach (ConfigMethod method in decl.Methods)
                {
                    if (method.ReturnTypeSymbol == null)
                    {
                        continue;
                    }

                    format(builder, method);
                }

                return builder.ToString();
            }

            string BuildParameterListWithNullable(
                string between,
                Action<StringBuilder, string, string, bool> formatField)
            {
                var builder = new StringBuilder();
                foreach (ConfigMethod method in decl.Methods)
                {
                    foreach (ConfigParameters parameter in method.Parameters)
                    {
                        if (builder.Length != 0)
                        {
                            builder.Append(between);
                        }

                        formatField(
                            builder,
                            parameter.Type,
                            SafeParameterName(parameter.Name, method),
                            parameter.IsNullable
                        );
                    }
                }

                return builder.ToString();
            }

            string BuildParameterList(string between, Action<StringBuilder, string, string> formatField)
            {
                return BuildParameterListWithNullable(between, (b, t, m, n) => formatField(b, t, m));
            }
        }

        public static string FormatName(NameFormat format, string name)
        {
            return format switch
            {
                NameFormat.Property => char.ToUpperInvariant(name[0]) + name.Substring(1),
                NameFormat.Field => "_" + char.ToLowerInvariant(name[0]) + name.Substring(1),
                NameFormat.Parameter => char.ToLowerInvariant(name[0]) + name.Substring(1),
                NameFormat.Local => char.ToLowerInvariant(name[0]) + name.Substring(1),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
            };
        }

        private static string FullName(INamespaceSymbol ns)
        {
            if (ns.ContainingNamespace == null)
            {
                return ns.Name;
            }

            string parent = FullName(ns.ContainingNamespace);
            if (string.IsNullOrEmpty(parent))
            {
                return ns.Name;
            }

            return parent + '.' + ns.Name;
        }

        private static string FullName(ITypeSymbol type)
        {
            if (type is ITypeParameterSymbol typeParam)
            {
                return type.Name;
            }

            string name = type.Name;

            if (type is INamedTypeSymbol {IsGenericType: true, IsUnboundGenericType: false} named)
            {
                name += "<" + string.Join(",", named.TypeArguments.Select(FullName)) + ">";
            }

            if (type.ContainingType != null)
            {
                return FullName(type.ContainingType) + '.' + name;
            }

            string parent = FullName(type.ContainingNamespace);
            if (string.IsNullOrEmpty(parent))
            {
                return name;
            }

            return parent + '.' + name;
        }

        public class TestConfigDeclaration
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

        public class ConfigMethod
        {
            public ConfigMethod(
                string name,
                List<ConfigParameters> parameters,
                string returnTypeSymbol,
                bool configureAllParameters)
            {
                Name = name;
                Parameters = parameters;
                ReturnTypeSymbol = returnTypeSymbol;
                ConfigureAllParameters = configureAllParameters;
            }

            public string Name { get; }
            public List<ConfigParameters> Parameters { get; }
            public string ReturnTypeSymbol { get; }
            public bool ConfigureAllParameters { get; }

            public string GetItemName(NameFormat format)
            {
                string name = Name;
                if (name.StartsWith("Get"))
                {
                    name = name.Substring(3);
                }

                return FormatName(format, name);
            }
        }

        public class ConfigParameters
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

        public class TestDataReceiver : ISyntaxContextReceiver
        {
            public List<TestConfigDeclaration> Declarations { get; } = new List<TestConfigDeclaration>();

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                if (context.Node is ClassDeclarationSyntax cls)
                {
                    if (cls.Parent is ClassDeclarationSyntax nested &&
                        nested.Modifiers.Any(m => m.Kind() == SyntaxKind.PartialKeyword))
                    {
                        if (nested.Parent is NamespaceDeclarationSyntax ns)
                        {
                            ISymbol parentType = ModelExtensions.GetDeclaredSymbol(context.SemanticModel, nested);
                            ISymbol currentType = ModelExtensions.GetDeclaredSymbol(context.SemanticModel, cls);
                            if (HasNamedAttribute(currentType, "Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions.TestDependencyInjectionSetupAttribute"))
                            {
                                Declarations.Add(
                                    new TestConfigDeclaration(
                                        FullName(parentType.ContainingNamespace),
                                        nested.Identifier.Text,
                                        currentType,
                                        cls
                                    )
                                );
                            }
                        }
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

                    if (FullName(sMethod.Parameters[0].Type) !=
                        "Microsoft.Extensions.DependencyInjection.IServiceCollection")
                    {
                        return;
                    }

                    string configType;
                    if (sMethod.ReturnsVoid)
                    {
                        configType = null;
                    }
                    else if (!(sMethod.ReturnType is INamedTypeSymbol returnType))
                    {
                        return;
                    }
                    else if (returnType is INamedTypeSymbol named &&
                        named.IsGenericType &&
                        !named.IsUnboundGenericType &&
                        FullName(named.ConstructedFrom) == "System.Func<T,TResult>" &&
                        named.TypeArguments.Length == 2)
                    {
                        configType = FullName(named.TypeArguments[1]);
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
                                        FullName(p.Type),
                                        IsNullable(p.Type)
                                    )
                                )
                                .ToList(),
                            configType,
                            HasNamedAttribute(sMethod, "Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions.ConfigureAllParametersAttribute")
                        )
                    );
                }
            }

            private static bool HasNamedAttribute(ISymbol currentType, string attributeName)
            {
                return currentType.GetAttributes().Any(a => FullName(a.AttributeClass) == attributeName);
            }

            private static bool IsNullable(ITypeSymbol type)
            {
                return type.IsReferenceType ||
                    type is INamedTypeSymbol namedPType &&
                    namedPType.IsGenericType &&
                    !namedPType.IsUnboundGenericType &&
                    FullName(namedPType.ConstructedFrom) == "System.Nullable";
            }
        }
    }
}

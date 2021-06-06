using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen
{
    static internal class TestDataClassWriter
    {
        public enum NameFormat
        {
            Property,
            Field,
            Parameter,
            Local,
        }

        public static string BuildTestData(TestConfigDeclaration decl)
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

            string parameterSection = "";
            if (decl.Methods.Any(m => m.Parameters.Count > 0))
            {
                parameterSection = $@"
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
";
            }

            return $@"#pragma warning disable

namespace {decl.NamespaceName}
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

{parameterSection}

                public async System.Threading.Tasks.Task<TestData> BuildAsync()
                {{
                    var collection = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
                    
{ConfigurationCallbacks()}
                    var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(collection);
{FetchCallbacks()}

                    return new TestData(
                        provider{TestDataArgumentsFromProvider()}
                    );
                }}

                public TestData Build() => BuildAsync().GetAwaiter().GetResult();
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
                    if (method.HasFetch)
                    {
                        builder.Append("var callback_");
                        builder.Append(method.GetItemName(NameFormat.Local));
                        builder.Append(" = ");
                    }

                    if (method.IsConfigurationAsync)
                    {
                        builder.Append("await ");
                    }

                    builder.Append(decl.ConfigClassName);
                    builder.Append('.');
                    builder.Append(method.Name);
                    builder.Append("(collection");
                    foreach (ConfigParameters parameter in method.Parameters)
                    {
                        builder.Append(", ");
                        builder.Append((string) FormatName(NameFormat.Field, parameter.Name));
                    }

                    builder.AppendLine(");");
                    builder.AppendLine();
                }

                return builder.ToString();
            }

            string FetchCallbacks()
            {
                var builder = new StringBuilder();

                foreach (ConfigMethod method in decl.Methods)
                {
                    if (!method.HasFetch)
                    {
                        continue;
                    }

                    builder.Append("                    ");
                    if (!string.IsNullOrEmpty(method.ReturnTypeSymbol))
                    {
                        builder.Append("var value_");
                        builder.Append(method.GetItemName(NameFormat.Local));
                        builder.Append(" = ");
                    }

                    if (method.IsFetchAsync)
                    {
                        builder.Append("await ");
                    }
                    
                    builder.Append("callback_");
                    builder.Append(method.GetItemName(NameFormat.Local));
                    builder.Append("(provider);");
                    builder.AppendLine();
                }

                return builder.ToString();
            }

            string TestDataArgumentsFromProvider()
            {
                return BuildMethodList(
                    (b, m) => b.AppendFormat(
                        ",\n                        value_{0}",
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
    }
}
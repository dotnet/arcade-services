using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen
{
    static internal class SymbolNameHelper
    {
        public static string FullName(INamespaceSymbol ns)
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

        public static string FullName(ITypeSymbol type)
        {
            if (type is ITypeParameterSymbol typeParam)
            {
                return type.Name;
            }

            if (type is INamedTypeSymbol {IsTupleType: true} tuple && tuple.TupleElements.All(t => !t.IsImplicitlyDeclared))
            {
                return "(" + string.Join(", ", tuple.TupleElements.Select(t => FullName(t.Type) + " " + t.Name)) + ")";
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
    }
}
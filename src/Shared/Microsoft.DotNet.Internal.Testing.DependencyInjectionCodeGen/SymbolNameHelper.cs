using System.Linq;
using Microsoft.CodeAnalysis;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen;

internal static class SymbolNameHelper
{
    public static string FullName(this INamespaceSymbol ns)
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

    public static string FullName(this ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol)
        {
            return type.Name;
        }

        if (type is INamedTypeSymbol {IsTupleType: true} tuple && tuple.TupleElements.All(t => !t.IsImplicitlyDeclared))
        {
            return "(" + string.Join(", ", tuple.TupleElements.Select(t => FullName(t.Type) + " " + t.Name)) + ")";
        }
            
        if (type is IArrayTypeSymbol array)
        {
            return FullName(array.ElementType) + "[" + new string(',', array.Rank - 1) + "]";
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

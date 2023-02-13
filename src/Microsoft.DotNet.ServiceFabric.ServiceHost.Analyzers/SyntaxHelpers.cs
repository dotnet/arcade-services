using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Analyzers;

public static class SyntaxHelpers
{
    public static StringBuilder AppendType(this StringBuilder sb, ITypeSymbol type)
    {
        return sb.Append(SymbolDisplay.ToDisplayString(type, SymbolDisplayFormat.FullyQualifiedFormat));
    }
}
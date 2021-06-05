using System.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Microsoft.DotNet.Internal.Testing.DependencyInjectionCodeGen
{
    [Generator]
    public class TestDataGenerator : ISourceGenerator
    {
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
            var rec = (TestDataReceiver) context.SyntaxContextReceiver;
            
            foreach (Diagnostic diagnostic in rec.Diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }

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

                string testDataSource = TestDataClassWriter.BuildTestData(decl);
                context.AddSource(decl.ParentName + ".TestData.cs", testDataSource);
            }
        }
    }
}

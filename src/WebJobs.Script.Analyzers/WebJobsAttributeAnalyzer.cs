using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Microsoft.Azure.Functions.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class WebJobsAttributeAnalyzer : DiagnosticAnalyzer
    {
        // TODO: Scope this to per-project
        JobHostMetadataProvider _tooling;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(DiagnosticDescriptors.IllegalFunctionName); } }

    public static void VerifyWebJobsLoaded() 
        {
            var x = new JobHost(new OptionsWrapper<JobHostOptions>(new JobHostOptions()), null);
        }

        public override void Initialize(AnalysisContext context)
        {
            // https://stackoverflow.com/questions/62638455/analyzer-with-code-fix-project-template-is-broken
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            VerifyWebJobsLoaded();

            // Analyze method signatures.
            context.RegisterSyntaxNodeAction(AnalyzeMethodDeclarationNode, SyntaxKind.MethodDeclaration);

            // Hook compilation to get the assemblies' references and build the WebJob tooling interfaces.
            context.RegisterCompilationStartAction(AnalyzeCompilation);
        }

        private void AnalyzeCompilation(CompilationStartAnalysisContext context)
        {
            var compilation = context.Compilation;

            AssemblyCache.Instance.Build(compilation);
            _tooling = AssemblyCache.Instance.Tooling;

            // cast to PortableExecutableReference which has a file path
            var x1 = compilation.References.OfType<PortableExecutableReference>().ToArray();
            var webJobsPath = (from reference in x1
                               where IsWebJobsSdk(reference)
                               select reference.FilePath).SingleOrDefault();

            if (webJobsPath == null)
            {
                return; // Not a WebJobs project.
            }
        }

        private bool IsWebJobsSdk(PortableExecutableReference reference)
        {
            if (reference.FilePath.EndsWith("Microsoft.Azure.WebJobs.dll"))
            {
                return true;
            }
            return false;
        }

        // This is called extremely frequently
        // Analyze the method signature to validate binding attributes + types on the parameters
        private void AnalyzeMethodDeclarationNode(SyntaxNodeAnalysisContext context)
        {
            if (_tooling == null) // Not yet initialized
            {
                return;
            }

            var methodDecl = (MethodDeclarationSyntax)context.Node;
            var methodName = methodDecl.Identifier.ValueText;

            if (!HasFunctionNameAttribute(context, methodDecl))
            {
                return;
            }

            // TODO: Attribute validation
        }

        // First argument to the FunctionName ctor.
        private string GetFunctionNameFromAttribute(SemanticModel semantics, AttributeSyntax attributeSyntax)
        {
            foreach (var arg in attributeSyntax.ArgumentList.Arguments)
            {
                var val = semantics.GetConstantValue(arg.Expression);
                if (!val.HasValue)
                {
                return null;
                }
                return val.Value as string;
            }
            return null;
        }

        // Does the method have a [FunctionName] attribute?
        // This provides a quick check before we get into the more intensive analysis work.
        private bool HasFunctionNameAttribute(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax methodDeclarationSyntax)
        {
            foreach (var attrListSyntax in methodDeclarationSyntax.AttributeLists)
            {
                foreach (AttributeSyntax attributeSyntax in attrListSyntax.Attributes)
                {
                    // Perf - Can we get the name without doing a symbol resolution?
                    var symAttributeCtor = context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol;
                    if (symAttributeCtor != null)
                    {
                        var attrType = symAttributeCtor.ContainingType;
                        if (attrType.Name == nameof(FunctionNameAttribute))
                        {
                            // Validate the FunctionName
                            var functionName = GetFunctionNameFromAttribute(context.SemanticModel, attributeSyntax);

                            bool match = FunctionNameAttribute.FunctionNameValidationRegex.IsMatch(functionName);
                            if (!match)
                            {
                                var error = Diagnostic.Create(DiagnosticDescriptors.IllegalFunctionName, attributeSyntax.GetLocation(), functionName);
                                context.ReportDiagnostic(error);
                            }

                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace Microsoft.Azure.Functions.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class WebJobsAttributeAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(DiagnosticDescriptors.IllegalFunctionName); } }

        public override void Initialize(AnalysisContext context)
        {
            // https://stackoverflow.com/questions/62638455/analyzer-with-code-fix-project-template-is-broken
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Analyze method signatures.
            context.RegisterSyntaxNodeAction(AnalyzeMethodDeclarationNode, SyntaxKind.MethodDeclaration);
        }

        // This is called extremely frequently
        // Analyze the method signature to validate binding attributes + types on the parameters
        private void AnalyzeMethodDeclarationNode(SyntaxNodeAnalysisContext context)
        {
            var methodDecl = (MethodDeclarationSyntax)context.Node;

            CheckForFunctionNameAttributeAndReport(context, methodDecl);
        }

        // First argument to the FunctionName ctor.
        private string GetFunctionNameFromAttribute(SemanticModel semantics, AttributeSyntax attributeSyntax)
        {
            if (attributeSyntax.ArgumentList.Arguments.Count == 0)
            {
                return null;
            }

            var firstArg = attributeSyntax.ArgumentList.Arguments[0];
            var val = semantics.GetConstantValue(firstArg.Expression);

            return val.Value as string;
        }

        // Quick check for [FunctionName] attribute on a method.
        // Reports a diagnostic if the name doesn't meet requirements.
        private void CheckForFunctionNameAttributeAndReport(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax methodDeclarationSyntax)
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
                        if (attrType.Name != nameof(FunctionNameAttribute))
                        {
                            return;
                        }

                        // Validate the FunctionName
                        var functionName = GetFunctionNameFromAttribute(context.SemanticModel, attributeSyntax);

                        bool match = !string.IsNullOrEmpty(functionName) && FunctionNameAttribute.FunctionNameValidationRegex.IsMatch(functionName);
                        if (!match)
                        {
                            var error = Diagnostic.Create(DiagnosticDescriptors.IllegalFunctionName, attributeSyntax.GetLocation(), functionName);
                            context.ReportDiagnostic(error);
                        }

                        return;
                    }
                }
            }
        }
    }
}

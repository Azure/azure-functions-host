// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Net.Http;

namespace Microsoft.Azure.Functions.Analyzers
{
    /// <summary>
    /// AZF0002: Use static HttpClient
    /// 
    /// Cause:
    /// An local declaration that happens inside a Function method, instantiates an HttpClient
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class AvoidNonStaticHttpClientAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(DiagnosticDescriptors.AvoidNonStaticHttpClient); } }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.ObjectCreationExpression);
        }

        private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            // check if object is created within function method
            var containingSymbol = context.ContainingSymbol as IMethodSymbol;
            if (containingSymbol is null || !containingSymbol.IsFunction(context.Compilation))
            {
                return;
            }

            // check if constructor is HttpClient
            var httpClientTypeSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(HttpClient).FullName);
            var nodeTypeSymbol = context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken).ConvertedType;

            if (!httpClientTypeSymbol.IsAssignableFrom(nodeTypeSymbol))
            {
                return;
            }

            var diagnostic = Diagnostic.Create(DiagnosticDescriptors.AvoidNonStaticHttpClient, context.Node.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }
}
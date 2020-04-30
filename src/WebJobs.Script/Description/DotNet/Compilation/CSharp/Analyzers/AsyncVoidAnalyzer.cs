// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Description.DotNet.CSharp.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AsyncVoidAnalyzer : DiagnosticAnalyzer
    {
        private const string Title = "Avoid async void";
        private const string MessageFormat = "This method has the async keyword but it returns void";
        private readonly DiagnosticDescriptor _supportedRule;

        public AsyncVoidAnalyzer()
        {
            _supportedRule = new DiagnosticDescriptor(DotNetConstants.AsyncVoidCode,
                Title, MessageFormat, "Function", DiagnosticSeverity.Warning, true);
        }

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(_supportedRule);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
            context.RegisterSymbolAction(AnalyzeSymbol, SymbolKind.Method);
        }

        private void AnalyzeSymbol(SymbolAnalysisContext context)
        {
            var methodSymbol = (IMethodSymbol)context.Symbol;

            if (methodSymbol.ReturnsVoid && methodSymbol.IsAsync)
            {
                context.ReportDiagnostic(Diagnostic.Create(_supportedRule, methodSymbol.Locations[0], methodSymbol.Name));
            }
        }
    }
}

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
    public sealed class InvalidFileMetadataReferenceAnalyzer : DiagnosticAnalyzer
    {
        private const string Title = "Invalid file metadata reference";
        private const string MessageFormat = "The reference '{0}' is invalid. If you are attempting to add a framework reference, please remove the '{1}' file extension.";
        private readonly DiagnosticDescriptor _supportedRule;

        public InvalidFileMetadataReferenceAnalyzer()
        {
            _supportedRule = new DiagnosticDescriptor(DotNetConstants.InvalidFileMetadataReferenceCode,
                Title, MessageFormat, "Function", DiagnosticSeverity.Warning, true);
        }

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get
            {
                return ImmutableArray.Create(_supportedRule);
            }
        }

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
            context.RegisterCompilationAction(AnalyzeCompilation);
        }

        private void AnalyzeCompilation(CompilationAnalysisContext context)
        {
            // Find CS0006: Metadata file '{0}' could not be found
            Diagnostic invalidMetadataDiagnostic = context.Compilation
                .GetDiagnostics().FirstOrDefault(d => string.Compare(d.Id, "CS0006") == 0);

            if (invalidMetadataDiagnostic != null)
            {
                var argument = invalidMetadataDiagnostic.GetDiagnosticMessageArguments().First().ToString();
                if (argument != null && string.Compare(Path.GetExtension(argument), ".dll") == 0)
                {
                    var diagnostic = Diagnostic.Create(_supportedRule, invalidMetadataDiagnostic.Location,
                        argument, ".dll");

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }
}

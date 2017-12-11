// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Description.DotNet.CSharp.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class InvalidPrivateMetadataReferenceAnalyzer : DiagnosticAnalyzer
    {
        private const string Title = "Invalid private metadata reference";
        private const string MessageFormat = "The reference '{0}' is invalid. Private assembly references must include the file extension. Try using '{1}'.";
        private readonly DiagnosticDescriptor _supportedDiagnostic;
        private static readonly ImmutableArray<string> _validMetadataExtensions = ImmutableArray.Create(".dll", ".exe");

        public InvalidPrivateMetadataReferenceAnalyzer()
        {
            _supportedDiagnostic = new DiagnosticDescriptor(DotNetConstants.InvalidPrivateMetadataReferenceCode,
                Title, MessageFormat, "Function", DiagnosticSeverity.Warning, true);
        }

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(_supportedDiagnostic);

        public override void Initialize(AnalysisContext context)
        {
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
                var resolver = context.Compilation.Options.MetadataReferenceResolver as IFunctionMetadataResolver;
                if (argument != null && !_validMetadataExtensions.Contains(Path.GetExtension(argument)) &&
                    resolver != null && resolver.TryResolvePrivateAssembly(argument, out string path))
                {
                    var diagnostic = Diagnostic.Create(_supportedDiagnostic,
                        invalidMetadataDiagnostic.Location,
                        argument,
                        Path.GetFileName(path));

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }
}

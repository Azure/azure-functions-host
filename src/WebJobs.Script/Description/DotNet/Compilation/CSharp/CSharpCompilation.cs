// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description.DotNet.CSharp.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public sealed class CSharpCompilation : IDotNetCompilation
    {
        private readonly Compilation _compilation;

        // Simply getting the built in analyzers for now.
        // This should eventually be enhanced to dynamically discover/load analyzers.
        private static ImmutableArray<DiagnosticAnalyzer> _analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new InvalidFileMetadataReferenceAnalyzer(), new AsyncVoidAnalyzer());

        public CSharpCompilation(Compilation compilation)
        {
            _compilation = compilation;
        }

        public ImmutableArray<Diagnostic> GetDiagnostics()
        {
            return _compilation.WithAnalyzers(GetAnalyzers()).GetAllDiagnosticsAsync().Result;
        }

        public FunctionSignature GetEntryPointSignature(IFunctionEntryPointResolver entryPointResolver, Assembly functionAssembly)
        {
            if (!_compilation.SyntaxTrees.Any())
            {
                throw new InvalidOperationException("The current compilation does not have a syntax tree.");
            }

            var methods = _compilation.ScriptClass
                .GetMembers()
                .OfType<IMethodSymbol>()
                .Select(m => new MethodReference<IMethodSymbol>(m.Name, m.DeclaredAccessibility == Accessibility.Public, m));

            IMethodSymbol entryPointReference = entryPointResolver.GetFunctionEntryPoint(methods).Value;
            bool hasLocalTypeReferences = HasLocalTypeReferences(entryPointReference);
            var functionParameters = entryPointReference.Parameters.Select(p => new FunctionParameter(p.Name, GetFullTypeName(p.Type), p.IsOptional, p.RefKind));

            return new FunctionSignature(entryPointReference.ContainingType.ToDisplayString(), entryPointReference.Name,
                ImmutableArray.CreateRange(functionParameters.ToArray()), GetFullTypeName(entryPointReference.ReturnType), hasLocalTypeReferences);
        }

        private static bool HasLocalTypeReferences(IMethodSymbol entryPointReference)
        {
            return IsOrUsesAssemblyType(entryPointReference.ReturnType, entryPointReference.ContainingAssembly)
                || entryPointReference.Parameters.Any(p => IsOrUsesAssemblyType(p.Type, entryPointReference.ContainingAssembly));
        }

        private static string GetFullTypeName(ITypeSymbol type)
        {
            if (type == null)
            {
                return string.Empty;
            }

            return type.ContainingAssembly == null
                ? type.ToDisplayString()
                : string.Format(CultureInfo.InvariantCulture, "{0}, {1}", type.ToDisplayString(), type.ContainingAssembly.ToDisplayString());
        }

        private static bool IsOrUsesAssemblyType(ITypeSymbol typeSymbol, IAssemblySymbol assemblySymbol)
        {
            if (typeSymbol.ContainingAssembly == assemblySymbol)
            {
                return true;
            }

            INamedTypeSymbol namedTypeSymbol = typeSymbol as INamedTypeSymbol;
            return namedTypeSymbol != null && namedTypeSymbol.IsGenericType
                && namedTypeSymbol.TypeArguments.Any(t => IsOrUsesAssemblyType(t, assemblySymbol));
        }

        async Task<object> ICompilation.EmitAsync(CancellationToken cancellationToken) => await EmitAsync(cancellationToken);

        public async Task<DotNetCompilationResult> EmitAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (var assemblyStream = new MemoryStream())
                using (var pdbStream = new MemoryStream())
                {
                    var compilationWithAnalyzers = _compilation.WithAnalyzers(GetAnalyzers());
                    var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
                    var emitOptions = new EmitOptions().WithDebugInformationFormat(PlatformHelper.IsWindows ? DebugInformationFormat.Pdb : DebugInformationFormat.PortablePdb);
                    var emitResult = compilationWithAnalyzers.Compilation.Emit(assemblyStream, pdbStream, options: emitOptions, cancellationToken: cancellationToken);

                    diagnostics = diagnostics.AddRange(emitResult.Diagnostics);

                    if (diagnostics.Any(di => di.Severity == DiagnosticSeverity.Error))
                    {
                        throw new CompilationErrorException("Script compilation failed.", diagnostics);
                    }

                    // Check if cancellation was requested while we were compiling,
                    // and if so quit here.
                    cancellationToken.ThrowIfCancellationRequested();

                    return DotNetCompilationResult.FromBytes(assemblyStream.GetBuffer(), pdbStream.GetBuffer());
                }
            }
            catch (Exception exc) when (!(exc is CompilationErrorException))
            {
                throw new CompilationServiceException($"C# compilation service error: {exc.Message}", exc);
            }
        }

        private static ImmutableArray<DiagnosticAnalyzer> GetAnalyzers()
        {
            return _analyzers;
        }
    }
}

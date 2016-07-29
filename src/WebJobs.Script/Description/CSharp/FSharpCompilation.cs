﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.FSharp.Compiler;
using Microsoft.FSharp.Compiler.SimpleSourceCodeServices;
using Microsoft.FSharp.Compiler.SourceCodeServices;
using Microsoft.FSharp.Core;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class FSharpCompilation : ICompilation
    {
        private FSharpErrorInfo[] _errors;
        private FSharpOption<Assembly> _assemblyOption;

        public FSharpCompilation(FSharpErrorInfo[] errors, FSharpOption<Assembly> assemblyOption)
        {
            _errors = errors;
            _assemblyOption = assemblyOption;
        }

        public ImmutableArray<Diagnostic> GetDiagnostics()
        {
            var result = ImmutableArray.CreateBuilder<Diagnostic>();
            foreach (var error in _errors)
            {
                var severity = error.Severity == FSharpErrorSeverity.Error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;
                var dd = new DiagnosticDescriptor("FS" + error.ErrorNumber.ToString(), error.Message, error.Message, error.Subcategory, severity, true);
                var loc = Location.Create(error.FileName, TextSpan.FromBounds(error.StartColumn, error.EndColumn), new LinePositionSpan(new LinePosition(error.StartLineAlternate, error.StartColumn), new LinePosition(error.EndLineAlternate, error.EndColumn)));
                var diag = Diagnostic.Create(dd, loc);
                result.Add(diag);
            }
            return result.ToImmutable();
        }

        public Assembly Emit()
        {
            if (_assemblyOption == null)
            {
                throw new CompilationErrorException("Script compilation failed.", this.GetDiagnostics());
            }
            return _assemblyOption.Value;
        }

        public DotNetFunctionSignature FindEntryPoint(IFunctionEntryPointResolver entryPointResolver)
        {
            if (_assemblyOption == null)
            {
                throw new CompilationErrorException("Script compilation failed.", this.GetDiagnostics());
            }

            // Scrape the compiled assembly for entry points
            IList<MethodReference<MethodInfo>> methods =
                            _assemblyOption.Value.GetTypes().SelectMany(t =>
                                t.GetMethods().Select(m => 
                                    new MethodReference<MethodInfo>(m.Name, m.IsPublic, m))).ToList();

            MethodInfo entryPointReference = entryPointResolver.GetFunctionEntryPoint(methods).Value;

            // For F#, this currently creates a malformed signautre with fewer parameter symbols than parameter names.
            // For validation we only need the parameter names. The implementation of DotNetFunctionSignature copes with the 
            // lists having different lengths.
            var signature = new DotNetFunctionSignature(entryPointReference.GetParameters().Select(x => x.Name).ToImmutableArray(), ImmutableArray<IParameterSymbol>.Empty);

            // For F#, we always set this to true for now.
            signature.HasLocalTypeReference = true;

            return signature;
        }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.Description.Node.TypeScript
{
    public class TypeScriptCompilation : IJavaScriptCompilation
    {
        private static readonly Lazy<ITypeScriptCompiler> _defaultCompiler = new Lazy<ITypeScriptCompiler>(() => new TypeScriptCompiler(), LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly string _inputFilePath;
        private readonly TypeScriptCompilationOptions _options;
        private readonly List<Diagnostic> _diagnostics = new List<Diagnostic>();
        private readonly ITypeScriptCompiler _compiler;

        private TypeScriptCompilation(string inputFilePath, TypeScriptCompilationOptions options, ITypeScriptCompiler compiler)
        {
            _compiler = compiler;
            _inputFilePath = inputFilePath;
            _options = options;
        }

        public bool SupportsDiagnostics => true;

        private async Task CompileAsync()
        {
          var diagnostics = await _compiler.CompileAsync(_inputFilePath, _options);
        }

        public static async Task<TypeScriptCompilation> CompileAsync(string inputFile, TypeScriptCompilationOptions options, ITypeScriptCompiler compiler = null)
        {
            var compilation = new TypeScriptCompilation(inputFile, options, compiler ?? _defaultCompiler.Value);
            await compilation.CompileAsync();

            return compilation;
        }

        public ImmutableArray<Diagnostic> GetDiagnostics() => ImmutableArray.Create(_diagnostics.ToArray());

        async Task<object> ICompilation.EmitAsync(CancellationToken cancellationToken) => await EmitAsync(cancellationToken);

        public Task<string> EmitAsync(CancellationToken cancellationToken)
        {
            string relativeInputFilePath = FileUtility.GetRelativePath(_options.RootDir, _inputFilePath);
            string outputFileName = Path.ChangeExtension(relativeInputFilePath, ".js");

            string scriptPath = Path.Combine(Path.GetDirectoryName(_inputFilePath), _options.OutDir, outputFileName);

            return Task.FromResult(scriptPath);
        }
    }
}

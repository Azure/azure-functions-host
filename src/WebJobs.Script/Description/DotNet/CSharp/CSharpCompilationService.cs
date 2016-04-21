// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    [CLSCompliant(false)]
    public class CSharpCompilationService : ICompilationService
    {
        private readonly IFunctionMetadataResolver _metadataResolver;

        private static readonly Lazy<InteractiveAssemblyLoader> AssemblyLoader
          = new Lazy<InteractiveAssemblyLoader>(() => new InteractiveAssemblyLoader(), LazyThreadSafetyMode.ExecutionAndPublication);

        public CSharpCompilationService(IFunctionMetadataResolver metadataResolver)
        {
            _metadataResolver = metadataResolver;
        }

        public IEnumerable<string> SupportedFileTypes
        {
            get
            {
                return new[] { ".csx", ".cs" };
            }
        }

        public ICompilation GetFunctionCompilation(FunctionMetadata functionMetadata)
        {
            string code = GetFunctionSource(functionMetadata);
            Script<object> script = CSharpScript.Create(code, options: _metadataResolver.FunctionScriptOptions, assemblyLoader: AssemblyLoader.Value);

            Compilation compilation = GetScriptCompilation(script, true, functionMetadata);

            return new CSharpCompilation(compilation);
        }

        private static string GetFunctionSource(FunctionMetadata functionMetadata)
        {
            string code = null;

            if (File.Exists(functionMetadata.Source))
            {
                code = File.ReadAllText(functionMetadata.Source);
            }

            return code ?? string.Empty;
        }

        private static Compilation GetScriptCompilation(Script<object> script, bool debug, FunctionMetadata functionMetadata)
        {
            Compilation compilation = script.GetCompilation();

            OptimizationLevel compilationOptimizationLevel = OptimizationLevel.Release;
            if (debug)
            {
                SyntaxTree scriptTree = compilation.SyntaxTrees.FirstOrDefault(t => string.IsNullOrEmpty(t.FilePath));
                var debugTree = SyntaxFactory.SyntaxTree(scriptTree.GetRoot(),
                  encoding: Encoding.UTF8,
                  path: Path.GetFileName(functionMetadata.Source),
                  options: new CSharpParseOptions(kind: SourceCodeKind.Script));

                compilationOptimizationLevel = OptimizationLevel.Debug;

                compilation = compilation
                    .RemoveAllSyntaxTrees()
                    .AddSyntaxTrees(debugTree);
            }

            return compilation.WithOptions(compilation.Options.WithOptimizationLevel(compilationOptimizationLevel))
                .WithAssemblyName(FunctionAssemblyLoader.GetAssemblyNameFromMetadata(functionMetadata, compilation.AssemblyName));
        }
    }
}

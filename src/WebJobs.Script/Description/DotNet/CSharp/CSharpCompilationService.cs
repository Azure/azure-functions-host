// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class CSharpCompilationService : ICompilationService
    {
        private static readonly string[] FileTypes = { ".csx", ".cs" };
        private static readonly Encoding UTF8WithNoBOM = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly Lazy<InteractiveAssemblyLoader> AssemblyLoader
          = new Lazy<InteractiveAssemblyLoader>(() => new InteractiveAssemblyLoader(), LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly OptimizationLevel _optimizationLevel;
        private readonly IFunctionMetadataResolver _metadataResolver;

        public CSharpCompilationService(IFunctionMetadataResolver metadataResolver, OptimizationLevel optimizationLevel)
        {
            _metadataResolver = metadataResolver;
            _optimizationLevel = optimizationLevel;
        }

        public string Language => "CSharp";

        public IEnumerable<string> SupportedFileTypes => FileTypes;

        public ICompilation GetFunctionCompilation(FunctionMetadata functionMetadata)
        {
            string code = GetFunctionSource(functionMetadata);
            Script<object> script = CSharpScript.Create(code, options: _metadataResolver.CreateScriptOptions(), assemblyLoader: AssemblyLoader.Value);

            Compilation compilation = GetScriptCompilation(script, functionMetadata);

            return new CSharpCompilation(compilation);
        }

        internal static string GetFunctionSource(FunctionMetadata functionMetadata)
        {
            string code = null;

            if (File.Exists(functionMetadata.ScriptFile))
            {
                // We use ReadAllBytes here to make sure we preserve the BOM, if present.
                var codeBytes = File.ReadAllBytes(functionMetadata.ScriptFile);
                code = Encoding.UTF8.GetString(codeBytes);
            }

            return code ?? string.Empty;
        }

        private Compilation GetScriptCompilation(Script<object> script, FunctionMetadata functionMetadata)
        {
            Compilation compilation = script.GetCompilation();

            if (_optimizationLevel == OptimizationLevel.Debug)
            {
                SyntaxTree scriptTree = compilation.SyntaxTrees.FirstOrDefault(t => string.IsNullOrEmpty(t.FilePath));
                var debugTree = SyntaxFactory.SyntaxTree(scriptTree.GetRoot(),
                  encoding: UTF8WithNoBOM,
                  path: Path.GetFileName(functionMetadata.ScriptFile),
                  options: new CSharpParseOptions(kind: SourceCodeKind.Script));

                compilation = compilation
                    .RemoveAllSyntaxTrees()
                    .AddSyntaxTrees(debugTree);
            }

            return compilation.WithOptions(compilation.Options.WithOptimizationLevel(_optimizationLevel))
                .WithAssemblyName(FunctionAssemblyLoader.GetAssemblyNameFromMetadata(functionMetadata, compilation.AssemblyName));
        }
    }
}

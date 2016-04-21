// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class CSharpCompiler : IDotNetCompiler
    {
        private static readonly string[] TheWatchedFileTypes = { ".cs", ".csx", ".dll", ".exe" };

        public string[] WatchedFileTypes
        {
            get
            {
                return TheWatchedFileTypes;
            }
        }

        public ScriptType ScriptType
        {
            get
            {
                return ScriptType.CSharp;
            }
        }

        public ICompilation GetCompilation(string code, ScriptOptions options, InteractiveAssemblyLoader assemblyLoader, string functionName, string filename, bool debug)
        {
            Script<object> script = Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript.Create(code, options: options, assemblyLoader: assemblyLoader);
            Compilation compilation = script.GetCompilation();
            OptimizationLevel compilationOptimizationLevel = OptimizationLevel.Release;
            if (debug)
            {
                SyntaxTree scriptTree = compilation.SyntaxTrees.FirstOrDefault(t => string.IsNullOrEmpty(t.FilePath));
                var debugTree = SyntaxFactory.SyntaxTree(scriptTree.GetRoot(),
                  encoding: Encoding.UTF8,
                  path: filename,
                  options: new CSharpParseOptions(kind: SourceCodeKind.Script));

                compilationOptimizationLevel = OptimizationLevel.Debug;

                compilation = compilation
                    .RemoveAllSyntaxTrees()
                    .AddSyntaxTrees(debugTree);
            }
            compilation =
                compilation
                  .WithOptions(compilation.Options.WithOptimizationLevel(compilationOptimizationLevel))
                  .WithAssemblyName(FunctionAssemblyLoader.GetAssemblyNameFromMetadata(functionName, compilation.AssemblyName));

            if (!compilation.SyntaxTrees.Any())
            {
                throw new InvalidOperationException("The provided compilation does not have a syntax tree.");
            }

            return new CSharpCompilation(compilation);
        }
    }
}

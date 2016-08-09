// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
    internal class FSharpCompiler : ICompilationService
    {
        private static readonly string[] TheWatchedFileTypes = { ".fs", ".fsx", ".dll", ".exe", ".fsi" };
        private readonly IFunctionMetadataResolver _metadataResolver;
        private static readonly Lazy<InteractiveAssemblyLoader> AssemblyLoader
        = new Lazy<InteractiveAssemblyLoader>(() => new InteractiveAssemblyLoader(), LazyThreadSafetyMode.ExecutionAndPublication);

        public FSharpCompiler(IFunctionMetadataResolver metadataResolver)
        {
            _metadataResolver = metadataResolver;
        }

        public IEnumerable<string> SupportedFileTypes
        {
            get
            {
                return TheWatchedFileTypes;
            }
        }

        public ICompilation GetFunctionCompilation(FunctionMetadata functionMetadata)
        {
            string code = GetFunctionSource(functionMetadata);
            // TODO: Get debug flag from context. Set to true for now.
            bool debug = true;

            // First use the C# compiler to resolve references, to get consistenct with the C# Azure Functions programming model
            Script<object> script = Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript.Create("using System;", options: _metadataResolver.CreateScriptOptions(), assemblyLoader: AssemblyLoader.Value);
            Compilation compilation = script.GetCompilation();

            var compiler = new SimpleSourceCodeServices();

            // We currently create the script file in the directory itself
            var scriptFile = Path.Combine(Path.GetDirectoryName(functionMetadata.ScriptFile),Path.ChangeExtension(Path.GetTempFileName(), "fsx"));
            FSharpErrorInfo[] errors = null;
            //var exitCode = result.Item2;
            FSharpOption<Assembly> assemblyOption = null;
            try
            {
                File.AppendAllLines(scriptFile, new string[] { "open System.Runtime.InteropServices" });
                foreach (var import in script.Options.Imports)
                {
                    File.AppendAllLines(scriptFile, new string[] { "open " + import });
                    
                }
                File.AppendAllLines(scriptFile, new string[] { "# 0 @\"" + functionMetadata.ScriptFile + "\"" });
                File.AppendAllText(scriptFile, File.ReadAllText(functionMetadata.ScriptFile));
                var otherFlags = new List<string>();

                // For some reason CompileToDynamicAssembly wants "fsc.exe" as the first arg, it is ignored.
                otherFlags.Add("fsc.exe");

                otherFlags.Add("--noframework");

                foreach (var mdr in compilation.References)
                {
                    if (!mdr.Display.Contains("Unresolved "))
                    {
                        otherFlags.Add("-r:" + mdr.Display);
                    }
                }
                otherFlags.Add("-r:System.Linq.dll"); // System.Linq.Expressions.Expression<T> 
                otherFlags.Add("-r:System.Reflection.dll"); // System.Reflection.ParameterInfo
                otherFlags.Add("-r:System.Linq.Expressions.dll"); // System.Linq.IQueryable<T>
                otherFlags.Add("-r:System.Threading.Tasks.dll"); // valuetype [System.Threading.Tasks]System.Threading.CancellationToken
                otherFlags.Add("-r:System.IO.dll");  //  System.IO.TextWriter
                                                 //yield "System.Console"  //  System.Console.Out etc.
                otherFlags.Add("-r:System.Net.Requests.dll");  //  System.Net.WebResponse etc.
                otherFlags.Add("-r:System.Collections.dll"); // System.Collections.Generic.List<T>
                otherFlags.Add("-r:System.Runtime.Numerics.dll"); // BigInteger
                otherFlags.Add("-r:System.Threading.dll");  // OperationCanceledException
                otherFlags.Add("-r:System.Runtime.dll");

                if (debug)
                {
                    otherFlags.Add("--optimize-");
                    otherFlags.Add("--debug+");
                    otherFlags.Add("--tailcalls-");
                }

                // This output DLL isn't actually written by FSharp.Compiler.Service when CompileToDynamicAssembly is called
                otherFlags.Add("--out:" + Path.ChangeExtension(Path.GetTempFileName(), "exe"));

                otherFlags.Add(scriptFile);

                var outStreams = FSharpOption<Tuple<TextWriter, TextWriter>>.Some(new Tuple<TextWriter, TextWriter>(Console.Out, Console.Error));

                var result = compiler.CompileToDynamicAssembly(otherFlags: otherFlags.ToArray(), execute: outStreams);

                errors = result.Item1;
                //var exitCode = result.Item2;
                assemblyOption = result.Item3;
            }
            finally
            {
                File.Delete(scriptFile);
            }
            return new FSharpCompilation(errors, assemblyOption);
        }

        private static string GetFunctionSource(FunctionMetadata functionMetadata)
        {
            string code = null;

            if (File.Exists(functionMetadata.ScriptFile))
            {
                code = File.ReadAllText(functionMetadata.ScriptFile);
            }

            return code ?? string.Empty;
        }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
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
    internal class FSharpCompiler : IDotNetCompiler
    {
        private static readonly string[] TheWatchedFileTypes = { ".fs", ".fsx", ".dll", ".exe", ".fsi" };

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
                return ScriptType.FSharp;
            }
        }

        public ICompilation GetCompilation(string code, ScriptOptions options, InteractiveAssemblyLoader assemblyLoader, string functionName, string filename, bool debug)
        {
            // First use the C# compiler to resolve references, to get consistenct with the C# Azure Functions programming model
            Script<object> script = Microsoft.CodeAnalysis.CSharp.Scripting.CSharpScript.Create("using System;", options: options, assemblyLoader: assemblyLoader);
            Compilation compilation = script.GetCompilation();

            var compiler = new SimpleSourceCodeServices();
            var scriptFile = Path.ChangeExtension(Path.GetTempFileName(), "fsx");
            foreach (var import in options.Imports)
            {
                File.AppendAllLines(scriptFile, new string[] { "open " + import });
            }
            File.AppendAllText(scriptFile, code);
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

            otherFlags.Add("--optimize+");
            if (debug)
            {
                otherFlags.Add("--debug+");
                //otherFlags.Add("--tailcalls-");
            }

            // This output DLL isn't actually written by FSharp.Compiler.Service when CompileToDynamicAssembly is called
            otherFlags.Add("--out:" + Path.ChangeExtension(Path.GetTempFileName(), "exe"));

            otherFlags.Add(scriptFile);

            var outStreams = FSharpOption<Tuple<TextWriter, TextWriter>>.Some(new Tuple<TextWriter, TextWriter>(Console.Out, Console.Error));

            var result = compiler.CompileToDynamicAssembly(otherFlags: otherFlags.ToArray(), execute: outStreams);

            var errors = result.Item1;
            //var exitCode = result.Item2;
            var assemblyOption = result.Item3;

            return new FSharpCompilation(errors, assemblyOption);
        }
    }
}
